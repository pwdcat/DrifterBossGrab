#nullable enable
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
        private const short MSG_CLIENT_PREFERENCES = 209;

        // Flag to suppress broadcasts during auto-grab phase to prevent intermediate state broadcasts
        public static bool SuppressBroadcasts = false;

        public static void Init()
        {
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler(MSG_CYCLE_REQUEST, OnServerReceiveCycleRequest);
                NetworkServer.RegisterHandler(MSG_CLIENT_UPDATE_BAG_STATE, OnServerReceiveClientBagState);
                NetworkServer.RegisterHandler(MSG_GRAB_OBJECT, OnServerReceiveGrabObject);
                NetworkServer.RegisterHandler(MSG_CLIENT_PREFERENCES, OnServerReceiveClientPreferences);
            }
        }

        // Client sends its preferences to the server
        public static void SendClientPreferences(NetworkIdentity controllerIdentity, bool autoPromote, bool prioritize)
        {
            if (!NetworkManager.singleton || NetworkManager.singleton.client == null) return;

            var msg = new ClientPreferencesMessage
            {
                controllerNetId = controllerIdentity.netId,
                autoPromoteMainSeat = autoPromote,
                prioritizeMainSeat = prioritize
            };

            NetworkManager.singleton.client.Send(MSG_CLIENT_PREFERENCES, msg);
        }

        private static void OnServerReceiveClientPreferences(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<ClientPreferencesMessage>();

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;

            var netController = controllerObj.GetComponent<BottomlessBagNetworkController>();
            if (netController == null) return;

            netController.autoPromoteMainSeat = msg.autoPromoteMainSeat;
            netController.prioritizeMainSeat = msg.prioritizeMainSeat;
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

            NetworkManager.singleton.client.Send(MSG_CLIENT_UPDATE_BAG_STATE, msg);
        }

        private static void OnServerReceiveCycleRequest(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<CyclePassengersMessage>();

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (controllerObj)
            {
                var bagController = controllerObj.GetComponent<DrifterBagController>();
                if (bagController)
                {
                    PassengerCycler.ServerCyclePassengers(bagController, msg.amount);
                }
            }
        }

        private static void OnServerReceiveClientBagState(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<ClientUpdateBagStateMessage>();

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
                            bagController.AssignPassenger(obj);
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

            NetworkManager.singleton.client.Send(MSG_GRAB_OBJECT, msg);
        }

        // Server receives grab request and calls AssignPassenger to trigger Harmony patch
        private static void OnServerReceiveGrabObject(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<GrabObjectMessage>();

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (!controllerObj) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (!bagController) return;

            var targetObject = NetworkServer.FindLocalObject(msg.targetObjectNetId);
            if (!targetObject) return;

            // Check if balance is enabled
            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                return;
            }

            // Check if object is already in any seat
            if (IsObjectInAnySeat(bagController, targetObject))
            {
                return;
            }

            bagController.AssignPassenger(targetObject);
        }
    }
}
