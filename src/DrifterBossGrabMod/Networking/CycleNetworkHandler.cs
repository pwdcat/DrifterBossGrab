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
        private const short MSG_GRAB_OBJECT = 208;

        // Flag to suppress broadcasts during auto-grab phase to prevent intermediate state broadcasts
        public static bool SuppressBroadcasts = false;

        public static void Init()
        {
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler(MSG_CYCLE_REQUEST, OnServerReceiveCycleRequest);
                NetworkServer.RegisterHandler(MSG_CLIENT_UPDATE_BAG_STATE, OnServerReceiveClientBagState);
                NetworkServer.RegisterHandler(MSG_GRAB_OBJECT, OnServerReceiveGrabObject);
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CycleNetworkHandler.SendCycleRequest] === CLIENT SENDING CYCLE REQUEST ===");
                Log.Info($"[CycleNetworkHandler.SendCycleRequest] Controller: {bagController.name}");
                Log.Info($"[CycleNetworkHandler.SendCycleRequest] NetID: {ni.netId.Value}, amount: {amount}");
                Log.Info($"[CycleNetworkHandler.SendCycleRequest] IsSwappingPassengers BEFORE: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                Log.Info($"[CycleNetworkHandler.SendCycleRequest] =========================================");
            }
            NetworkManager.singleton.client.Send(MSG_CYCLE_REQUEST, msg);
        }

        // Client sends its bag state to the server via custom message (more reliable than [Command])
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
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CycleNetworkHandler.OnServerReceiveCycleRequest] === SERVER RECEIVED CYCLE REQUEST ===");
                Log.Info($"[CycleNetworkHandler.OnServerReceiveCycleRequest] NetID: {msg.bagControllerNetId.Value}, amount: {msg.amount}");
                Log.Info($"[CycleNetworkHandler.OnServerReceiveCycleRequest] IsSwappingPassengers BEFORE: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                Log.Info($"[CycleNetworkHandler.OnServerReceiveCycleRequest] ===========================================");
            }

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (controllerObj)
            {
                var bagController = controllerObj.GetComponent<DrifterBagController>();
                if (bagController)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[CycleNetworkHandler.OnServerReceiveCycleRequest] Calling ServerCyclePassengers for {bagController.name}");
                    PassengerCycler.ServerCyclePassengers(bagController, msg.amount);
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
            var seatDict = BagPatches.GetState(controller).AdditionalSeats;
            if (seatDict != null)
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

        // Client sends grab request to server (Client -> Server)
        public static void SendGrabObjectRequest(DrifterBagController bagController, GameObject targetObject)
        {
            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;

            var targetNi = targetObject.GetComponent<NetworkIdentity>();
            if (!targetNi) return;

            var msg = new GrabObjectMessage
            {
                bagControllerNetId = ni.netId,
                targetObjectNetId = targetNi.netId
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CycleNetworkHandler.SendGrabObjectRequest] === CLIENT SENDING GRAB REQUEST ===");
                Log.Info($"[CycleNetworkHandler.SendGrabObjectRequest] Controller: {bagController.name}, Target: {targetObject.name}");
                Log.Info($"[CycleNetworkHandler.SendGrabObjectRequest] Controller NetID: {ni.netId.Value}, Target NetID: {targetNi.netId.Value}");
                Log.Info($"[CycleNetworkHandler.SendGrabObjectRequest] EnableBalance={PluginConfig.Instance.EnableBalance.Value}");
                Log.Info($"[CycleNetworkHandler.SendGrabObjectRequest] =========================================");
            }
            NetworkManager.singleton.client.Send(MSG_GRAB_OBJECT, msg);
        }

        // Server receives grab request and calls AssignPassenger to trigger Harmony patch
        private static void OnServerReceiveGrabObject(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<GrabObjectMessage>();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] === SERVER RECEIVED GRAB REQUEST ===");
                Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] Controller NetID: {msg.bagControllerNetId.Value}, Target NetID: {msg.targetObjectNetId.Value}");
                Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] EnableBalance={PluginConfig.Instance.EnableBalance.Value}");
                Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] ===========================================");
            }

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (!controllerObj) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (!bagController) return;

            var targetObject = NetworkServer.FindLocalObject(msg.targetObjectNetId);
            if (!targetObject) return;

            // Check if balance is enabled
            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] Balance disabled, skipping grab request");
                return;
            }

            // Check if object is already in any seat
            if (IsObjectInAnySeat(bagController, targetObject))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] Object {targetObject.name} already in a seat, skipping");
                return;
            }

            // Call AssignPassenger to trigger the Harmony patch
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[CycleNetworkHandler.OnServerReceiveGrabObject] Calling AssignPassenger for {targetObject.name}");

            bagController.AssignPassenger(targetObject);
        }
    }
}
