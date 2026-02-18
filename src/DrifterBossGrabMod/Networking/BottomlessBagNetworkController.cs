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
    public class BottomlessBagNetworkController : NetworkBehaviour
    {
        // Cached reflection methods
        private static readonly MethodInfo _tryOverrideUtilityMethod = HarmonyLib.AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
        private static readonly MethodInfo _tryOverridePrimaryMethod = HarmonyLib.AccessTools.Method(typeof(BaggedObject), "TryOverridePrimary", new System.Type[] { typeof(GenericSkill) });

        [SyncVar]
        public int selectedIndex = -1;

        private List<uint> _baggedObjectNetIds = new List<uint>();
        private List<uint> _additionalSeatNetIds = new List<uint>();
        private int _lastScrollDirection = 0;
        private int _previousSelectedIndex = -1;

        // NetID-to-GameObject cache
        private readonly Dictionary<NetworkInstanceId, GameObject> _netIdCache = new();
        public override void OnStartClient()
        {
            base.OnStartClient();
            Log.Debug($"[BottomlessBagNetworkController] OnStartClient called. Triggering initial sync.");
            OnBagStateChanged();
        }
        private void Awake()
        {
        }
        public void SetBagState(int index, List<GameObject> baggedObjects, List<GameObject> additionalSeats, int direction = 0)
        {
            List<uint> baggedIds = new List<uint>();
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
                if (CycleNetworkHandler.SuppressBroadcasts)
                {
                    Log.Debug($"[BottomlessBagNetworkController] Suppressing broadcast during auto-grab phase");
                    return;
                }

                Log.Debug($"[BottomlessBagNetworkController] Server sending UpdateBagStateMessage. index={index}, objects={baggedIds.Count}");

                 var msg = new UpdateBagStateMessage
                {
                    controllerNetId = GetComponent<NetworkIdentity>().netId,
                    selectedIndex = index,
                    baggedIds = baggedIds.ToArray(),
                    seatIds = seatIds.ToArray(),
                    scrollDirection = direction
                };

                NetworkServer.SendToAll(Constants.Network.UpdateBagStateMessageType, msg);

                UpdateLocalState(index, baggedIds, seatIds);
            }
            else if (hasAuthority)
            {
                Log.Debug($"[BottomlessBagNetworkController] Client sending bag state via message for {gameObject.name}");
                var controller = GetComponent<DrifterBagController>();
                if (controller != null)
                {
                    CycleNetworkHandler.SendClientBagState(controller!, index, baggedIds.ToArray(), seatIds.ToArray());
                }

                UpdateLocalState(index, baggedIds, seatIds);
            }
        }
        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds, int direction = 0)
        {
             Log.Debug($"[BottomlessBagNetworkController.ApplyStateFromMessage] === APPLY STATE FROM MESSAGE ===");
             Log.Debug($"[BottomlessBagNetworkController.ApplyStateFromMessage] index={index}, objects={baggedIds.Length}, direction={direction}");
             Log.Debug($"[BottomlessBagNetworkController.ApplyStateFromMessage] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
             Log.Debug($"[BottomlessBagNetworkController.ApplyStateFromMessage] NetworkServer.active: {NetworkServer.active}");
             Log.Debug($"[BottomlessBagNetworkController.ApplyStateFromMessage] hasAuthority: {hasAuthority}");
             Log.Debug($"[BottomlessBagNetworkController.ApplyStateFromMessage] =======================================");

             _lastScrollDirection = direction;

             var ctrl = GetComponent<DrifterBagController>();

             UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
        }
        [Command]
        public void CmdCycle(int amount)
        {
            if (!NetworkServer.active) return;

            Log.Debug($"[BottomlessBagNetworkController] Server received CmdCycle for {gameObject.name} with amount {amount}");
            var controller = GetComponent<DrifterBagController>();
            if (controller)
            {
                BottomlessBagPatches.CyclePassengers(controller, amount);
            }
        }
        [Command]
        private void CmdUpdateBagState(int index, uint[] baggedIds, uint[] seatIds)
        {
            Log.Debug($"[BottomlessBagNetworkController] Server received CmdUpdateBagState for {gameObject.name}. Objects: {baggedIds.Length}");

            var controller = GetComponent<DrifterBagController>();
            if (controller != null)
            {
                foreach (var idValue in baggedIds)
                {
                    var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        bool isInAnySeat = IsObjectInAnySeat(controller, obj);

                        if (!isInAnySeat)
                        {
                            Log.Debug($"[BottomlessBagNetworkController] Server auto-grabbing {obj.name} for client (from CmdUpdateBagState)");

                            controller.AssignPassenger(obj);
                        }
                        else
                        {
                            Log.Debug($"[BottomlessBagNetworkController] Object {obj.name} already in a seat, skipping auto-grab");
                        }
                    }
                }
            }

            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));

            if (controller != null)
            {
                TryFixNullTargetState(controller, new List<uint>(baggedIds));
            }

            var msg = new UpdateBagStateMessage
            {
                controllerNetId = GetComponent<NetworkIdentity>().netId,
                selectedIndex = index,
                baggedIds = baggedIds,
                seatIds = seatIds,
                scrollDirection = 0
            };
                NetworkServer.SendToAll(Constants.Network.UpdateBagStateMessageType, msg);
        }

        private bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;

            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                if (controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    return true;
            }

            // Dictionary<GameObject, RoR2.VehicleSeat> seatDict = null;
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
            }

            // Re-collect seat IDs post-DoSync.
            uint[] actualSeatIds = seatIds;
            if (controller != null)
            {
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
            }

            int correctedIndex = index;
            if (controller != null && index < 0 && baggedIds.Length > 0)
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

            var msg = new UpdateBagStateMessage
            {
                controllerNetId = GetComponent<NetworkIdentity>().netId,
                selectedIndex = correctedIndex,
                baggedIds = baggedIds,
                seatIds = actualSeatIds,
                scrollDirection = 0
            };
                NetworkServer.SendToAll(Constants.Network.UpdateBagStateMessageType, msg);

            // Also update local state with the actual seat IDs so server stays in sync
            if (actualSeatIds != seatIds)
            {
                _additionalSeatNetIds = new List<uint>(actualSeatIds);
                _additionalSeatNetIdsTarget = new List<uint>(actualSeatIds);
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
                        Log.Debug($"[TryFixNullTargetState] Late-fixing null target to {obj.name}");
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

            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] === SYNC STATE COROUTINE START ===");
            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] Controller: {controller.name}");
            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] Bagged IDs: {_baggedObjectNetIdsTarget.Count}, Seat IDs: {_additionalSeatNetIdsTarget.Count}");
            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] NetworkServer.active: {NetworkServer.active}");
            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] hasAuthority: {hasAuthority}");
            Log.Debug($"[BottomlessBagNetworkController.SyncStateCoroutine] ======================================");

            float timeout = Constants.Timeouts.SyncStateTimeout;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                bool allFound = true;

                foreach (var idValue in _baggedObjectNetIdsTarget)
                {
                    if (idValue == 0) continue;
                    var foundObj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue)) ?? NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (foundObj == null)
                    {
                        Log.Debug($"[SyncStateCoroutine] Waiting for Bagged Object ID {idValue}...");
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
                            Log.Debug($"[SyncStateCoroutine] Waiting for Additional Seat ID {idValue}...");
                            allFound = false;
                            break;
                        }
                    }
                }

                if (allFound)
                {
                    Log.Debug($"[SyncStateCoroutine] All objects found after {elapsed:F2}s");
                    Log.Debug($"[SyncStateCoroutine] IsSwappingPassengers at DoSync time: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                    break;
                }

                yield return new WaitForSeconds(Constants.Timeouts.SyncWaitIncrement);
                elapsed += Constants.Timeouts.SyncWaitIncrement;
            }

            if (elapsed >= timeout)
            {
                Log.Warning($"[SyncStateCoroutine] Timed out waiting for objects after {timeout:F2}s");
                foreach (var idValue in _baggedObjectNetIdsTarget)
                {
                    if (idValue == 0) continue;
                    var foundObj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue)) ?? NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (foundObj == null) Log.Warning($"[SyncStateCoroutine] Missing Bagged Object ID: {idValue}");
                }
            }

            _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
            _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);

            Log.Debug($"[SyncStateCoroutine] Calling DoSync with triggerUIUpdate=true");
            Log.Debug($"[SyncStateCoroutine] IsSwappingPassengers at DoSync call: {DrifterBossGrabPlugin.IsSwappingPassengers}");

            DoSync(controller, true, _lastScrollDirection);
            _syncCoroutine = null;
        }
        private void DoSync(DrifterBagController controller, bool triggerUIUpdate, int scrollDirection = 0)
        {
            Log.Debug($"[BottomlessBagNetworkController.DoSync] === DO SYNC CALLED ===");
            Log.Debug($"[BottomlessBagNetworkController.DoSync] Controller: {controller?.name ?? "null"}");
            Log.Debug($"[BottomlessBagNetworkController.DoSync] triggerUIUpdate: {triggerUIUpdate}, scrollDirection: {scrollDirection}");
            Log.Debug($"[BottomlessBagNetworkController.DoSync] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
            Log.Debug($"[BottomlessBagNetworkController.DoSync] NetworkServer.active: {NetworkServer.active}");
            Log.Debug($"[BottomlessBagNetworkController.DoSync] hasAuthority: {hasAuthority}");
            Log.Debug($"[BottomlessBagNetworkController.DoSync] =========================");

            GameObject? mainSeatObject = null;
            var syncedObjects = GetBaggedObjects();
            var seats = GetAdditionalSeats();
            var additionalSeatDict = new System.Collections.Concurrent.ConcurrentDictionary<GameObject, VehicleSeat>();

            if (!NetworkServer.active)
            {
                var allChildSeats = controller!.GetComponentsInChildren<VehicleSeat>(true);
                foreach (var childSeat in allChildSeats)
                {
                     if (childSeat == controller.vehicleSeat) continue;

                      bool isSynced = false;
                      if (seats != null)
                      {
                          foreach (var syncedSeat in seats)
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

                 if (seats != null)
                {
                    foreach (var seat in seats)
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
                                    additionalSeatDict[passengerObj] = seat;
                                }
                            }
                        }
                    }
                }

            if (NetworkServer.active)
            {
                var allChildSeats = controller?.GetComponentsInChildren<VehicleSeat>(true);
                if (allChildSeats != null)
                {
                foreach (var childSeat in allChildSeats)
                {
                    if (childSeat == controller!.vehicleSeat) continue;

                    if (childSeat != null && childSeat.hasPassenger)
                    {
                        var passenger = childSeat.NetworkpassengerBodyObject;
                        if (passenger != null && !additionalSeatDict.ContainsKey(passenger))
                        {
                            bool isInSyncedList = false;
                            foreach(var syncedObj in syncedObjects)
                            {
                                if (syncedObj != null && syncedObj.GetInstanceID() == passenger.GetInstanceID())
                                {
                                    isInSyncedList = true;
                                    break;
                                }
                            }
                            if (isInSyncedList)
                            {
                                Log.Debug($"[DoSync] Server-side recovery: Found missing seat for {passenger.name} in additional seats.");
                                additionalSeatDict[passenger] = childSeat;
                            }
                        }
                    }
                }
                }
            }

            if (syncedObjects != null && selectedIndex >= 0 && selectedIndex < syncedObjects.Count)
            {
                var potentialMainSeatObject = syncedObjects[selectedIndex];

                bool isActuallyInMainSeat = false;
                if (NetworkServer.active)
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
                    mainSeatObject = potentialMainSeatObject;
                    if (NetworkServer.active)
                        Log.Debug($"[DoSync] Main seat object from selectedIndex {selectedIndex}: {mainSeatObject?.name ?? "null"} (InstanceID: {mainSeatObject?.GetInstanceID()}) - PHYSICALLY IN MAIN SEAT");
                    else
                        Log.Debug($"[DoSync] Main seat object from server selectedIndex {selectedIndex}: {mainSeatObject?.name ?? "null"} (InstanceID: {mainSeatObject?.GetInstanceID()}) - TRUSTING SERVER STATE");

                    if (mainSeatObject != null && additionalSeatDict.ContainsKey(mainSeatObject))
                    {
                        Log.Debug($"[DoSync] Reconciling main seat object: removing {mainSeatObject.name} from additional seats dictionary");
                        additionalSeatDict.TryRemove(mainSeatObject, out _);
                    }
                }
                else
                {
                    mainSeatObject = null;
                    Log.Debug($"[DoSync] Ignoring selectedIndex {selectedIndex} - object {potentialMainSeatObject?.name} is in additional seat, not main seat (InstanceID: {potentialMainSeatObject?.GetInstanceID()})");
                }
            }
            else
            {
                Log.Debug($"[DoSync] No main seat object (selectedIndex={selectedIndex}, syncedObjects count={syncedObjects!.Count})");
            }
            if (controller != null)
            {
                BagPatches.GetState(controller).AdditionalSeats = additionalSeatDict;
                BagPatches.SetMainSeatObject(controller!, mainSeatObject);
            }

            // Restore state when transitioning from null.
            if (!NetworkServer.active && mainSeatObject != null)
            {
                // Check if we're transitioning from null state (previous selectedIndex was -1)
                bool wasNullState = _previousSelectedIndex < 0;

                if (wasNullState)
                {
                    Log.Debug($"[DoSync] Client: Transitioning from null to {mainSeatObject.name}, restoring state");

                    // Load stored state.
                    if (controller != null)
                    {
                        var storedState = BaggedObjectPatches.LoadObjectState(controller, mainSeatObject);
                        if (storedState != null)
                        {
                            Log.Debug($"[DoSync] Client: Applying stored state to {mainSeatObject.name}");

                            // Find or create state.
                            var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(controller, mainSeatObject);
                            if (baggedState != null)
                            {
                                storedState.ApplyToBaggedObject(baggedState);
                            }
                        }
                        else
                        {
                            Log.Debug($"[DoSync] Client: No stored state found for {mainSeatObject.name}");
                        }
                    }
                }
            }

            // Apply skill overrides directly. Avoid SynchronizeBaggedObjectState (causes sync loops).
            if (!NetworkServer.active && mainSeatObject != null && controller != null)
            {
                var baggedObject = BaggedObjectPatches.FindOrCreateBaggedObjectState(controller, mainSeatObject);
                if (baggedObject != null)
                {
                    baggedObject.targetObject = mainSeatObject;
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

            // Track previous index.
            _previousSelectedIndex = selectedIndex;

            if (controller != null) BagPassengerManager.ForceRecalculateMass(controller);
            if (syncedObjects != null)
            {
            if (controller != null) BagPassengerManager.MarkMassDirty(controller);
                if (controller != null) BagPatches.GetState(controller).BaggedObjects = syncedObjects;
            }
            if (triggerUIUpdate)
            {
                if (controller != null) BagCarouselUpdater.UpdateCarousel(controller, scrollDirection);

                // Refresh UI if main seat changed.
                if (controller != null)
                {
                    bool mainSeatChanged = (mainSeatObject != BagPatches.GetMainSeatObject(controller));
                    if (mainSeatChanged && mainSeatObject != null)
                    {
                        Log.Debug($"[DoSync] Main seat object changed to {mainSeatObject.name}, refreshing UI");
                        BaggedObjectPatches.RefreshUIOverlayForMainSeat(controller, mainSeatObject);
                    }
                    else if (mainSeatObject == null)
                    {
                        Log.Debug($"[DoSync] Main seat object is null, removing UI overlay");
                        BaggedObjectPatches.RemoveUIOverlayForNullState(controller);
                    }
                }
            }
        }
        public List<GameObject> GetBaggedObjects()
        {
            if (_netIdCache.Count == 0)
            {
                UpdateNetIdCache();
            }

            List<GameObject> objects = new List<GameObject>();
            foreach (var idValue in _baggedObjectNetIds)
            {
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
                        Log.Debug($"[GetBaggedObjects] Could not find object for NetID {idValue}");
                    }
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
    }
}
