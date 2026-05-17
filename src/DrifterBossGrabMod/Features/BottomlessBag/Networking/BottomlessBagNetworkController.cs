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

        private readonly Dictionary<NetworkInstanceId, GameObject> _netIdCache = new();

        private static readonly List<uint> _setBagStateIdBuffer = new List<uint>();
        private static readonly List<bool> _setBagStateBoolBuffer = new List<bool>();
        private static readonly List<float> _setBagStateFloatBuffer = new List<float>();
        private static readonly List<float> _setBagStateAttemptsBuffer = new List<float>();
        private static readonly List<float> _setBagStateTotalTimesBuffer = new List<float>();

        private float _lastCarouselUpdateTime = 0f;
        private const float CAROUSEL_UPDATE_MIN_INTERVAL = 0.05f;

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
                if (CycleNetworkHandler.SuppressBroadcasts) return;

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
                    baggedIds = baggedIds.ToArray(),
                    seatIds = seatIds.ToArray(),
                    scrollDirection = direction,
                    collidersDisabled = collidersDisabled.ToArray(),
                    elapsedBreakoutTimes = elapsedTimes.ToArray(),
                    breakoutAttempts = attempts.ToArray(),
                    breakoutTimes = totalTimes.ToArray()
                };

                NetworkServer.SendToAll(Constants.Network.UpdateBagStateMessageType, msg);

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
        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds, int direction, float[] elapsedTimes, float[] attempts, float[] totalTimes)
        {
            _lastScrollDirection = direction;

            var controller = GetComponent<DrifterBagController>();
            if (controller != null && baggedIds != null && elapsedTimes != null && attempts != null && totalTimes != null &&
                elapsedTimes.Length == baggedIds.Length && attempts.Length == baggedIds.Length && totalTimes.Length == baggedIds.Length)
            {
                for (int i = 0; i < baggedIds.Length; i++)
                {
                    var netId = new NetworkInstanceId(baggedIds[i]);
                    var obj = ClientScene.FindLocalObject(netId) ?? NetworkServer.FindLocalObject(netId);
                    if (obj != null)
                    {
                        var stateData = BaggedObjectPatches.LoadObjectState(controller, obj) ?? new Core.BaggedObjectStateData();
                        if (stateData.targetObject == null) stateData.CalculateFromObject(obj, controller);

                        stateData.elapsedBreakoutTime = elapsedTimes[i];
                        stateData.breakoutAttempts = (int)attempts[i];
                        stateData.breakoutTime = totalTimes[i];

                        BaggedObjectPatches.SaveObjectState(controller, obj, stateData);
                    }
                }
            }

            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
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

        public void ServerUpdateFromClient(int index, uint[] baggedIds, uint[] seatIds)
        {
            if (!NetworkServer.active) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[BottomlessBagNetworkController] ServerUpdateFromClient for {gameObject.name}. index={index}, objects={baggedIds.Length}");

            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));

            var controller = GetComponent<DrifterBagController>();
            if (controller != null)
            {
                TryFixNullTargetState(controller, new List<uint>(baggedIds));

                uint[] actualSeatIds = seatIds;
                var actualSeats = BagPatches.GetState(controller).AdditionalSeats;
                if (actualSeats != null && actualSeats.Count > 0)
                {
                    var seatIdList = new List<uint>();
                    foreach (var kvp in actualSeats)
                    {
                        if (kvp.Value != null)
                        {
                            var ni = kvp.Value.GetComponent<NetworkIdentity>();
                            if (ni != null && ni.netId.Value != 0)
                            {
                                seatIdList.Add(ni.netId.Value);
                            }
                        }
                    }
                    if (seatIdList.Count > 0)
                    {
                        actualSeatIds = seatIdList.ToArray();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[ServerUpdateFromClient] Replaced client seatIds (count={seatIds.Length}) with {actualSeatIds.Length} recovered seat IDs");
                    }
                }

                int correctedIndex = index;
                if (index < 0 && baggedIds.Length > 0)
                {
                    var mainSeatObj = BagPatches.GetMainSeatObject(controller);
                    if (mainSeatObj != null)
                    {
                        var mainNetId = mainSeatObj.GetComponent<NetworkIdentity>();
                        if (mainNetId != null)
                        {
                            for (int i = 0; i < baggedIds.Length; i++)
                            {
                                if (baggedIds[i] == mainNetId.netId.Value)
                                {
                                    correctedIndex = i;
                                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        Log.Info($"[ServerUpdateFromClient] Corrected index from {index} to {correctedIndex} for {mainSeatObj.name}");
                                    break;
                                }
                            }
                        }
                    }
                }

                List<bool> collidersDisabled = new List<bool>();
                var bagState = BagPatches.GetState(controller);
                if (bagState != null && bagState.DisabledCollidersByObject != null)
                {
                    foreach (var id in baggedIds)
                    {
                        var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(id));
                        bool disabled = false;
                        if (obj != null && bagState.DisabledCollidersByObject.TryGetValue(obj, out var disabledColliders))
                        {
                            disabled = disabledColliders.Count > 0;
                        }
                        collidersDisabled.Add(disabled);
                    }
                }

                var msg = new UpdateBagStateMessage
                {
                    controllerNetId = GetComponent<NetworkIdentity>().netId,
                    selectedIndex = correctedIndex,
                    baggedIds = baggedIds,
                    seatIds = actualSeatIds,
                    scrollDirection = 0,
                    collidersDisabled = collidersDisabled.ToArray()
                };
                NetworkServer.SendToAll(Constants.Network.UpdateBagStateMessageType, msg);

                if (actualSeatIds != seatIds)
                {
                    _additionalSeatNetIds = new List<uint>(actualSeatIds);
                    _additionalSeatNetIdsTarget = new List<uint>(actualSeatIds);
                }
            }
        }
        private void TryFixNullTargetState(DrifterBagController controller, List<uint> baggedIds)
        {
            if (!NetworkServer.active || baggedIds.Count == 0) return;

            var stateMachines = controller.GetComponentsInChildren<EntityStateMachine>(true);
            foreach (var sm in stateMachines)
            {
                if (sm.state is BaggedObject baggedState && baggedState.targetObject == null)
                {
                    var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(baggedIds[0]));
                    if (obj != null)
                    {
                        baggedState.targetObject = obj;
                        BagPatches.SetMainSeatObject(controller, obj);
                    }
                    break;
                }
            }
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

            float timeout = Constants.Timeouts.SyncStateTimeout;
            float elapsed = 0f;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[BottomlessBagNetworkController] Starting state sync for {controller.name}");
            }

            while (elapsed < timeout)
            {
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

            DoSync(controller, true, _lastScrollDirection);
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
                if (ctx.SyncedObjects != null && (state.BaggedObjects == null || ctx.SyncedObjects.Count >= state.BaggedObjects.Count))
                {
                    state.BaggedObjects = ctx.SyncedObjects;
                }

                if (!NetworkServer.active)
                {
                    DoSync_Client_Actions(controller, ref ctx);
                }

                DoSync_Shared(controller, ref ctx, triggerUIUpdate, scrollDirection, oldMain);
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

            if (ctx.SyncedObjects != null && ctx.AdditionalSeatDict != null && controller != null)
            {
                foreach (var obj in ctx.SyncedObjects)
                {
                    if (obj == null) continue;
                    if (obj == ctx.MainSeatObject) continue;
                    if (!ctx.AdditionalSeatDict.TryGetValue(obj, out var seat)) continue;
                    if (seat == null) continue;

                    if (ProjectileRecoveryPatches.IsInProjectileState(obj)) continue;

                    if (obj.transform.parent != controller.transform)
                    {
                        obj.transform.SetParent(controller.transform);
                        obj.transform.localPosition = Vector3.zero;
                        obj.transform.localRotation = Quaternion.identity;
                    }

                    var storedState = BaggedObjectPatches.LoadObjectState(controller, obj);
                    if (storedState != null)
                    {
                        var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(controller, obj);
                        if (baggedState != null)
                        {
                            storedState.ApplyToBaggedObject(baggedState);
                        }
                    }

                    var body = obj.GetComponent<CharacterBody>();
                    if (body != null && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable))
                    {
                        var bagState = BagPatches.GetState(controller);
                        if (bagState != null && !bagState.DisabledCollidersByObject.ContainsKey(obj))
                        {
                            bagState.DisabledCollidersByObject[obj] = new Dictionary<Collider, bool>();
                        }
                        if (bagState != null)
                        {
                            BodyColliderCache.DisableMovementColliders(obj, bagState.DisabledCollidersByObject[obj]);
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
                BagPassengerManager.ForceRecalculateMass(controller);
                if (ctx.SyncedObjects != null)
                {
                    BagPassengerManager.MarkMassDirty(controller);
                }
                if (triggerUIUpdate)
                {
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
                        BagCarouselUpdater.UpdateCarousel(controller, scrollDirection);
                        _lastCarouselUpdateTime = Time.time;

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
