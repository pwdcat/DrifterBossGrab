#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    internal struct DoSyncContext
    {
        public GameObject? MainSeatObject;
        public List<GameObject> SyncedObjects;
        public List<VehicleSeat> Seats;
        public System.Collections.Concurrent.ConcurrentDictionary<GameObject, VehicleSeat> AdditionalSeatDict;

        public DoSyncContext(List<GameObject> syncedObjects, List<VehicleSeat> seats)
        {
            MainSeatObject = null;
            SyncedObjects = syncedObjects;
            Seats = seats;
            AdditionalSeatDict = new System.Collections.Concurrent.ConcurrentDictionary<GameObject, VehicleSeat>();
        }
    }

    public class BottomlessBagNetworkController : NetworkBehaviour
    {
        private static readonly MethodInfo _tryOverrideUtilityMethod = ReflectionCache.BaggedObject.TryOverrideUtility;
        private static readonly MethodInfo _tryOverridePrimaryMethod = ReflectionCache.BaggedObject.TryOverridePrimary;

        public int selectedIndex = -1;

        private List<uint> _baggedObjectNetIds = new List<uint>();
        private List<uint> _additionalSeatNetIds = new List<uint>();
        private int _lastScrollDirection = 0;
        private int _previousSelectedIndex = -1;
        private bool _hasCarouselSelection;
        private GameObject? _lastCarouselSelection;

        private readonly Dictionary<NetworkInstanceId, GameObject> _netIdCache = new();

        private static readonly List<uint> _setBagStateIdBuffer = new List<uint>();
        private static readonly List<bool> _setBagStateBoolBuffer = new List<bool>();
        private static readonly List<float> _setBagStateFloatBuffer = new List<float>();
        private static readonly List<float> _setBagStateAttemptsBuffer = new List<float>();
        private static readonly List<float> _setBagStateTotalTimesBuffer = new List<float>();

        private float _lastCarouselUpdateTime = 0f;
        private const float CAROUSEL_UPDATE_MIN_INTERVAL = 0.05f;
        private bool _carouselUpdatePending;
        private int _pendingCarouselDirection;

        private readonly Dictionary<uint, (float progress, float attempts, float interval, float receivedAt)> _breakoutPresentation = new();
        private float _nextBagPresentationTime;
        private bool _sentBagPresentation;

        private void LateUpdate()
        {
            if (!_carouselUpdatePending || BagPassengerManager.IsProcessingThrowRemoval ||
                Time.time - _lastCarouselUpdateTime < CAROUSEL_UPDATE_MIN_INTERVAL) return;
            var controller = GetComponent<DrifterBagController>();
            if (controller == null) return;
            _carouselUpdatePending = false;
            BagCarouselUpdater.UpdateCarousel(controller, _pendingCarouselDirection);
            _lastCarouselUpdateTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Time.time < _nextBagPresentationTime) return;
            _nextBagPresentationTime = Time.time + 0.1f;
            var controller = GetComponent<DrifterBagController>();
            if (controller == null) return;
            var passengers = BagPatches.GetState(controller).BaggedObjects;
            if (passengers.Count == 0 && !_sentBagPresentation) return;
            var ids = new List<uint>();
            var progress = new List<float>();
            var attempts = new List<float>();
            var intervals = new List<float>();
            var durabilityIds = new List<uint>();
            var durability = new List<int>();
            var maxDurability = new List<int>();
            foreach (var passenger in passengers)
            {
                if (passenger == null) continue;
                var identity = passenger.GetComponent<NetworkIdentity>();
                if (identity == null || identity.netId == NetworkInstanceId.Invalid) continue;
                var attributes = passenger.GetComponent<SpecialObjectAttributes>();
                if (attributes != null && attributes.maxDurability > 0 &&
                    (passenger.GetComponent<CharacterBody>() == null || attributes.overrideBody))
                {
                    durabilityIds.Add(identity.netId.Value);
                    durability.Add(attributes.durability);
                    maxDurability.Add(attributes.maxDurability);
                }
                if (!PluginConfig.Instance.BottomlessBagEnabled.Value || DrifterBagController.bagDisableBreakout ||
                    !UI.BreakoutWarningOverlay.TryGetProgress(passenger, controller,
                        out float value, out float attempt, out float interval)) continue;
                ids.Add(identity.netId.Value);
                progress.Add(value);
                attempts.Add(attempt);
                intervals.Add(interval);
            }
            if (ids.Count == 0 && durabilityIds.Count == 0 && !_sentBagPresentation) return;
            _sentBagPresentation = ids.Count > 0 || durabilityIds.Count > 0;
            NetworkMessageRegistry.SendToAll(Constants.Network.BreakoutPresentationSubMessageType, new BreakoutPresentationMessage
            {
                controllerNetId = netId,
                passengerIds = ids.ToArray(),
                progress = progress.ToArray(),
                attempts = attempts.ToArray(),
                intervals = intervals.ToArray(),
                durabilityPassengerIds = durabilityIds.ToArray(),
                durability = durability.ToArray(),
                maxDurability = maxDurability.ToArray()
            });
        }

        internal void ApplyBreakoutPresentation(BreakoutPresentationMessage message)
        {
            if (NetworkServer.active) return;
            _breakoutPresentation.Clear();
            for (int i = 0; i < message.passengerIds.Length; i++)
            {
                float interval = message.intervals[i];
                float progress = message.progress[i];
                float attempts = message.attempts[i];
                if (interval <= 0f || float.IsNaN(interval) || float.IsInfinity(interval) ||
                    float.IsNaN(progress) || float.IsInfinity(progress) ||
                    attempts < 0f || float.IsNaN(attempts) || float.IsInfinity(attempts)) continue;
                _breakoutPresentation[message.passengerIds[i]] = (Mathf.Clamp01(progress), attempts, interval, Time.time);
            }
            bool durabilityChanged = false;
            for (int i = 0; i < message.durabilityPassengerIds.Length; i++)
            {
                int maximum = message.maxDurability[i];
                if (maximum <= 0 || message.durabilityPassengerIds[i] == 0) continue;
                var passenger = ClientScene.FindLocalObject(new NetworkInstanceId(message.durabilityPassengerIds[i]));
                var attributes = passenger != null ? passenger.GetComponent<SpecialObjectAttributes>() : null;
                if (attributes == null) continue;
                int current = Mathf.Clamp(message.durability[i], 0, maximum);
                if (attributes.durability == current && attributes.maxDurability == maximum) continue;
                attributes.maxDurability = maximum;
                attributes.durability = current;
                durabilityChanged = true;
            }
            if (durabilityChanged) UI.DamagePreviewOverlay.InvalidateAllCaches();
        }

        internal bool TryGetBreakoutPresentation(GameObject passenger, out float progress, out float attempts, out float interval)
        {
            progress = attempts = interval = 0f;
            var identity = passenger.GetComponent<NetworkIdentity>();
            if (identity == null || !_breakoutPresentation.TryGetValue(identity.netId.Value, out var state) ||
                Time.time - state.receivedAt > 1f) return false;
            interval = state.interval;
            attempts = state.attempts;
            progress = Mathf.Clamp01(state.progress + Mathf.Max(0f, Time.time - state.receivedAt) / interval);
            return true;
        }

        public bool autoPromoteMainSeat;
        public bool prioritizeMainSeat;

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnBagStateChanged();
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            var ni = GetComponent<NetworkIdentity>();
            if (ni != null)
            {
                CycleNetworkHandler.SendClientPreferences(ni, PluginConfig.Instance.AutoPromoteMainSeat.Value, PluginConfig.Instance.PrioritizeMainSeat.Value);
            }
        }

        [Server]
        public void SetBagState(int index, List<GameObject> baggedObjects, List<GameObject> additionalSeats, int direction = 0)
        {
            var baggedIds = _setBagStateIdBuffer;
            baggedIds.Clear();
            foreach (var obj in baggedObjects)
            {
                if (obj)
                {
                    var ni = obj.GetComponent<NetworkIdentity>();
                    if (ni) baggedIds.Add(ni.netId.Value);
                }
            }
            List<uint> seatIds = new List<uint>();
            foreach (var seat in additionalSeats)
            {
                if (seat)
                {
                    var ni = seat.GetComponent<NetworkIdentity>();
                    if (ni) seatIds.Add(ni.netId.Value);
                }
            }
            if (NetworkServer.active)
            {
                var bagController = GetComponent<DrifterBagController>();
                var bagState = bagController != null ? BagPatches.GetState(bagController) : null;

                var collidersDisabled = _setBagStateBoolBuffer;
                collidersDisabled.Clear();
                var elapsedTimes = _setBagStateFloatBuffer;
                elapsedTimes.Clear();
                var attempts = _setBagStateAttemptsBuffer;
                attempts.Clear();
                var totalTimes = _setBagStateTotalTimesBuffer;
                totalTimes.Clear();

                foreach (var id in baggedIds)
                {
                    var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(id));
                    bool disabled = false;
                    float elapsed = 0f;
                    float attemptCount = 0f;
                    float totalTime = 0f;

                    if (obj != null && bagController != null)
                    {
                        if (bagState != null && bagState.DisabledCollidersByObject != null && bagState.DisabledCollidersByObject.TryGetValue(obj, out var disabledColliders))
                        {
                            disabled = disabledColliders.Count > 0;
                        }

                        var storedState = BaggedObjectPatches.LoadObjectState(bagController, obj);
                        if (storedState != null)
                        {
                            elapsed = storedState.elapsedBreakoutTime;
                            attemptCount = storedState.breakoutAttempts;
                            totalTime = storedState.breakoutTime;
                        }
                    }

                    collidersDisabled.Add(disabled);
                    elapsedTimes.Add(elapsed);
                    attempts.Add(attemptCount);
                    totalTimes.Add(totalTime);
                }

                var msg = new UpdateBagStateMessage
                {
                    controllerNetId = GetComponent<NetworkIdentity>().netId,
                    selectedIndex = index,
                    emptySlotIndex = bagState?.EmptySlotIndex ?? -1,
                    baggedIds = baggedIds.ToArray(),
                    seatIds = seatIds.ToArray(),
                    scrollDirection = direction,
                    collidersDisabled = collidersDisabled.ToArray(),
                    elapsedBreakoutTimes = elapsedTimes.ToArray(),
                    breakoutAttempts = attempts.ToArray(),
                    breakoutTimes = totalTimes.ToArray()
                };
                NetworkMessageRegistry.SendToAll(Constants.Network.UpdateBagStateSubMessageType, msg);

                UpdateLocalState(index, baggedIds, seatIds);
            }
            else if (hasAuthority)
            {
                var controller = GetComponent<DrifterBagController>();
                if (controller != null)
                {
                    CycleNetworkHandler.SendClientBagState(controller!, index, baggedIds.ToArray(), seatIds.ToArray());
                }

                UpdateLocalState(index, baggedIds, seatIds);
            }
        }
        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds, int direction, float[] elapsedTimes, float[] attempts, float[] totalTimes, int emptySlotIndex)
        {
            var controller = GetComponent<DrifterBagController>();
            if (controller != null && !NetworkServer.active)
                BagPatches.GetState(controller).EmptySlotIndex = emptySlotIndex < 0 ? -1 : Mathf.Clamp(emptySlotIndex, 0, baggedIds.Length);
            _lastScrollDirection = direction;

            if (!NetworkServer.active)
            {
                foreach (var id in _baggedObjectNetIdsTarget)
                {
                    if (Array.IndexOf(baggedIds, id) < 0) PersistenceNetworkHandler.CancelPendingPersistence(id);
                }
            }
            _receivedElapsedTimes = elapsedTimes;
            _receivedAttempts = attempts;
            _receivedTotalTimes = totalTimes;
            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
        }

        private void ApplyReceivedObjectStates(DrifterBagController controller)
        {
            if (_receivedElapsedTimes.Length != _baggedObjectNetIds.Count ||
                _receivedAttempts.Length != _baggedObjectNetIds.Count ||
                _receivedTotalTimes.Length != _baggedObjectNetIds.Count) return;
            for (int i = 0; i < _baggedObjectNetIds.Count; i++)
            {
                var obj = ClientScene.FindLocalObject(new NetworkInstanceId(_baggedObjectNetIds[i]));
                if (obj == null) continue;
                var stateData = BaggedObjectPatches.LoadObjectState(controller, obj) ?? new Core.BaggedObjectStateData();
                if (stateData.targetObject == null) stateData.CalculateFromObject(obj, controller);
                stateData.elapsedBreakoutTime = _receivedElapsedTimes[i];
                stateData.breakoutAttempts = (int)_receivedAttempts[i];
                stateData.breakoutTime = _receivedTotalTimes[i];
                BaggedObjectPatches.SaveObjectState(controller, obj, stateData);
            }
        }

        private bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;

            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                if (controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    return true;
            }

            var seatDict = BagPatches.GetState(controller).AdditionalSeats;
            if (seatDict != null)
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && kvp.Value.hasPassenger && kvp.Value.NetworkpassengerBodyObject == obj)
                        return true;
                }
            }

            var childSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
            foreach (var seat in childSeats)
            {
                if (seat != controller.vehicleSeat && seat.hasPassenger && seat.NetworkpassengerBodyObject == obj)
                    return true;
            }

            return false;
        }

        public void SendAuthoritativeBagState()
        {
            if (!NetworkServer.active) return;
            var controller = GetComponent<DrifterBagController>();
            if (controller != null) BagCarouselUpdater.UpdateNetworkBagState(controller);
        }
        private void UpdateLocalState(int index, List<uint> baggedIds, List<uint> seatIds)
        {
            selectedIndex = index;
            _baggedObjectNetIdsTarget = baggedIds;
            _additionalSeatNetIdsTarget = seatIds;

            if (NetworkServer.active)
            {
                _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
                _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);

                var controller = GetComponent<DrifterBagController>();
                if (controller) DoSync(controller, false);
            }
            else
            {
                OnBagStateChanged();
            }
        }

        private void UpdateNetIdCache()
        {
            _netIdCache.Clear();
            foreach (var id in _baggedObjectNetIds)
            {
                var netId = new NetworkInstanceId(id);
                var obj = ClientScene.FindLocalObject(netId) ?? NetworkServer.FindLocalObject(netId);
                if (obj) _netIdCache[netId] = obj;
            }
            foreach (var id in _additionalSeatNetIds)
            {
                var netId = new NetworkInstanceId(id);
                var obj = ClientScene.FindLocalObject(netId) ?? NetworkServer.FindLocalObject(netId);
                if (obj) _netIdCache[netId] = obj;
            }
        }
        private List<uint> _baggedObjectNetIdsTarget = new List<uint>();
        private List<uint> _additionalSeatNetIdsTarget = new List<uint>();

        private float[] _receivedElapsedTimes = Array.Empty<float>();
        private float[] _receivedAttempts = Array.Empty<float>();
        private float[] _receivedTotalTimes = Array.Empty<float>();

        private Coroutine? _syncCoroutine;
        private void OnBagStateChanged()
        {
            if (NetworkServer.active) return;

            if (_syncCoroutine != null) StopCoroutine(_syncCoroutine);
            _syncCoroutine = StartCoroutine(SyncStateCoroutine());
        }
        private System.Collections.IEnumerator SyncStateCoroutine()
        {
            var controller = GetComponent<DrifterBagController>();
            if (!controller) yield break;

            float timeout = Mathf.Max(Constants.Timeouts.SyncStateTimeout, 5f);
            float elapsed = 0f;
            var run = Run.instance;
            var client = NetworkManager.singleton?.client;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[BottomlessBagNetworkController] Starting state sync for {controller.name}");
            }

            while (elapsed < timeout)
            {
                if (Run.instance != run || client == null || !client.isConnected || NetworkManager.singleton?.client != client)
                {
                    _syncCoroutine = null;
                    yield break;
                }
                bool allFound = true;

                foreach (var idValue in _baggedObjectNetIdsTarget)
                {
                    if (idValue == 0) continue;
                    var foundObj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue)) ?? NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (foundObj == null)
                    {
                        allFound = false;
                        break;
                    }
                }

                if (allFound)
                {
                    foreach (var idValue in _additionalSeatNetIdsTarget)
                    {
                        if (idValue == 0) continue;
                        var foundObj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue)) ?? NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                        if (foundObj == null)
                        {
                            allFound = false;
                            break;
                        }
                    }
                }

                if (allFound)
                {
                    break;
                }

                yield return new WaitForSeconds(Constants.Timeouts.SyncWaitIncrement);
                elapsed += Constants.Timeouts.SyncWaitIncrement;
            }

            if (elapsed >= timeout)
            {
                Log.Warning($"[SyncStateCoroutine] Timed out waiting for objects after {timeout:F2}s");
            }

            _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
            _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);

            ApplyReceivedObjectStates(controller);
            int direction = _lastScrollDirection;
            _lastScrollDirection = 0;
            DoSync(controller, true, direction);
            BaggedObjectPatches.ClearAllTemporaryPreservation(controller);
            _syncCoroutine = null;
        }
        private void DoSync(DrifterBagController? controller, bool triggerUIUpdate, int scrollDirection = 0)
        {
            if (controller == null) return;

            if (scrollDirection != 0)
            {
                DrifterBossGrabPlugin.LastCycleClientTime = UnityEngine.Time.time;
                DrifterBossGrabPlugin._isSwappingPassengers = true;
            }

            try
            {
                var syncedObjects = GetBaggedObjects();
                var seats = GetAdditionalSeats();
                var ctx = new DoSyncContext(syncedObjects, seats);

                if (NetworkServer.active)
                {
                    DoSync_Server(controller, ref ctx);
                }
                else
                {
                    DoSync_Client_Populate(controller, ref ctx);
                }

                ResolveMainSeatObject(controller, ref ctx, NetworkServer.active);

                var state = BagPatches.GetState(controller);
                var oldMain = state.MainSeatObject;

                state.AdditionalSeats = ctx.AdditionalSeatDict;
                BagPatches.SetMainSeatObject(controller, ctx.MainSeatObject);
                if (ctx.SyncedObjects != null)
                {
                    state.BaggedObjects = ctx.SyncedObjects;
                }

                if (!NetworkServer.active)
                {
                    DoSync_Client_Actions(controller, ref ctx);
                }

                int carouselDirection = scrollDirection;
                if (!NetworkServer.active && triggerUIUpdate)
                {
                    if (_hasCarouselSelection && ReferenceEquals(_lastCarouselSelection, ctx.MainSeatObject))
                        carouselDirection = 0;
                    _lastCarouselSelection = ctx.MainSeatObject;
                    _hasCarouselSelection = true;
                }
                DoSync_Shared(controller, ref ctx, triggerUIUpdate, carouselDirection, oldMain);
            }
            finally
            {
                if (scrollDirection != 0)
                {
                    DrifterBossGrabPlugin._isSwappingPassengers = false;
                }
            }
        }

        private void DoSync_Server(DrifterBagController? controller, ref DoSyncContext ctx)
        {
            if (controller == null) return;
            var allChildSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
            if (allChildSeats != null)
            {
                foreach (var childSeat in allChildSeats)
                {
                    if (childSeat == controller.vehicleSeat) continue;

                    if (childSeat != null && childSeat.hasPassenger)
                    {
                        var passenger = childSeat.NetworkpassengerBodyObject;
                        if (passenger != null && !ctx.AdditionalSeatDict.ContainsKey(passenger))
                        {
                            bool isInSyncedList = false;
                            foreach (var syncedObj in ctx.SyncedObjects)
                            {
                                if (syncedObj != null && syncedObj.GetInstanceID() == passenger.GetInstanceID())
                                {
                                    isInSyncedList = true;
                                    break;
                                }
                            }
                            if (isInSyncedList)
                            {
                                ctx.AdditionalSeatDict[passenger] = childSeat;
                            }
                        }
                    }
                }
            }
        }

        private void DoSync_Client_Populate(DrifterBagController? controller, ref DoSyncContext ctx)
        {
            if (controller == null) return;
            var allChildSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
            foreach (var childSeat in allChildSeats)
            {
                if (childSeat == controller.vehicleSeat) continue;

                bool isSynced = false;
                if (ctx.Seats != null)
                {
                    foreach (var syncedSeat in ctx.Seats)
                    {
                        if (syncedSeat == childSeat)
                        {
                            isSynced = true;
                            break;
                        }
                    }
                }
                if (isSynced) continue;
                var ni = childSeat.GetComponent<NetworkIdentity>();
                bool isLocalSeat = ni == null || ni.netId.Value == 0;

                if (isLocalSeat)
                {
                    if (!childSeat.hasPassenger)
                    {
                        UnityEngine.Object.Destroy(childSeat.gameObject);
                    }
                }
            }
        }

        private void DoSync_Client_Actions(DrifterBagController? controller, ref DoSyncContext ctx)
        {
            if (controller == null) return;

            if (ctx.SyncedObjects != null)
            {
                foreach (var obj in ctx.SyncedObjects)
                {
                    if (obj == null) continue;

                    if (obj.GetComponent<RoR2.SpecialObjectAttributes>() == null && PluginConfig.IsGrabbable(obj))
                    {
                        Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
                    }

                    if (!PersistenceObjectsTracker.IsObjectCurrentlyBagged(obj))
                    {
                        PersistenceObjectsTracker.TrackBaggedObject(obj);
                    }

                    PersistenceObjectsTracker.SetBaggedObjectVisibility(obj, false);
                }
            }

            if (ctx.MainSeatObject != null)
            {
                bool wasNullState = _previousSelectedIndex < 0;

                if (wasNullState)
                {
                    var storedState = BaggedObjectPatches.LoadObjectState(controller, ctx.MainSeatObject);
                    if (storedState != null)
                    {
                        var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(controller, ctx.MainSeatObject);
                        if (baggedState != null)
                        {
                            storedState.ApplyToBaggedObject(baggedState);
                        }
                    }
                }
            }

            if (ctx.MainSeatObject != null && controller != null
                && ctx.SyncedObjects != null && ctx.SyncedObjects.Contains(ctx.MainSeatObject)
                && !ProjectileRecoveryPatches.IsInProjectileState(ctx.MainSeatObject))
            {
                if (ctx.MainSeatObject.transform.parent != null && !PersistenceObjectManager.IsObjectPersisted(ctx.MainSeatObject))
                {
                    Log.Debug($"[DoSync_Client_Actions] Unparenting main seat object {ctx.MainSeatObject.name} from {ctx.MainSeatObject.transform.parent.name}");
                    ctx.MainSeatObject.transform.SetParent(null, true);
                }

                var baggedObject = BaggedObjectPatches.FindOrCreateBaggedObjectState(controller, ctx.MainSeatObject);
                if (baggedObject != null)
                {
                    baggedObject.targetObject = ctx.MainSeatObject;
                    BaggedObjectPatches.UpdateTargetFields(baggedObject);

                    var skillLocator = baggedObject.outer?.GetComponent<SkillLocator>();
                    if (skillLocator != null)
                    {
                        if (skillLocator.utility != null)
                        {
                            _tryOverrideUtilityMethod?.Invoke(baggedObject, new object[] { skillLocator.utility });
                        }
                        if (skillLocator.primary != null)
                        {
                            _tryOverridePrimaryMethod?.Invoke(baggedObject, new object[] { skillLocator.primary });
                        }
                    }
                }
            }
            else if (controller != null && ctx.MainSeatObject == null)
            {
                BaggedObjectStatePatches.ForceCleanupOverrides(controller, null);
            }

            if (ctx.SyncedObjects != null && ctx.AdditionalSeatDict != null && controller != null)
            {
                foreach (var obj in ctx.SyncedObjects)
                {
                    if (obj == null) continue;
                    if (obj == ctx.MainSeatObject) continue;
                    if (!ctx.AdditionalSeatDict.TryGetValue(obj, out var seat)) continue;
                    if (seat == null) continue;

                    if (ProjectileRecoveryPatches.IsInProjectileState(obj)) continue;

                    if (obj.transform.parent != controller.transform && !PersistenceObjectManager.IsObjectPersisted(obj))
                    {
                        obj.transform.SetParent(controller.transform);
                        obj.transform.localPosition = Vector3.zero;
                        obj.transform.localRotation = Quaternion.identity;
                    }

                    var storedState = BaggedObjectPatches.LoadObjectState(controller, obj);
                    if (storedState != null)
                    {
                        var charBody = obj.GetComponent<CharacterBody>();
                        if (charBody != null)
                        {
                            storedState.ApplyToCharacterBody(charBody);
                        }
                    }
                }
            }
        }

        private void ResolveMainSeatObject(DrifterBagController? controller, ref DoSyncContext ctx, bool isServer)
        {
            if (ctx.Seats != null)
            {
                foreach (var seat in ctx.Seats)
                {
                    if (seat != null)
                    {
                        if (seat != null && controller != null && seat.transform.parent != controller.transform)
                        {
                            seat.transform.SetParent(controller.transform);
                            seat.transform.localPosition = Vector3.zero;
                            seat.transform.localRotation = Quaternion.identity;
                        }
                        if (seat != null && controller != null && controller.vehicleSeat != null && seat != controller.vehicleSeat)
                        {
                            seat.seatPosition = controller.vehicleSeat.seatPosition;
                            seat.exitPosition = controller.vehicleSeat.exitPosition;
                        }
                        if (seat != null && seat.hasPassenger)
                        {
                            var passengerObj = seat!.NetworkpassengerBodyObject;
                            if (passengerObj != null)
                            {
                                ctx.AdditionalSeatDict[passengerObj] = seat;
                            }
                        }
                    }
                }
            }

            if (ctx.SyncedObjects != null && selectedIndex >= 0 && selectedIndex < ctx.SyncedObjects.Count)
            {
                var potentialMainSeatObject = ctx.SyncedObjects[selectedIndex];

                bool isActuallyInMainSeat = false;
                if (isServer)
                {
                    if (controller != null && controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
                    {
                        if (ReferenceEquals(controller.vehicleSeat.NetworkpassengerBodyObject, potentialMainSeatObject))
                        {
                            isActuallyInMainSeat = true;
                        }
                    }
                }
                else
                {
                    isActuallyInMainSeat = true;
                }

                if (isActuallyInMainSeat)
                {
                    ctx.MainSeatObject = potentialMainSeatObject;
                    if (ctx.MainSeatObject != null)
                    {
                        ctx.AdditionalSeatDict.TryRemove(ctx.MainSeatObject, out _);
                    }
                }
            }
        }

        private void DoSync_Shared(DrifterBagController? controller, ref DoSyncContext ctx, bool triggerUIUpdate, int scrollDirection, GameObject? oldMain)
        {
            _previousSelectedIndex = selectedIndex;

            if (controller != null)
            {
                if (ctx.MainSeatObject == null || (oldMain != null && ctx.MainSeatObject != oldMain))
                {
                    BaggedObjectStatePatches.ForceCleanupOverrides(controller, oldMain);
                }

                BagPassengerManager.ForceRecalculateMass(controller);

                if (triggerUIUpdate)
                {
                    if (!_carouselUpdatePending || scrollDirection != 0) _pendingCarouselDirection = scrollDirection;
                    _carouselUpdatePending = true;
                    if (Patches.BagPassengerManager.IsProcessingThrowRemoval)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[DoSync] Skipping carousel update - processing throw removal");
                        }
                        return;
                    }

                    float timeSinceLastUpdate = Time.time - _lastCarouselUpdateTime;
                    if (timeSinceLastUpdate >= CAROUSEL_UPDATE_MIN_INTERVAL)
                    {
                        _carouselUpdatePending = false;
                        BagCarouselUpdater.UpdateCarousel(controller, _pendingCarouselDirection);
                        _lastCarouselUpdateTime = Time.time;
                    }

                    bool mainSeatChanged = (ctx.MainSeatObject != oldMain);
                    if (mainSeatChanged && ctx.MainSeatObject != null)
                    {
                        BaggedObjectPatches.RefreshUIOverlayForMainSeat(controller, ctx.MainSeatObject);
                    }
                    else if (ctx.MainSeatObject == null)
                    {
                        BaggedObjectPatches.RemoveUIOverlayForNullState(controller);
                    }
                }
            }
        }
        public int GetTotalObjectCount()
        {
            if (UnityEngine.Networking.NetworkServer.active) return _baggedObjectNetIds.Count;

            return Math.Max(_baggedObjectNetIds.Count, _baggedObjectNetIdsTarget.Count);
        }

        public List<GameObject> GetBaggedObjects()
        {
            if (_netIdCache.Count == 0)
            {
                UpdateNetIdCache();
            }

            List<GameObject> objects = new List<GameObject>();
            List<int> indicesToRemove = new List<int>();

            for (int i = 0; i < _baggedObjectNetIds.Count; i++)
            {
                var idValue = _baggedObjectNetIds[i];
                var id = new NetworkInstanceId(idValue);

                if (_netIdCache.TryGetValue(id, out var obj) && obj)
                {
                    objects.Add(obj);
                }
                else
                {
                    var fallbackObj = ClientScene.FindLocalObject(id) ?? NetworkServer.FindLocalObject(id);
                    if (fallbackObj)
                    {
                        objects.Add(fallbackObj);
                        _netIdCache[id] = fallbackObj;
                    }
                    else
                    {
                        indicesToRemove.Add(i);
                    }
                }
            }

            if (UnityEngine.Networking.NetworkServer.active)
            {
                for (int i = indicesToRemove.Count - 1; i >= 0; i--)
                {
                    _baggedObjectNetIds.RemoveAt(indicesToRemove[i]);
                    _baggedObjectNetIdsTarget.RemoveAt(indicesToRemove[i]);
                }
            }

            return objects;
        }

        public List<VehicleSeat> GetAdditionalSeats()
        {
            List<VehicleSeat> seats = new List<VehicleSeat>();
            foreach (var idValue in _additionalSeatNetIds)
            {
                var id = new NetworkInstanceId(idValue);
                var obj = ClientScene.FindLocalObject(id) ?? NetworkServer.FindLocalObject(id);
                if (obj)
                {
                    var seat = obj.GetComponent<VehicleSeat>();
                    if (seat) seats.Add(seat);
                }
            }
            return seats;
        }

        public void RemoveBaggedObjectId(NetworkInstanceId netId)
        {
            if (netId == NetworkInstanceId.Invalid) return;

            _baggedObjectNetIds.Remove(netId.Value);
            _baggedObjectNetIdsTarget.Remove(netId.Value);

            _netIdCache.Remove(netId);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[BottomlessBagNetworkController] Removed netId={netId.Value} from bagged state");
            }
        }

        public void TryAddBaggedObjectId(UnityEngine.Networking.NetworkInstanceId netId)
        {
            if (netId == UnityEngine.Networking.NetworkInstanceId.Invalid) return;

            if (!_baggedObjectNetIds.Contains(netId.Value))
            {
                _baggedObjectNetIds.Add(netId.Value);
            }
            if (!_baggedObjectNetIdsTarget.Contains(netId.Value))
            {
                _baggedObjectNetIdsTarget.Add(netId.Value);
            }
        }
    }
}
