#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    // Handles client->server communication for bag operations.
    public static class CycleNetworkHandler
    {
        // Flag to suppress broadcasts during auto-grab phase to prevent intermediate state broadcasts
        public static volatile bool SuppressBroadcasts = false;

        // Sends client preferences (auto-promote, prioritize main seat) to the server.
        public static void SendClientPreferences(NetworkIdentity controllerIdentity, bool autoPromote, bool prioritize)
        {
            if (!NetworkManager.singleton || NetworkManager.singleton.client == null) return;

            var msg = new ClientPreferencesMessage
            {
                controllerNetId = controllerIdentity.netId,
                autoPromoteMainSeat = autoPromote,
                prioritizeMainSeat = prioritize
            };

            NetworkManager.singleton.client.Send(Constants.Network.ClientPreferencesMessageType, msg);
        }

        // Sends a cycle request to the server.
        public static void SendCycleRequest(DrifterBagController bagController, int amount)
        {
            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;

            var msg = new CyclePassengersMessage
            {
                bagControllerNetId = ni.netId,
                amount = amount
            };

            NetworkManager.singleton.client.Send(Constants.Network.CycleRequestMessageType, msg);
        }

        // Sends client's bag state to the server via custom message
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

            NetworkManager.singleton.client.Send(Constants.Network.ClientUpdateBagStateMessageType, msg);
        }

        // Sends a grab request to the server.
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

            NetworkManager.singleton.client.Send(Constants.Network.GrabObjectMessageType, msg);
        }

        // Sends explicit bag state update to all clients (Server -> Client)
        public static void SendBagStateUpdate(DrifterBagController bagController, NetworkInstanceId removedObjectNetId, bool isThrowOperation = false)
        {
            if (!NetworkServer.active) return;

            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController == null) return;

            var baggedObjects = netController.GetBaggedObjects();
            var baggedIds = new uint[baggedObjects.Count];
            var seatIds = new uint[baggedObjects.Count];
            for (int i = 0; i < baggedObjects.Count; i++)
            {
                var netId = baggedObjects[i].GetComponent<NetworkIdentity>();
                baggedIds[i] = netId != null ? netId.netId.Value : 0;
                seatIds[i] = netId != null ? netId.netId.Value : 0;
            }

            var msg = new BagStateUpdatedMessage
            {
                controllerNetId = ni.netId,
                selectedIndex = netController.selectedIndex,
                removedObjectNetId = removedObjectNetId,
                baggedIds = baggedIds,
                seatIds = seatIds,
                scrollDirection = 0,
                isThrowOperation = isThrowOperation
            };

            NetworkServer.SendToAll(Constants.Network.BagStateUpdatedMessageType, msg);

            Log.Info($"[SendBagStateUpdate] Sent bag state update for {bagController.name} - selectedIndex={netController.selectedIndex}, isThrow={isThrowOperation}, removedObject={(removedObjectNetId == NetworkInstanceId.Invalid ? "none" : removedObjectNetId.Value.ToString())}");
        }

        // Handles client preferences message.
        [NetworkMessageHandler(msgType = Constants.Network.ClientPreferencesMessageType, server = true, client = false)]
        public static void HandleClientPreferencesMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<ClientPreferencesMessage>();

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;

            var netController = controllerObj.GetComponent<BottomlessBagNetworkController>();
            if (netController == null) return;

            netController.autoPromoteMainSeat = msg.autoPromoteMainSeat;
            netController.prioritizeMainSeat = msg.prioritizeMainSeat;
        }

        // Handles cycle request message (Client -> Server).
        [NetworkMessageHandler(msgType = Constants.Network.CycleRequestMessageType, server = true, client = false)]
        public static void HandleCycleRequestMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<CyclePassengersMessage>();

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (!controllerObj) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Debug($"[CycleNetworkHandler.HandleCycleRequestMessage] Processing request: Controller={bagController.name}, Amount={msg.amount}.");
                PassengerCycler.ServerCyclePassengers(bagController, msg.amount);
            }
        }

        // Handles client bag state update message (Client -> Server).
        [NetworkMessageHandler(msgType = Constants.Network.ClientUpdateBagStateMessageType, server = true, client = false)]
        public static void HandleClientBagStateMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<ClientUpdateBagStateMessage>();

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null) return;

            // Suppress broadcasts during auto-grab phase to avoid sending intermediate states
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

        // Handles grab object request message (Client -> Server).
        [NetworkMessageHandler(msgType = Constants.Network.GrabObjectMessageType, server = true, client = false)]
        public static void HandleGrabObjectMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<GrabObjectMessage>();

            // Use NetworkUtils for safe object lookup with detailed logging
            var controllerObj = NetworkUtils.FindLocalObjectWithLogging(msg.bagControllerNetId, "HandleGrabObjectMessage", isServer: true);
            if (controllerObj == null) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null)
            {
                Log.Warning($"[HandleGrabObjectMessage] {controllerObj.name} does not have DrifterBagController component");
                return;
            }

            var targetObject = NetworkUtils.FindLocalObjectWithLogging(msg.targetObjectNetId, "HandleGrabObjectMessage", isServer: true);
            if (targetObject == null) return;

            // Validate that target object is ready for network operations
            if (!NetworkUtils.ValidateObjectReady(targetObject))
            {
                Log.Error($"[HandleGrabObjectMessage] Target object {targetObject.name} is not ready for network operations");
                return;
            }

            // Log the grab operation with context
            NetworkUtils.LogNetworkOperation("HandleGrabObjectMessage", targetObject, isServer: true, new Dictionary<string, object>
            {
                { "bagController", bagController.name },
                { "bagControllerNetId", msg.bagControllerNetId.Value },
                { "targetObjectNetId", msg.targetObjectNetId.Value }
            });

            // Check if object is already in any seat
            if (IsObjectInAnySeat(bagController, targetObject))
            {
                Log.Info($"[HandleGrabObjectMessage] {targetObject.name} is already in a seat, skipping grab");
                return;
            }

            // Server-side validation for throw operation
            if (ProjectileRecoveryPatches.IsUndergoingThrowOperation(targetObject))
            {
                Log.Warning($"[HandleGrabObjectMessage] Blocking grab request for {targetObject.name} - object is currently undergoing throw operation");
                return;
            }

            bagController.AssignPassenger(targetObject);
        }

        // Handles explicit bag state update message (Server -> Client)
        [NetworkMessageHandler(msgType = Constants.Network.BagStateUpdatedMessageType, server = false, client = true)]
        public static void HandleBagStateUpdatedMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<BagStateUpdatedMessage>();

            // Use direct ClientScene.FindLocalObject without logging for controller lookup
            var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (controllerObj == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[HandleBagStateUpdatedMessage] Controller (netId={msg.controllerNetId.Value}) not found - likely destroyed");
                }
                return;
            }

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null)
            {
                Log.Warning($"[HandleBagStateUpdatedMessage] {controllerObj.name} does not have DrifterBagController component");
                return;
            }

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController == null)
            {
                Log.Warning($"[HandleBagStateUpdatedMessage] {bagController.name} does not have BottomlessBagNetworkController component");
                return;
            }

            // Log the bag state update
            NetworkUtils.LogNetworkOperation("HandleBagStateUpdatedMessage", controllerObj, isServer: false, new Dictionary<string, object>
            {
                { "selectedIndex", msg.selectedIndex },
                { "isThrowOperation", msg.isThrowOperation },
                { "removedObjectNetId", msg.removedObjectNetId.Value },
                { "baggedCount", msg.baggedIds.Length }
            });

            // Update the network controller's state
            netController.ServerUpdateFromClient(msg.selectedIndex, msg.baggedIds, msg.seatIds);

            // If an object was removed (thrown/exited), clean up its state
            if (msg.removedObjectNetId != NetworkInstanceId.Invalid)
            {
                var removedObj = ClientScene.FindLocalObject(msg.removedObjectNetId);
                if (removedObj != null)
                {
                    // Clean up the removed object
                    Log.Info($"[HandleBagStateUpdatedMessage] Cleaning up removed object {removedObj.name}");
                    NetworkUtils.InvalidateReadyCache(removedObj);
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[HandleBagStateUpdatedMessage] Removed object (netId={msg.removedObjectNetId.Value}) not found - likely destroyed/already thrown");
                    }
                }
            }

            // Refresh carousel UI
            BagCarouselUpdater.UpdateCarousel(bagController);

            // Log that we're about to sync the bag state
            Log.Info($"[HandleBagStateUpdatedMessage] About to sync bag state for {bagController.name} - baggedCount={msg.baggedIds.Length}, selectedIndex={msg.selectedIndex}, isThrow={msg.isThrowOperation}");

            // Sync bagged object tracking list
            var bagState = BagPatches.GetState(bagController);
            if (bagState != null)
            {
                bagState.BaggedObjects.Clear();
                foreach (var idValue in msg.baggedIds)
                {
                    var obj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        bagState.BaggedObjects.Add(obj);

                        // Restore preserved state if missing (happens after client-side throw)
                        var existingState = BaggedObjectPatches.LoadObjectState(bagController, obj);
                        if (existingState == null && msg.isThrowOperation)
                        {
                            BaggedObjectPatches.RestorePreservedState(bagController, obj);
                        }
                    }
                    else
                    {
                        Log.Warning($"[HandleBagStateUpdatedMessage] Could not find object for netId={idValue}, skipping");
                    }
                }

                // Clean up all temporary preserved states for this controller
                BaggedObjectPatches.ClearAllTemporaryPreservation(bagController);
            }

            Log.Info($"[HandleBagStateUpdatedMessage] Bag state updated for {bagController.name} - new selectedIndex={netController.selectedIndex}");
        }

        // Checks if an object is currently in any seat of the bag controller.
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
    }
}
