using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    public class BottomlessBagNetworkController : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnSelectedIndexChanged))]
        public int selectedIndex = -1;

        // Use local lists instead of SyncLists to avoid HLAPI initialization issues on runtime-added components
        private List<uint> _baggedObjectNetIds = new List<uint>();
        private List<uint> _additionalSeatNetIds = new List<uint>();

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

        public void SetBagState(int index, List<GameObject> baggedObjects, List<GameObject> additionalSeats)
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
                // Sync to clients via Custom Message (replaces RPC)
                if (PluginConfig.Instance.EnableDebugLogs.Value) 
                    Log.Info($"[BottomlessBagNetworkController] Server sending UpdateBagStateMessage. index={index}, objects={baggedIds.Count}");
                
                var msg = new UpdateBagStateMessage
                {
                    controllerNetId = GetComponent<NetworkIdentity>().netId,
                    selectedIndex = index,
                    baggedIds = baggedIds.ToArray(),
                    seatIds = seatIds.ToArray()
                };
                
                NetworkServer.SendToAll(206, msg); // MSG_UPDATE_BAG_STATE = 206
                
                // Also update server side local state immediately
                UpdateLocalState(index, baggedIds, seatIds);
            }
            else if (hasAuthority)
            {
                // Client with authority (player) sends their local bag state to the server
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Client sending CmdUpdateBagState for {gameObject.name}");
                CmdUpdateBagState(index, baggedIds.ToArray(), seatIds.ToArray());
                
                // Also update local state immediately so UI/logic is responsive
                UpdateLocalState(index, baggedIds, seatIds);
            }
        }

        public void ApplyStateFromMessage(int index, uint[] baggedIds, uint[] seatIds)
        {
             // NetworkServer.active check logic moved to handler or irrelevant here for clients
             if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Applying State From Message. index={index}, objects={baggedIds.Length}");
             UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
        }

        [Command]
        public void CmdCycle(bool scrollUp)
        {
            // Guard: [Command] should only run on server
            if (!NetworkServer.active) return;
            
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Server received CmdCycle for {gameObject.name}");
            var controller = GetComponent<DrifterBagController>();
            if (controller)
            {
                BottomlessBagPatches.CyclePassengers(controller, scrollUp);
            }
        }

        [Command]
        private void CmdUpdateBagState(int index, uint[] baggedIds, uint[] seatIds)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BottomlessBagNetworkController] Server received CmdUpdateBagState for {gameObject.name}. Objects: {baggedIds.Length}");
            
            // Server updates its local state from the client's data
            UpdateLocalState(index, new List<uint>(baggedIds), new List<uint>(seatIds));
            
            // Then tell all OTHER clients about it via Custom Message
            var msg = new UpdateBagStateMessage
            {
                controllerNetId = GetComponent<NetworkIdentity>().netId,
                selectedIndex = index,
                baggedIds = baggedIds,
                seatIds = seatIds
            };
            NetworkServer.SendToAll(206, msg);
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

        private void OnSelectedIndexChanged(int newIndex)
        {
            selectedIndex = newIndex;
            // Only trigger sync on client for SyncVar changes
            if (!NetworkServer.active) OnBagStateChanged();
        }

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
                    Log.Info($"[DoSync] Main seat object from selectedIndex {selectedIndex}: {mainSeatObject?.name ?? "null"}");
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
                BagPatches.UpdateCarousel(controller);
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
                if (obj) objects.Add(obj);
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
