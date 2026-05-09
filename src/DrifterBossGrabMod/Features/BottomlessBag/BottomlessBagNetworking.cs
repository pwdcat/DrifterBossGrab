#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using HarmonyLib;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.UI;
using EntityStates.Drifter.Bag;
using EntityStateMachine = RoR2.EntityStateMachine;

namespace DrifterBossGrabMod.Networking
{
    // ========================================================================================
    // BAG STATE SYNC
    // ========================================================================================

    public static class BagStateSync
    {
        public static GameObject? AdditionalSeatPrefab { get; private set; }
        private static Harmony? _harmony;

        public static void Init(Harmony harmony)
        {
            _harmony = harmony;
            RoR2.Networking.NetworkManagerSystem.onClientConnectGlobal += OnClientConnect;
            RoR2.Networking.NetworkManagerSystem.onStartServerGlobal += OnServerStart;
            Run.onRunStartGlobal += OnRunStart;

            BodyCatalog.availability.CallWhenAvailable(() =>
            {
                AddControllerToDrifterPrefab();
            });

            CreateSeatPrefab();
        }

        private static void AddControllerToDrifterPrefab()
        {
            var drifterBody = BodyCatalog.FindBodyPrefab("DrifterBody");
            if (drifterBody && !drifterBody.GetComponent<BottomlessBagNetworkController>())
            {
                Log.DebugIfEnabled("[BagStateSync] Adding BottomlessBagNetworkController to DrifterBody prefab");
                drifterBody.AddComponent<BottomlessBagNetworkController>();
                Log.DebugIfEnabled("[BagStateSync] Successfully added BottomlessBagNetworkController to DrifterBody prefab!");
            }
        }

        private static void CreateSeatPrefab()
        {
            if (AdditionalSeatPrefab != null) return;

            AdditionalSeatPrefab = new GameObject("DrifterBossGrabAdditionalSeat");
            var ni = AdditionalSeatPrefab.AddComponent<NetworkIdentity>();
            ni.localPlayerAuthority = false;
            ni.serverOnly = false;

            var seat = AdditionalSeatPrefab.AddComponent<VehicleSeat>();

            var seatPosObj = new GameObject("SeatPosition");
            seatPosObj.transform.SetParent(AdditionalSeatPrefab.transform);
            seatPosObj.transform.localPosition = Vector3.zero;
            seat.seatPosition = seatPosObj.transform;

            var exitPosObj = new GameObject("ExitPosition");
            exitPosObj.transform.SetParent(AdditionalSeatPrefab.transform);
            exitPosObj.transform.localPosition = Vector3.zero;
            seat.exitPosition = exitPosObj.transform;

            seat.passengerState = new EntityStates.SerializableEntityStateType(typeof(EntityStates.Idle));
            seat.hidePassenger = true;
            seat.disablePassengerMotor = true;
            seat.disableAllCollidersAndHurtboxes = true;
            seat.isEquipmentActivationAllowed = true;
            seat.shouldSetIdle = true;

            var assetId = new Guid("d62f2e5a-7b3c-4e8a-9d1f-8c5e2a3b4d5e");
            ReflectionCache.NetworkIdentity.AssetId?.SetValue(ni, NetworkHash128.Parse(assetId.ToString()));
            GameObject.DontDestroyOnLoad(AdditionalSeatPrefab);
            AdditionalSeatPrefab.SetActive(false);

            ClientScene.RegisterPrefab(AdditionalSeatPrefab);
        }

        private static void OnClientConnect(NetworkConnection conn)
        {
            Log.DebugIfEnabled("[BagStateSync] OnClientConnect firing");
        }

        private static void OnServerStart()
        {
            Log.DebugIfEnabled("[BagStateSync] OnServerStart firing");
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(DelayedServerHooksInit());
            }
        }

