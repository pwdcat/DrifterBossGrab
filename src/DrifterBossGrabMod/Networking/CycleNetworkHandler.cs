using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    public static class CycleNetworkHandler
    {
        private const short MSG_CYCLE_REQUEST = 205;
        private const short MSG_CLIENT_UPDATE_BAG_STATE = 207;

        /// Flag to suppress broadcasts during auto-grab phase to prevent intermediate state broadcasts
        public static bool SuppressBroadcasts = false;

        public static void Init()
        {
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler(MSG_CYCLE_REQUEST, OnServerReceiveCycleRequest);
                NetworkServer.RegisterHandler(MSG_CLIENT_UPDATE_BAG_STATE, OnServerReceiveClientBagState);
            }
        }

        public static void SendCycleRequest(DrifterBagController bagController, int amount)
        {
            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;

            var msg = new CyclePassengersMessage
            {
                bagControllerNetId = ni.netId,
                amount = amount
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleNetworkHandler] Sent cycle request: netId={ni.netId.Value}, amount={amount}");
            NetworkManager.singleton.client.Send(MSG_CYCLE_REQUEST, msg);
        }

        /// Client sends its bag state to the server via custom message (more reliable than [Command])
        public static void SendClientBagState(DrifterBagController bagController, int selectedIndex, uint[] baggedIds, uint[] seatIds)
        {
            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;
            
            var msg = new ClientUpdateBagStateMessage
            {
                controllerNetId = ni.netId,
                selectedIndex = selectedIndex,
                baggedIds = baggedIds,
                seatIds = seatIds
            };
            
            if (PluginConfig.Instance.EnableDebugLogs.Value) 
                Log.Info($"[CycleNetworkHandler] Sending client bag state: netId={ni.netId.Value}, objects={baggedIds.Length}");
            NetworkManager.singleton.client.Send(MSG_CLIENT_UPDATE_BAG_STATE, msg);
        }

        private static void OnServerReceiveCycleRequest(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<CyclePassengersMessage>();
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleNetworkHandler] Server received cycle request: netId={msg.bagControllerNetId.Value}, amount={msg.amount}");

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (controllerObj)
            {
                var bagController = controllerObj.GetComponent<DrifterBagController>();
                if (bagController)
                {
                    BottomlessBagPatches.ServerCyclePassengers(bagController, msg.amount);
                }
            }
        }

        private static void OnServerReceiveClientBagState(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<ClientUpdateBagStateMessage>();
            if (PluginConfig.Instance.EnableDebugLogs.Value) 
                Log.Info($"[CycleNetworkHandler] Server received client bag state: netId={msg.controllerNetId.Value}, objects={msg.baggedIds.Length}");

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;
            
            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (!bagController) return;

            // Suppress broadcasts during auto-grab phase to avoid sending intermediate states
            // We'll do one consolidated broadcast at the end via ServerUpdateFromClient
            SuppressBroadcasts = true;
            try
            {
                // Grab any objects that aren't already in seats
                foreach (var idValue in msg.baggedIds)
                {
                    var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        bool isInAnySeat = IsObjectInAnySeat(bagController, obj);
                        
                        if (!isInAnySeat)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[CycleNetworkHandler] Server auto-grabbing {obj.name} for client");
                            
                            bagController.AssignPassenger(obj);
                        }
                        else if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[CycleNetworkHandler] Object {obj.name} already in seat, skipping");
                        }
                    }
                }
                
                // Update the network controller's state - this will do the final broadcast
                // Keep SuppressBroadcasts true until after this completes to protect BaggedObject_OnExit
                var netController = bagController.GetComponent<BottomlessBagNetworkController>();
                if (netController != null)
                {
                    netController.ServerUpdateFromClient(msg.selectedIndex, msg.baggedIds, msg.seatIds);
                }
            }
            finally
            {
                SuppressBroadcasts = false;
            }
        }

        private static bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj)
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
    }
}
