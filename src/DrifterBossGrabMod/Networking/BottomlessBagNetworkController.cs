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

        // Use local lists instead of SyncLists to avoid HLAPI initialization issues on runtime-added components
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
            // Initialization is now handled by local lists
        }

        public void SetBagState(int index, List<GameObject> baggedObjects, List<GameObject> additionalSeats, int direction = 0)
        {
            // Prepare IDs
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
                // Skip broadcasts during auto-grab phase (intermediate state)
                if (CycleNetworkHandler.SuppressBroadcasts)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[BottomlessBagNetworkController] Suppressing broadcast during auto-grab phase");
                    return;
                }
                
                // Sync to clients via Custom Message (replaces RPC)
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
                
                NetworkServer.SendToAll(206, msg); // MSG_UPDATE_BAG_STATE = 206
                
                // Also update server side local state immediately
                UpdateLocalState(index, baggedIds, seatIds);
            }
            else if (hasAuthority)
            {
                // Client with authority (player) sends their local bag state to the server via custom message
                // Using custom message instead of [Command] because Commands weren't reaching the server properly
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Client sending bag state via message for {gameObject.name}");
                var controller = GetComponent<DrifterBagController>();
                if (controller != null)
                {
                    CycleNetworkHandler.SendClientBagState(controller, index, baggedIds.ToArray(), seatIds.ToArray());
                }
                
                // Also update local state immediately so UI/logic is responsive
                UpdateLocalState(index, baggedIds, seatIds);
            }
        }

        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds, int direction = 0)
        {
             // NetworkServer.active check logic moved to handler or irrelevant here for clients
             if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Applying State From Message. index={index}, objects={baggedIds.Length}, direction={direction}");
             _lastScrollDirection = direction;
             UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
        }

        [Command]
        public void CmdCycle(int amount)
        {
            // Guard: [Command] should only run on server
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
            
            // CRITICAL: Actually grab objects on server that client grabbed
            // Must do this BEFORE UpdateLocalState, since that populates baggedObjectsDict from network IDs
            var controller = GetComponent<DrifterBagController>();
            if (controller != null)
            {
                foreach (var idValue in baggedIds)
                {
                    var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        // Check if object is ACTUALLY in a seat (ground truth), not just in dict
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
            
            // NOW update local state from the client's data
            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
            
            // Fix race condition where state transition happened before target sync
            if (controller != null)
            {
                TryFixNullTargetState(controller, new List<uint>(baggedIds));
            }
            
            // Then tell all OTHER clients about it via Custom Message
            var msg = new UpdateBagStateMessage
            {
                controllerNetId = GetComponent<NetworkIdentity>().netId,
                selectedIndex = index,
                baggedIds = baggedIds,
                seatIds = seatIds,
                scrollDirection = 0 // Clients updating server don't need to specify direction
            };
            NetworkServer.SendToAll(206, msg);
        }
        
        // Helper to check if an object is actually in any seat (main or additional)
        private bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            
            // Check main seat
            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                if (controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    return true;
            }
            
            // Check additional seats
            if (BagPatches.additionalSeatsDict.TryGetValue(controller, out var seatDict))
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && kvp.Value.hasPassenger && kvp.Value.NetworkpassengerBodyObject == obj)
                        return true;
                }
            }
            
            // Also check child VehicleSeats
            var childSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
            foreach (var seat in childSeats)
            {
                if (seat != controller.vehicleSeat && seat.hasPassenger && seat.NetworkpassengerBodyObject == obj)
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Called by CycleNetworkHandler when server receives bag state from a client
        /// </summary>
        public void ServerUpdateFromClient(int index, uint[] baggedIds, uint[] seatIds)
        {
            if (!NetworkServer.active) return;
            
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[BottomlessBagNetworkController] ServerUpdateFromClient for {gameObject.name}. index={index}, objects={baggedIds.Length}");
            
            // Update local state
            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
            
            // Try to fix null target state if needed
            var controller = GetComponent<DrifterBagController>();
            if (controller != null)
            {
                TryFixNullTargetState(controller, new List<uint>(baggedIds));
            }
            
            // Recalculate selectedIndex if client sent -1 but we actually have a main seat object
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
            
            // Broadcast to all clients
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
        // Called when we receive bag state from client. If we're in BaggedObject state
        // with null target, attempt to fix it using the newly received object IDs.
        private void TryFixNullTargetState(DrifterBagController controller, List<uint> baggedIds)
        {
            if (!NetworkServer.active || baggedIds.Count == 0) return;
            
            // Find the BaggedObject state
            var stateMachines = controller.GetComponentsInChildren<EntityStateMachine>(true);
            foreach (var sm in stateMachines)
            {
                if (sm.state is BaggedObject baggedState && baggedState.targetObject == null)
                {
                    // Get the first bagged object as the target
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
                // On server, we can sync immediately as objects already exist
                _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
                _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);
                
                var controller = GetComponent<DrifterBagController>();
                if (controller) DoSync(controller, false); // Pass false to prevent recursion
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
            if (NetworkServer.active) return; // Server handles state directly in SetBagState

            if (_syncCoroutine != null) StopCoroutine(_syncCoroutine);
            _syncCoroutine = StartCoroutine(SyncStateCoroutine());
        }

        private System.Collections.IEnumerator SyncStateCoroutine()
        {
            var controller = GetComponent<DrifterBagController>();
            if (!controller) yield break;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[SyncStateCoroutine] Starting sync for {controller.name}. Bagged IDs: {_baggedObjectNetIdsTarget.Count}, Seat IDs: {_additionalSeatNetIdsTarget.Count}");
            }

            float timeout = 2.0f;
            float elapsed = 0f;
            
            while (elapsed < timeout)
            {
                bool allFound = true;
                
                // Check if all bagged objects are found
                foreach (var idValue in _baggedObjectNetIdsTarget)
                {
                    if (idValue == 0) continue; // Skip invalid IDs
                    var foundObj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue)) ?? NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (foundObj == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[SyncStateCoroutine] Waiting for Bagged Object ID {idValue}...");
                        allFound = false;
                        break;
                    }
                }

                // Check if all additional seats are found
                if (allFound)
                {
                    foreach (var idValue in _additionalSeatNetIdsTarget)
                    {
                        if (idValue == 0) continue; // Skip invalid IDs
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[SyncStateCoroutine] All objects found after {elapsed:F2}s");
                    break;
                }

                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
            
            if (elapsed >= timeout && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning($"[SyncStateCoroutine] Timed out waiting for objects after {timeout:F2}s");
                // Log strictly which ones are missing
                foreach (var idValue in _baggedObjectNetIdsTarget)
                {
                    if (idValue == 0) continue;
                    var foundObj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue)) ?? NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (foundObj == null) Log.Warning($"[SyncStateCoroutine] Missing Bagged Object ID: {idValue}");
                }
            }

            // Sync internal lists
            _baggedObjectNetIds = new List<uint>(_baggedObjectNetIdsTarget);
            _additionalSeatNetIds = new List<uint>(_additionalSeatNetIdsTarget);

            // Perform final sync
            DoSync(controller, true);
            _syncCoroutine = null;
        }

        private void DoSync(DrifterBagController controller, bool triggerUIUpdate)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[DoSync] Starting DoSync for {controller.name} (triggerUIUpdate: {triggerUIUpdate})");
            
            // Identify which object is in the main seat vs additional seats
            GameObject? mainSeatObject = null;
            var syncedObjects = GetBaggedObjects();
            var seats = GetAdditionalSeats();
            var additionalSeatDict = new System.Collections.Concurrent.ConcurrentDictionary<GameObject, VehicleSeat>();
            
            // Cleanup: Destroy local temporary seats that are NOT in the synced list
            // This prevents duplicate/orphaned seats on the client
            if (!NetworkServer.active) 
            {
                var allChildSeats = controller.GetComponentsInChildren<VehicleSeat>(true);
                foreach (var childSeat in allChildSeats)
                {
                     // Skip main seat
                     if (childSeat == controller.vehicleSeat) continue;
                     
                     // Skip seats that are in the synced list
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
                          if (childSeat.hasPassenger)
                          {
                              if (!childSeat.hasPassenger)
                              {
                                   UnityEngine.Object.Destroy(childSeat.gameObject);
                              }
                          }
                          else
                          {
                               // Empty local seat -> Destroy it
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
                        // Enforce parenting on client so they follow the Drifter
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
            
            // Use the synchronized selectedIndex to determine main seat object
            // selectedIndex is set by the server and synced to clients
            if (syncedObjects != null && selectedIndex >= 0 && selectedIndex < syncedObjects.Count)
            {
                mainSeatObject = syncedObjects[selectedIndex];
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[DoSync] Main seat object from selectedIndex {selectedIndex}: {mainSeatObject?.name ?? "null"} (InstanceID: {mainSeatObject?.GetInstanceID()})");

                // remove it from additional seats.
                if (mainSeatObject != null && additionalSeatDict.ContainsKey(mainSeatObject))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[DoSync] Reconciling main seat object: removing {mainSeatObject.name} from additional seats dictionary");
                    additionalSeatDict.TryRemove(mainSeatObject, out _);
                }
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[DoSync] No main seat object (selectedIndex={selectedIndex}, syncedObjects count={syncedObjects?.Count ?? 0})");
            }
            
            // Update global tracked state
            BagPatches.additionalSeatsDict[controller] = additionalSeatDict;
            // Always set main seat object, even if null, to clear stale state
            BagPatches.SetMainSeatObject(controller, mainSeatObject);
            
            // Store the synced list for capacity checks and UI
            if (syncedObjects != null)
            {
                BagPatches.baggedObjectsDict[controller] = syncedObjects;
            }

            if (triggerUIUpdate) 
            {
                BagPatches.UpdateCarousel(controller, _lastScrollDirection);
                _lastScrollDirection = 0; // Reset after use
                // Also update the UI Overlay
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
