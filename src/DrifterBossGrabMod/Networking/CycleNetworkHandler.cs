#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Networking;
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod.Networking
{
    // ========================================================================================
    // CYCLE NETWORK HANDLER
    // ========================================================================================

    public static class CycleNetworkHandler
    {
        // Flag to suppress broadcasts during auto-grab phase to prevent intermediate state broadcasts
        public static volatile bool SuppressBroadcasts = false;

        // ========================================================================================
        // OUTBOUND MESSAGES (CLIENT -> SERVER)
        // ========================================================================================

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

        // ========================================================================================
        // STATE BROADCASTS (SERVER -> CLIENT)
        // ========================================================================================

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
            var breakoutTimes = new float[baggedObjects.Count];
            var elapsedBreakoutTimes = new float[baggedObjects.Count];
            var additionalSeats = API.DrifterBagAPI.GetAdditionalSeats(bagController);

            for (int i = 0; i < baggedObjects.Count; i++)
            {
                var obj = baggedObjects[i];
                var netId = obj.GetComponent<NetworkIdentity>();
                baggedIds[i] = netId != null ? netId.netId.Value : 0;

                // Find which seat this object is in
                uint seatNetId = 0;
                if (additionalSeats != null && additionalSeats.TryGetValue(obj, out var seat))
                {
                    var seatNi = seat.GetComponent<NetworkIdentity>();
                    if (seatNi != null) seatNetId = seatNi.netId.Value;
                }
                seatIds[i] = seatNetId;

                // Get breakout times
                var state = StateCalculator.GetIndividualObjectState(bagController, obj);
                if (state != null)
                {
                    breakoutTimes[i] = state.breakoutTime;
                    elapsedBreakoutTimes[i] = state.elapsedBreakoutTime;
                }
            }

            var msg = new BagStateUpdatedMessage
            {
                controllerNetId = ni.netId,
                selectedIndex = netController.selectedIndex,
                removedObjectNetId = removedObjectNetId,
                baggedIds = baggedIds,
                seatIds = seatIds,
                breakoutTimes = breakoutTimes,
                elapsedBreakoutTimes = elapsedBreakoutTimes,
                scrollDirection = 0,
                isThrowOperation = isThrowOperation
            };

            NetworkServer.SendToAll(Constants.Network.BagStateUpdatedMessageType, msg);

            Log.DebugIfEnabled("[SendBagStateUpdate] Sent bag state update for {0} selectedIndex={1} isThrow={2} removedObject={3}", bagController.name, netController.selectedIndex, isThrowOperation, (removedObjectNetId == NetworkInstanceId.Invalid ? "none" : removedObjectNetId.Value.ToString()));
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS (SERVER)
        // ========================================================================================

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
                Log.DebugIfEnabled("[CycleNetworkHandler.HandleCycleRequestMessage] Processing request: Controller={0} Amount={1}", bagController.name, msg.amount);
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
                // Get child seats once for the entire message
                var childSeats = bagController.GetComponentsInChildren<VehicleSeat>(true);

                // Grab any objects that aren't already in seats
                foreach (var idValue in msg.baggedIds)
                {
                    var obj = NetworkServer.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        bool isInAnySeat = IsObjectInAnySeat(bagController, obj, childSeats);

                        if (!isInAnySeat && API.DrifterBagAPI.HasRoom(bagController))
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
                Log.DebugIfEnabled($"[HandleGrabObjectMessage] {controllerObj.name} does not have DrifterBagController component");
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

            NetworkUtils.LogNetworkOperation("HandleGrabObjectMessage", targetObject, isServer: true, new Dictionary<string, object>
            {
                { "bagController", bagController.name },
                { "bagControllerNetId", msg.bagControllerNetId.Value },
                { "targetObjectNetId", msg.targetObjectNetId.Value }
            });

            // Check if object is already in any seat
            var childSeats = bagController.GetComponentsInChildren<VehicleSeat>(true);
            if (IsObjectInAnySeat(bagController, targetObject, childSeats))
            {
                Log.DebugIfEnabled("[HandleGrabObjectMessage] {0} is already in a seat, skipping grab", targetObject.name);
                return;
            }

            if (API.DrifterBagAPI.HasRoom(bagController))
            {
                bagController.AssignPassenger(targetObject);
            }
            else
            {
                Log.DebugIfEnabled("[HandleGrabObjectMessage] Bag is full for {0}, rejecting grab for {1}", bagController.name, targetObject.name);
            }
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS (CLIENT)
        // ========================================================================================

        [NetworkMessageHandler(msgType = Constants.Network.BagStateUpdatedMessageType, server = false, client = true)]
        public static void HandleBagStateUpdatedMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<BagStateUpdatedMessage>();

            // Use direct ClientScene.FindLocalObject without logging for controller lookup
            var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (controllerObj == null)
            {
                Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Controller netId={0} not found", msg.controllerNetId.Value);
                return;
            }

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null)
            {
                Log.DebugIfEnabled($"[HandleBagStateUpdatedMessage] {controllerObj.name} does not have DrifterBagController component");
                return;
            }

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController == null)
            {
                Log.DebugIfEnabled($"[HandleBagStateUpdatedMessage] {bagController.name} does not have BottomlessBagNetworkController component");
                return;
            }

            NetworkUtils.LogNetworkOperation("HandleBagStateUpdatedMessage", controllerObj, isServer: false, new Dictionary<string, object>
            {
                { "selectedIndex", msg.selectedIndex },
                { "isThrowOperation", msg.isThrowOperation },
                { "removedObjectNetId", msg.removedObjectNetId.Value },
                { "baggedCount", msg.baggedIds.Length }
            });

            // Update the network controller's state using the authoritative message data
            netController.ApplyStateFromMessage(msg.selectedIndex, msg.baggedIds, msg.seatIds, msg.scrollDirection, msg.breakoutTimes, msg.elapsedBreakoutTimes);
            if (msg.selectedIndex == -1)
            {
                API.DrifterBagAPI.SetMainSeatObject(bagController, null);
                Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Forcefully nullified MainSeatObject for empty bag state.");
            }

            // If an object was removed (thrown/exited), clean up its state
            if (msg.removedObjectNetId != NetworkInstanceId.Invalid)
            {
                var removedObj = ClientScene.FindLocalObject(msg.removedObjectNetId);
                if (removedObj != null)
                {
                    // Clean up the removed object
                    Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Cleaning up removed object {0}", removedObj.name);
                    NetworkUtils.InvalidateReadyCache(removedObj);
                }
                else
                {
                    Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Removed object netId={0} not found", msg.removedObjectNetId.Value);
                }
            }

            // Refresh carousel UI
            BagCarouselUpdater.UpdateCarousel(bagController);

            // Trigger capacity UI refresh so the fill bar updates on the client
            BagPassengerManager.MarkMassDirty(bagController);
            BagPassengerManager.ForceRecalculateMass(bagController);

            // Log that we're about to sync the bag state
            Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] About to sync bag state for {0} baggedCount={1} selectedIndex={2} isThrow={3}", bagController.name, msg.baggedIds.Length, msg.selectedIndex, msg.isThrowOperation);

            // Sync bagged object tracking list
            var receivedObjects = new List<GameObject>();
            foreach (var idValue in msg.baggedIds)
            {
                var obj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue));
                if (obj != null)
                {
                    receivedObjects.Add(obj);

                    // Restore preserved state if missing (happens after client-side throw)
                    var existingState = API.DrifterBagAPI.LoadObjectState(bagController, obj);
                    if (existingState == null && msg.isThrowOperation)
                    {
                        API.DrifterBagAPI.RestorePreservedState(bagController, obj);
                    }
                }
                else
                {
                    Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Could not find object for netId {0}", idValue);
                }
            }
            API.DrifterBagAPI.SetBaggedObjects(bagController, receivedObjects);

            // Update breakout times in local storage for all received objects
            for (int i = 0; i < msg.baggedIds.Length; i++)
            {
                var obj = ClientScene.FindLocalObject(new NetworkInstanceId(msg.baggedIds[i]));
                if (obj != null && i < msg.breakoutTimes.Length && i < msg.elapsedBreakoutTimes.Length)
                {
                    var state = API.DrifterBagAPI.LoadObjectState(bagController, obj);
                    if (state == null)
                    {
                        state = new BaggedObjectStateData();
                        state.CalculateFromObject(obj, bagController);
                    }

                    if (state != null)
                    {
                        state.breakoutTime = msg.breakoutTimes[i];
                        state.elapsedBreakoutTime = msg.elapsedBreakoutTimes[i];
                        API.DrifterBagAPI.SaveObjectState(bagController, obj, state);

                        Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Synced breakout timer for {0}: {1:F1}/{2:F1}s", obj.name, state.elapsedBreakoutTime, state.breakoutTime);
                    }
                }
            }

            // Clean up all temporary preserved states for this controller
            API.DrifterBagAPI.ClearAllTemporaryPreservation(bagController);

            Log.DebugIfEnabled("[HandleBagStateUpdatedMessage] Bag state updated for {0} new selectedIndex={1}", bagController.name, netController.selectedIndex);
        }

        // ========================================================================================
        // HELPER LOGIC
        // ========================================================================================

        private static bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj, VehicleSeat[]? seats = null)
        {
            if (controller == null || obj == null) return false;

            // Check main seat
            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                if (controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    return true;
            }

            // Check additional seats
            var seatDict = API.DrifterBagAPI.GetAdditionalSeats(controller);
            if (seatDict != null)
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && kvp.Value.hasPassenger && kvp.Value.NetworkpassengerBodyObject == obj)
                        return true;
                }
            }

            // Also check child VehicleSeats
            var childSeats = seats ?? controller.GetComponentsInChildren<VehicleSeat>(true);
            foreach (var seat in childSeats)
            {
                if (seat != controller.vehicleSeat && seat.hasPassenger && seat.NetworkpassengerBodyObject == obj)
                    return true;
            }

            return false;
        }
    }
}

