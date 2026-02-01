using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    public class BottomlessBagNetworkController : NetworkBehaviour
    {
        [SyncVar]
        public int selectedIndex = -1;
 
        private List<uint> _baggedObjectNetIds = new List<uint>();
        private List<uint> _additionalSeatNetIds = new List<uint>();
        private int _lastScrollDirection = 0;
        public override void OnStartClient()
        {
            base.OnStartClient();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BottomlessBagNetworkController] OnStartClient called. Triggering initial sync.");
            }
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[BottomlessBagNetworkController] Suppressing broadcast during auto-grab phase");
                    return;
                }
                
                if (PluginConfig.Instance.EnableDebugLogs.Value) 
                    Log.Info($"[BottomlessBagNetworkController] Server sending UpdateBagStateMessage. index={index}, objects={baggedIds.Count}");
                
                var msg = new UpdateBagStateMessage
                {
                    controllerNetId = GetComponent<NetworkIdentity>().netId,
                    selectedIndex = index,
                    baggedIds = baggedIds.ToArray(),
                    seatIds = seatIds.ToArray(),
                    scrollDirection = direction
                };
                
                NetworkServer.SendToAll(206, msg);
                
                UpdateLocalState(index, baggedIds, seatIds);
            }
            else if (hasAuthority)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Client sending bag state via message for {gameObject.name}");
                var controller = GetComponent<DrifterBagController>();
                if (controller != null)
                {
                    CycleNetworkHandler.SendClientBagState(controller, index, baggedIds.ToArray(), seatIds.ToArray());
                }
                
                UpdateLocalState(index, baggedIds, seatIds);
            }
        }
        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds, int direction = 0)
        {
             if (PluginConfig.Instance.EnableDebugLogs.Value)
             {
                 Log.Info($"[BottomlessBagNetworkController.ApplyStateFromMessage] === APPLY STATE FROM MESSAGE ===");
                 Log.Info($"[BottomlessBagNetworkController.ApplyStateFromMessage] index={index}, objects={baggedIds.Length}, direction={direction}");
                 Log.Info($"[BottomlessBagNetworkController.ApplyStateFromMessage] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                 Log.Info($"[BottomlessBagNetworkController.ApplyStateFromMessage] NetworkServer.active: {NetworkServer.active}");
                 Log.Info($"[BottomlessBagNetworkController.ApplyStateFromMessage] hasAuthority: {hasAuthority}");
                 Log.Info($"[BottomlessBagNetworkController.ApplyStateFromMessage] =======================================");
             }

             _lastScrollDirection = direction;

             UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
        }
        [Command]
        public void CmdCycle(int amount)
        {
            if (!NetworkServer.active) return;
            
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Server received CmdCycle for {gameObject.name} with amount {amount}");
            var controller = GetComponent<DrifterBagController>();
            if (controller)
            {
                BottomlessBagPatches.CyclePassengers(controller, amount);
            }
        }
        [Command]
        private void CmdUpdateBagState(int index, uint[] baggedIds, uint[] seatIds)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Server received CmdUpdateBagState for {gameObject.name}. Objects: {baggedIds.Length}");
            
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
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[BottomlessBagNetworkController] Server auto-grabbing {obj.name} for client (from CmdUpdateBagState)");
                            
                            controller.AssignPassenger(obj);
                        }
                        else if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[BottomlessBagNetworkController] Object {obj.name} already in a seat, skipping auto-grab");
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
            NetworkServer.SendToAll(206, msg);
        }
        
        private bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            
            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                if (controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    return true;
            }
            
            if (BagPatches.additionalSeatsDict.TryGetValue(controller, out var seatDict))
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
                seatIds = seatIds,
                scrollDirection = 0
            };
            NetworkServer.SendToAll(206, msg);
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[TryFixNullTargetState] Late-fixing null target to {obj.name}");
                        }
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] === SYNC STATE COROUTINE START ===");
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] Controller: {controller.name}");
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] Bagged IDs: {_baggedObjectNetIdsTarget.Count}, Seat IDs: {_additionalSeatNetIdsTarget.Count}");
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] NetworkServer.active: {NetworkServer.active}");
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] hasAuthority: {hasAuthority}");
                Log.Info($"[BottomlessBagNetworkController.SyncStateCoroutine] ======================================");
            }
            float timeout = 2.0f;
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[SyncStateCoroutine] Waiting for Bagged Object ID {idValue}...");
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
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[SyncStateCoroutine] Waiting for Additional Seat ID {idValue}...");
                            allFound = false;
                            break;
                        }
                    }
                }

                if (allFound)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[SyncStateCoroutine] All objects found after {elapsed:F2}s");
                        Log.Info($"[SyncStateCoroutine] IsSwappingPassengers at DoSync time: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                    }
                    break;
                }

                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (elapsed >= timeout && PluginConfig.Instance.EnableDebugLogs.Value)
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[SyncStateCoroutine] Calling DoSync with triggerUIUpdate=true");
                Log.Info($"[SyncStateCoroutine] IsSwappingPassengers at DoSync call: {DrifterBossGrabPlugin.IsSwappingPassengers}");
            }

            DoSync(controller, true, _lastScrollDirection);
            _syncCoroutine = null;
        }
        private void DoSync(DrifterBagController controller, bool triggerUIUpdate, int scrollDirection = 0)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BottomlessBagNetworkController.DoSync] === DO SYNC CALLED ===");
                Log.Info($"[BottomlessBagNetworkController.DoSync] Controller: {controller?.name ?? "null"}");
                Log.Info($"[BottomlessBagNetworkController.DoSync] triggerUIUpdate: {triggerUIUpdate}, scrollDirection: {scrollDirection}");
                Log.Info($"[BottomlessBagNetworkController.DoSync] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                Log.Info($"[BottomlessBagNetworkController.DoSync] NetworkServer.active: {NetworkServer.active}");
                Log.Info($"[BottomlessBagNetworkController.DoSync] hasAuthority: {hasAuthority}");
                Log.Info($"[BottomlessBagNetworkController.DoSync] =========================");
            }

            GameObject? mainSeatObject = null;
            var syncedObjects = GetBaggedObjects();
            var seats = GetAdditionalSeats();
            var additionalSeatDict = new System.Collections.Concurrent.ConcurrentDictionary<GameObject, VehicleSeat>();
            
            if (!NetworkServer.active) 
            {
                var allChildSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
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
                        if (seat.transform.parent != controller.transform)
                        {
                            seat.transform.SetParent(controller.transform);
                            seat.transform.localPosition = Vector3.zero;
                            seat.transform.localRotation = Quaternion.identity;
                        }
                        if (seat.hasPassenger)
                        {
                            additionalSeatDict[seat.NetworkpassengerBodyObject] = seat;
                        }
                    }
                }
            }
            
            if (NetworkServer.active)
            {
                var allChildSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
                foreach (var childSeat in allChildSeats)
                {
                    if (childSeat == controller.vehicleSeat) continue;
                    
                    if (childSeat.hasPassenger)
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
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[DoSync] Server-side recovery: Found missing seat for {passenger.name} in additional seats.");
                                additionalSeatDict[passenger] = childSeat;
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        if (NetworkServer.active)
                            Log.Info($"[DoSync] Main seat object from selectedIndex {selectedIndex}: {mainSeatObject?.name ?? "null"} (InstanceID: {mainSeatObject?.GetInstanceID()}) - PHYSICALLY IN MAIN SEAT");
                        else
                            Log.Info($"[DoSync] Main seat object from server selectedIndex {selectedIndex}: {mainSeatObject?.name ?? "null"} (InstanceID: {mainSeatObject?.GetInstanceID()}) - TRUSTING SERVER STATE");
                    }
 
                    if (mainSeatObject != null && additionalSeatDict.ContainsKey(mainSeatObject))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[DoSync] Reconciling main seat object: removing {mainSeatObject.name} from additional seats dictionary");
                        additionalSeatDict.TryRemove(mainSeatObject, out _);
                    }
                }
                else
                {
                    mainSeatObject = null;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[DoSync] Ignoring selectedIndex {selectedIndex} - object {potentialMainSeatObject?.name} is in additional seat, not main seat (InstanceID: {potentialMainSeatObject?.GetInstanceID()})");
                }
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[DoSync] No main seat object (selectedIndex={selectedIndex}, syncedObjects count={syncedObjects?.Count ?? 0})");
            }
            if (controller != null)
            {
                BagPatches.additionalSeatsDict[controller] = additionalSeatDict;
            }
            BagPatches.SetMainSeatObject(controller, mainSeatObject);
            if (syncedObjects != null)
            {
                BagPatches.baggedObjectsDict[controller] = syncedObjects;
            }
            BagPatches.ForceRecalculateMass(controller);
            if (triggerUIUpdate) 
            {
                BagPatches.UpdateCarousel(controller, scrollDirection);
                
                if (mainSeatObject != null)
                {
                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(controller, mainSeatObject);
                }
                else
                {
                    BaggedObjectPatches.RemoveUIOverlayForNullState(controller);
                }
            }
        }
        public List<GameObject> GetBaggedObjects()
        {
            List<GameObject> objects = new List<GameObject>();
            foreach (var idValue in _baggedObjectNetIds)
            {
                var id = new NetworkInstanceId(idValue);
                var obj = ClientScene.FindLocalObject(id) ?? NetworkServer.FindLocalObject(id);
                if (obj) 
                {
                    objects.Add(obj);
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                     Log.Info($"[GetBaggedObjects] Could not find object for NetID {idValue}");
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