        private static System.Collections.IEnumerator DelayedServerHooksInit()
        {
            float timeout = 5f;
            float elapsed = 0f;
            while (!NetworkServer.active && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (NetworkServer.active)
            {
                Log.DebugIfEnabled("[BagStateSync] NetworkServer.active became true after {0:F1}s, initializing server hooks", elapsed);
                PersistenceNetworkHandler.RegisterServerHooks();
            }
        }

        private static void OnRunStart(Run run)
        {
            if (NetworkServer.active)
            {
                Log.DebugIfEnabled("[BagStateSync] OnRunStart - re-initializing server hooks");
                PersistenceNetworkHandler.RegisterServerHooks();
            }
        }

        public static void Cleanup()
        {
            RoR2.Networking.NetworkManagerSystem.onClientConnectGlobal -= OnClientConnect;
            RoR2.Networking.NetworkManagerSystem.onStartServerGlobal -= OnServerStart;
            Run.onRunStartGlobal -= OnRunStart;
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }

    // ========================================================================================
    // BOTTOMLESS BAG NETWORK CONTROLLER
    // ========================================================================================

    public class BottomlessBagNetworkController : NetworkBehaviour
    {
        public int selectedIndex = -1;
        private List<uint> _baggedObjectNetIds = new List<uint>();
        private List<uint> _additionalSeatNetIds = new List<uint>();

        public IReadOnlyList<uint> BaggedObjectNetIds => _baggedObjectNetIds;
        public IReadOnlyList<uint> AdditionalSeatNetIds => _additionalSeatNetIds;

        private int _lastScrollDirection = 0;
        private int _previousSelectedIndex = -1;
        private List<float> _breakoutTimesTarget = new List<float>();
        private List<float> _elapsedBreakoutTimesTarget = new List<float>();
        private readonly Dictionary<NetworkInstanceId, GameObject> _netIdCache = new();
        private float _lastCarouselUpdateTime = 0f;
        private const float CAROUSEL_UPDATE_MIN_INTERVAL = 0.05f;

        public bool autoPromoteMainSeat;
        public bool prioritizeMainSeat;

        public bool isLocallyGrabbed;
        private float _localGrabTimer;
        private const float LOCAL_GRAB_TIMEOUT = 1.0f;
        private uint _locallyGrabbedNetId;

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnBagStateChanged();
        }

        // Ew...
        private void Update()
        {
            if (isLocallyGrabbed)
            {
                _localGrabTimer += Time.deltaTime;
                if (_localGrabTimer >= LOCAL_GRAB_TIMEOUT)
                {
                    Log.DebugIfEnabled("[BottomlessBagNetworkController] Local grab guard timed out.");
                    isLocallyGrabbed = false;
                    _locallyGrabbedNetId = 0;
                }
            }
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
            List<uint> baggedIds = new List<uint>();
            foreach (var obj in baggedObjects)
            {
                if (obj && obj.GetComponent<NetworkIdentity>() is { } ni) baggedIds.Add(ni.netId.Value);
            }
            List<uint> seatIds = new List<uint>();
            foreach (var seat in additionalSeats)
            {
                if (seat && seat.GetComponent<NetworkIdentity>() is { } ni) seatIds.Add(ni.netId.Value);
            }

            List<float> breakoutTimes = new List<float>();
            List<float> elapsedBreakoutTimes = new List<float>();
            foreach (var obj in baggedObjects)
            {
                var state = StateCalculator.GetIndividualObjectState(GetComponent<DrifterBagController>(), obj);
                breakoutTimes.Add(state?.breakoutTime ?? 0f);
                elapsedBreakoutTimes.Add(state?.elapsedBreakoutTime ?? 0f);
            }

            if (NetworkServer.active)
            {
                if (CycleNetworkHandler.SuppressBroadcasts) return;

                List<bool> collidersDisabled = new List<bool>();
                var controller = GetComponent<DrifterBagController>();
                if (controller != null)
                {
                    foreach (var id in baggedIds)
                    {
                        var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(id));
                        collidersDisabled.Add(obj != null && controller.GetComponent<BottomlessBagNetworkController>() is { } net && API.DrifterBagAPI.GetAdditionalSeats(controller).TryGetValue(obj, out var _) && true); // Simplified for now
                    }
                }

                var msg = new BagStateUpdatedMessage
                {
                    controllerNetId = GetComponent<NetworkIdentity>().netId,
                    selectedIndex = index,
                    removedObjectNetId = NetworkInstanceId.Invalid,
                    baggedIds = baggedIds.ToArray(),
                    seatIds = seatIds.ToArray(),
                    scrollDirection = direction,
                    isThrowOperation = false,
                    collidersDisabled = collidersDisabled.ToArray(),
                    breakoutTimes = breakoutTimes.ToArray(),
                    elapsedBreakoutTimes = elapsedBreakoutTimes.ToArray()
                };

                NetworkServer.SendToAll(Constants.Network.BagStateUpdatedMessageType, msg);
                UpdateLocalState(index, baggedIds, seatIds, breakoutTimes, elapsedBreakoutTimes);
            }
            else if (hasAuthority)
            {
                if (GetComponent<DrifterBagController>() is { } ctrl) CycleNetworkHandler.SendClientBagState(ctrl, index, baggedIds.ToArray(), seatIds.ToArray());
                UpdateLocalState(index, baggedIds, seatIds, breakoutTimes, elapsedBreakoutTimes);
            }
        }

        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds, int direction = 0, float[]? breakoutTimes = null, float[]? elapsedBreakoutTimes = null)
        {
            if (isLocallyGrabbed && _locallyGrabbedNetId != 0)
            {
                bool serverHasObject = false;
                foreach (var id in baggedIds)
                {
                    if (id == _locallyGrabbedNetId)
                    {
                        serverHasObject = true;
                        break;
                    }
                }

                if (!serverHasObject)
                {
                    Log.DebugIfEnabled("[ApplyStateFromMessage] Skipping server update - locally grabbed object {0} not yet reflected on server", _locallyGrabbedNetId);
                    return;
                }
                
                Log.DebugIfEnabled("[ApplyStateFromMessage] Server now reflects locally grabbed object {0}. Clearing guard.", _locallyGrabbedNetId);
                isLocallyGrabbed = false;
                _locallyGrabbedNetId = 0;
            }

            _lastScrollDirection = direction;
            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds),
                breakoutTimes != null ? new List<float>(breakoutTimes) : null,
                elapsedBreakoutTimes != null ? new List<float>(elapsedBreakoutTimes) : null);
        }

        public void ServerUpdateFromClient(int index, uint[] baggedIds, uint[] seatIds)
        {
            if (!NetworkServer.active) return;

            Log.DebugIfEnabled("[BottomlessBagNetworkController] ServerUpdateFromClient for {0}. index={1}, objects={2}",
                gameObject.name, index, baggedIds.Length);

            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds), null, null);

            if (GetComponent<DrifterBagController>() is { } controller)
            {
                TryFixNullTargetState(controller, new List<uint>(baggedIds));

                uint[] actualSeatIds = seatIds;
                var actualSeats = API.DrifterBagAPI.GetAdditionalSeats(controller);
                if (actualSeats != null && actualSeats.Count > 0)
                {
                    var seatIdList = new List<uint>();
                    foreach (var kvp in actualSeats)
                    {
                        if (kvp.Value && kvp.Value.GetComponent<NetworkIdentity>() is { } ni && ni.netId.Value != 0) seatIdList.Add(ni.netId.Value);
                    }
                    if (seatIdList.Count > 0) actualSeatIds = seatIdList.ToArray();
                }

                int correctedIndex = index;
                if (index < 0 && baggedIds.Length > 0 && API.DrifterBagAPI.GetMainPassenger(controller) is { } mainSeatObj && mainSeatObj.GetComponent<NetworkIdentity>() is { } mainNetId)
                {
                    for (int i = 0; i < baggedIds.Length; i++)
                    {
                        if (baggedIds[i] == mainNetId.netId.Value) { correctedIndex = i; break; }
                    }
                }

                List<bool> collidersDisabled = new List<bool>();
                if (controller != null)
                {
                    foreach (var id in baggedIds)
                    {
                        var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(id));
                        collidersDisabled.Add(obj != null);
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
            if (EntityStateMachine.FindByCustomName(controller.gameObject, "Bag") is { } sm && sm.state is BaggedObject baggedState && baggedState.targetObject == null)
            {
                if (NetworkServer.FindLocalObject(new NetworkInstanceId(baggedIds[0])) is { } obj)
                {
                    baggedState.targetObject = obj;
                    API.DrifterBagAPI.SetMainSeatObject(controller, obj);
                }
            }
        }

        private void UpdateLocalState(int index, List<uint> baggedIds, List<uint> seatIds, List<float>? breakoutTimes = null, List<float>? elapsedBreakoutTimes = null)
        {
            selectedIndex = index;
            _baggedObjectNetIdsTarget = baggedIds;
            _additionalSeatNetIdsTarget = seatIds;
            _breakoutTimesTarget = breakoutTimes ?? new List<float>();
            _elapsedBreakoutTimesTarget = elapsedBreakoutTimes ?? new List<float>();

            if (NetworkServer.active)
            {
                _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
                _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);
                if (GetComponent<DrifterBagController>() is { } ctrl) DoSync(ctrl, false);
            }
            else
            {
                // empty bag state
                if (index == -1)
                {
                    Log.DebugIfEnabled("[UpdateLocalState] Null seat detected");
                    _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
                    _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);
                    if (GetComponent<DrifterBagController>() is { } ctrl) DoSync(ctrl, true);
                }
                else
                {
                    OnBagStateChanged();
                }
            }
        }

        private void UpdateNetIdCache()
        {
            _netIdCache.Clear();
            foreach (var id in _baggedObjectNetIds)
            {
                var netId = new NetworkInstanceId(id);
                if ((ClientScene.FindLocalObject(netId) ?? NetworkServer.FindLocalObject(netId)) is { } obj) _netIdCache[netId] = obj;
            }
            foreach (var id in _additionalSeatNetIds)
            {
                var netId = new NetworkInstanceId(id);
                if ((ClientScene.FindLocalObject(netId) ?? NetworkServer.FindLocalObject(netId)) is { } obj) _netIdCache[netId] = obj;
            }
        }

        private List<uint> _baggedObjectNetIdsTarget = new List<uint>();
        private List<uint> _additionalSeatNetIdsTarget = new List<uint>();
        public bool IsSyncing => _syncCoroutine != null;
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

            Log.DebugIfEnabled("[BottomlessBagNetworkController] Starting state sync for {0}", controller.name);

            while (elapsed < timeout)
            {
                bool allFound = true;
                foreach (var id in _baggedObjectNetIdsTarget)
                {
                    if (id != 0 && ClientScene.FindLocalObject(new NetworkInstanceId(id)) == null && NetworkServer.FindLocalObject(new NetworkInstanceId(id)) == null) { allFound = false; break; }
                }
                if (allFound)
                {
                    foreach (var id in _additionalSeatNetIdsTarget)
                    {
                        if (id != 0 && ClientScene.FindLocalObject(new NetworkInstanceId(id)) == null && NetworkServer.FindLocalObject(new NetworkInstanceId(id)) == null) { allFound = false; break; }
                    }
                }

                if (allFound) break;
                yield return new WaitForSeconds(Constants.Timeouts.SyncWaitIncrement);
                elapsed += Constants.Timeouts.SyncWaitIncrement;
            }

            if (elapsed >= timeout) Log.DebugIfEnabled($"[SyncStateCoroutine] Timed out waiting for objects for {controller.name} after {timeout:F2}s");

            _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
            _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);

            DoSync(controller, true, _lastScrollDirection);
            _syncCoroutine = null;
        }

        private void DoSync(DrifterBagController controller, bool triggerUIUpdate, int scrollDirection = 0)
        {
            GameObject? mainSeatObject = null;
            var syncedObjects = GetBaggedObjects();
            var seats = GetAdditionalSeats();
            var additionalSeatDict = new System.Collections.Concurrent.ConcurrentDictionary<GameObject, VehicleSeat>();

            if (!NetworkServer.active)
            {
                foreach (var childSeat in controller.GetComponentsInChildren<VehicleSeat>(true))
                {
                    if (childSeat == controller.vehicleSeat) continue;
                    bool isSynced = false;
                    if (seats != null) foreach (var s in seats) if (s == childSeat) { isSynced = true; break; }
                    if (isSynced) continue;
                    if (childSeat.GetComponent<NetworkIdentity>() is not { } ni || ni.netId.Value == 0)
                    {
                        if (!childSeat.hasPassenger) UnityEngine.Object.Destroy(childSeat.gameObject);
                    }
                }
            }

            if (seats != null)
            {
                foreach (var seat in seats)
                {
                    if (seat)
                    {
                        if (seat.transform.parent != controller.transform) { seat.transform.SetParent(controller.transform); seat.transform.localPosition = Vector3.zero; seat.transform.localRotation = Quaternion.identity; }
                        if (seat.hasPassenger && seat.NetworkpassengerBodyObject is { } p) additionalSeatDict[p] = seat;
                    }
                }
            }

            if (scrollDirection != 0) { DrifterBossGrabPlugin.LastCycleClientTime = Time.time; DrifterBossGrabPlugin._isSwappingPassengers = true; }

            try
            {
                // Calculate MainSeatObject
                if (NetworkServer.active)
                {
                    foreach (var childSeat in controller.GetComponentsInChildren<VehicleSeat>(true))
                    {
                        if (childSeat == controller.vehicleSeat) continue;
                        if (childSeat && childSeat.hasPassenger && childSeat.NetworkpassengerBodyObject is { } p && !additionalSeatDict.ContainsKey(p))
                        {
                            bool isInSyncedList = false;
                            if (syncedObjects != null) foreach (var o in syncedObjects) if (o && o.GetInstanceID() == p.GetInstanceID()) { isInSyncedList = true; break; }
                            if (isInSyncedList) additionalSeatDict[p] = childSeat;
                        }
                    }
                }

                if (syncedObjects != null && selectedIndex >= 0 && selectedIndex < syncedObjects.Count)
                {
                    var potential = syncedObjects[selectedIndex];
                    if (NetworkServer.active)
                    {
                        if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger && ReferenceEquals(controller.vehicleSeat.NetworkpassengerBodyObject, potential))
                        {
                            mainSeatObject = potential;
                            additionalSeatDict.TryRemove(mainSeatObject, out _);
                        }
                    }
                    else { mainSeatObject = potential; additionalSeatDict.TryRemove(mainSeatObject, out _); }
                }

                API.DrifterBagAPI.SetAdditionalSeats(controller, additionalSeatDict);
                API.DrifterBagAPI.SetMainSeatObject(controller, mainSeatObject);

                if (!NetworkServer.active && EntityStateMachine.FindByCustomName(controller.gameObject, "Bag") is { } bagEsm)
                {
                    if (selectedIndex >= 0 && syncedObjects != null && selectedIndex < syncedObjects.Count && syncedObjects[selectedIndex] is { } currentObj)
                    {
                        if (bagEsm.state is not BaggedObject baggedState || baggedState.targetObject != currentObj)
                        {
                            Log.DebugIfEnabled("[BottomlessBagNetworkController] Forcing BaggedObject state for {0} on client (index={1})", currentObj.name, selectedIndex);
                            if (API.DrifterBagAPI.FindOrCreateBaggedObjectState(controller, currentObj) is { } newState && bagEsm.state != newState) bagEsm.SetState(newState);
                        }
                    }
                    else if (bagEsm.state is BaggedObject)
                    {
                        Log.DebugIfEnabled("[BottomlessBagNetworkController] Exiting BaggedObject state on client (selectedIndex={0})", selectedIndex);
                        bagEsm.SetNextStateToMain();
                    }
                }

                if (!NetworkServer.active && mainSeatObject != null)
                {
                    if (_previousSelectedIndex < 0 && API.DrifterBagAPI.LoadObjectState(controller, mainSeatObject) is { } stored)
                    {
                        if (API.DrifterBagAPI.FindOrCreateBaggedObjectState(controller, mainSeatObject) is { } bs) stored.ApplyToBaggedObject(bs);
                    }

                    if (API.DrifterBagAPI.FindOrCreateBaggedObjectState(controller, mainSeatObject) is { } baggedState && mainSeatObject.GetComponent<NetworkIdentity>() is { } mainNi)
                    {
                        int idx = _baggedObjectNetIds.IndexOf(mainNi.netId.Value);
                        if (idx >= 0 && idx < _breakoutTimesTarget.Count && idx < _elapsedBreakoutTimesTarget.Count)
                        {
                            float bTime = _breakoutTimesTarget[idx];
                            float eTime = _elapsedBreakoutTimesTarget[idx];
                            if (bTime > 0)
                            {
                                ReflectionCache.BaggedObject.BreakoutTime?.SetValue(baggedState, bTime);
                                ReflectionCache.EntityState.FixedAge?.SetValue(baggedState, eTime);
                                Log.DebugIfEnabled("[DoSync] Applied synced breakout timer for {0}: {1:F1}/{2:F1}s", mainSeatObject.name, eTime, bTime);
                            }
                        }
                    }
                }
            }
            finally { DrifterBossGrabPlugin._isSwappingPassengers = false; }

            if (syncedObjects != null) API.DrifterBagAPI.SetBaggedObjects(controller, syncedObjects);

            if (mainSeatObject != null && syncedObjects != null && syncedObjects.Contains(mainSeatObject) && !ProjectileRecoveryPatches.IsInProjectileState(mainSeatObject))
            {
                if (EntityStateMachine.FindByCustomName(controller.gameObject, "Bag") is { } syncBagEsm && (syncBagEsm.state is not BaggedObject baggedState || baggedState.targetObject != mainSeatObject))
                {
                    Log.DebugIfEnabled("[BottomlessBagNetworkController] Syncing BaggedObject state via reset for {0}", mainSeatObject.name);
                    syncBagEsm.SetNextState(new BaggedObject { targetObject = mainSeatObject });
                }
            }

            _previousSelectedIndex = selectedIndex;

            if (controller != null)
            {
                BagPassengerManager.ForceRecalculateMass(controller);
                if (syncedObjects != null) BagPassengerManager.MarkMassDirty(controller);
                if (triggerUIUpdate)
                {
                    if (BagPassengerManager.IsProcessingThrowRemoval) return;

                    if (Time.time - _lastCarouselUpdateTime >= CAROUSEL_UPDATE_MIN_INTERVAL)
                    {
                        BagCarouselUpdater.UpdateCarousel(controller, scrollDirection);
                        _lastCarouselUpdateTime = Time.time;

                        if (mainSeatObject != null) API.DrifterBagAPI.RefreshUIOverlayForMainSeat(controller, mainSeatObject);
                        else API.DrifterBagAPI.RemoveUIOverlayForNullState(controller);
                    }
                }
            }
        }

        public int GetTotalObjectCount()
        {
            if (NetworkServer.active) return _baggedObjectNetIds.Count;
            return Math.Max(_baggedObjectNetIds.Count, _baggedObjectNetIdsTarget.Count);
        }

        public List<GameObject> GetBaggedObjects()
        {
            if (_netIdCache.Count == 0) UpdateNetIdCache();
            List<GameObject> objects = new List<GameObject>();
            List<int> toRemove = new List<int>();

            var ids = (NetworkServer.active) ? _baggedObjectNetIds : _baggedObjectNetIdsTarget;
            for (int i = 0; i < ids.Count; i++)
            {
                var id = new NetworkInstanceId(ids[i]);
                if (_netIdCache.TryGetValue(id, out var obj) && obj) objects.Add(obj);
                else if ((ClientScene.FindLocalObject(id) ?? NetworkServer.FindLocalObject(id)) is { } f) { objects.Add(f); _netIdCache[id] = f; }
                else toRemove.Add(i);
            }

            if (NetworkServer.active)
            {
                for (int i = toRemove.Count - 1; i >= 0; i--) { _baggedObjectNetIds.RemoveAt(toRemove[i]); _baggedObjectNetIdsTarget.RemoveAt(toRemove[i]); }
            }
            return objects;
        }

        public List<VehicleSeat> GetAdditionalSeats()
        {
            List<VehicleSeat> seats = new List<VehicleSeat>();
            foreach (var idValue in _additionalSeatNetIds)
            {
                if (idValue == 0) continue;
                var id = new NetworkInstanceId(idValue);
                var obj = ClientScene.FindLocalObject(id) ?? NetworkServer.FindLocalObject(id);

                if (!obj && !NetworkServer.active)
                {
                    if (_netIdCache.TryGetValue(id, out var cached)) obj = cached;
                    else
                    {
                        foreach (var seat in GetComponentsInChildren<VehicleSeat>(true))
                        {
                            if (seat.GetComponent<NetworkIdentity>() is { } ni && ni.netId == id) { obj = seat.gameObject; _netIdCache[id] = obj; break; }
                        }
                    }
                }
                if (obj && obj.GetComponent<VehicleSeat>() is { } s) seats.Add(s);
            }
            return seats;
        }

        public void RemoveBaggedObjectId(NetworkInstanceId netId)
        {
            if (netId == NetworkInstanceId.Invalid) return;
            _baggedObjectNetIds.Remove(netId.Value);
            _baggedObjectNetIdsTarget.Remove(netId.Value);
            _netIdCache.Remove(netId);
            Log.DebugIfEnabled("[BottomlessBagNetworkController] Removed netId={0} from bagged state", netId.Value);
        }

        public void TryAddBaggedObjectId(NetworkInstanceId netId)
        {
            if (netId == NetworkInstanceId.Invalid) return;
            if (!_baggedObjectNetIds.Contains(netId.Value)) _baggedObjectNetIds.Add(netId.Value);
            if (!_baggedObjectNetIdsTarget.Contains(netId.Value)) _baggedObjectNetIdsTarget.Add(netId.Value);
        }

        public void SetLocallyGrabbed(uint netId)
        {
            isLocallyGrabbed = true;
            _locallyGrabbedNetId = netId;
            _localGrabTimer = 0f;
            Log.DebugIfEnabled("[BottomlessBagNetworkController] Local grab guard set for netId={0}", netId);
        }
    }
}

