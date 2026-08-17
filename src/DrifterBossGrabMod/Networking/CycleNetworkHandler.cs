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

    // ========================================================================================
    // CYCLE NETWORK HANDLER
    // ========================================================================================
    public static class CycleNetworkHandler
    {

        public static volatile bool SuppressBroadcasts = false;

        // ========================================================================================
        // OUTBOUND MESSAGES (CLIENT -> SERVER)
        // ========================================================================================
        public static void SendClientPreferences(NetworkIdentity controllerIdentity, bool autoPromote, bool prioritize)
        {
            var msg = new ClientPreferencesMessage
            {
                controllerNetId = controllerIdentity.netId,
                autoPromoteMainSeat = autoPromote,
                prioritizeMainSeat = prioritize
            };

            NetworkMessageRegistry.SendToServer(Constants.Network.ClientPreferencesSubMessageType, msg);
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

            NetworkMessageRegistry.SendToServer(Constants.Network.CycleRequestSubMessageType, msg);
        }

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

            NetworkMessageRegistry.SendToServer(Constants.Network.ClientUpdateBagStateSubMessageType, msg);
        }

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

            NetworkMessageRegistry.SendToServer(Constants.Network.GrabObjectSubMessageType, msg);
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
            for (int i = 0; i < baggedObjects.Count; i++)
            {
                var netId = baggedObjects[i].GetComponent<NetworkIdentity>();
                baggedIds[i] = netId != null ? netId.netId.Value : 0;
                seatIds[i] = netId != null ? netId.netId.Value : 0;
            }

            var elapsedTimes = new float[baggedObjects.Count];
            var attempts = new float[baggedObjects.Count];
            var totalTimes = new float[baggedObjects.Count];

            for (int i = 0; i < baggedObjects.Count; i++)
            {
                var obj = baggedObjects[i];
                var state = BaggedObjectPatches.LoadObjectState(bagController, obj);
                if (state != null)
                {
                    elapsedTimes[i] = state.elapsedBreakoutTime;
                    attempts[i] = state.breakoutAttempts;
                    totalTimes[i] = state.breakoutTime;
                }
            }

            var msg = new BagStateUpdatedMessage
            {
                controllerNetId = ni.netId,
                selectedIndex = netController.selectedIndex,
                removedObjectNetId = removedObjectNetId,
                baggedIds = baggedIds,
                seatIds = seatIds,
                scrollDirection = 0,
                isThrowOperation = isThrowOperation,
                elapsedBreakoutTimes = elapsedTimes,
                breakoutAttempts = attempts,
                breakoutTimes = totalTimes
            };

            NetworkMessageRegistry.SendToAll(Constants.Network.BagStateUpdatedSubMessageType, msg);

            Log.Debug($"[SendBagStateUpdate] Sent bag state update for {bagController.name} - selectedIndex={netController.selectedIndex}, isThrow={isThrowOperation}, removedObject={(removedObjectNetId == NetworkInstanceId.Invalid ? "none" : removedObjectNetId.Value.ToString())}");
        }

        public static void RegisterMessages()
        {
            NetworkMessageRegistry.RegisterServerSubHandler(Constants.Network.ClientPreferencesSubMessageType, HandleClientPreferencesMessage);
            NetworkMessageRegistry.RegisterServerSubHandler(Constants.Network.CycleRequestSubMessageType, HandleCycleRequestMessage);
            NetworkMessageRegistry.RegisterServerSubHandler(Constants.Network.ClientUpdateBagStateSubMessageType, HandleClientBagStateMessage);
            NetworkMessageRegistry.RegisterServerSubHandler(Constants.Network.GrabObjectSubMessageType, HandleGrabObjectMessage);
            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.BagStateUpdatedSubMessageType, HandleBagStateUpdatedMessage);
        }

        public static void UnregisterMessages()
        {
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.ClientPreferencesSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.CycleRequestSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.ClientUpdateBagStateSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.GrabObjectSubMessageType);
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.BagStateUpdatedSubMessageType);
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS (SERVER)
        // ========================================================================================
        public static void HandleClientPreferencesMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new ClientPreferencesMessage();
            msg.Deserialize(reader);

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;

            var netController = controllerObj.GetComponent<BottomlessBagNetworkController>();
            if (netController == null) return;

            netController.autoPromoteMainSeat = msg.autoPromoteMainSeat;
            netController.prioritizeMainSeat = msg.prioritizeMainSeat;
        }

        public static void HandleCycleRequestMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new CyclePassengersMessage();
            msg.Deserialize(reader);

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (!controllerObj) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController != null)
            {
                Log.Debug($"[CycleNetworkHandler.HandleCycleRequestMessage] Processing request: Controller={bagController.name}, Amount={msg.amount}.");
                PassengerCycler.ServerCyclePassengers(bagController, msg.amount);
            }
        }

        public static void HandleClientBagStateMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new ClientUpdateBagStateMessage();
            msg.Deserialize(reader);

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null) return;

            SuppressBroadcasts = true;
            try
            {
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

        public static void HandleGrabObjectMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new GrabObjectMessage();
            msg.Deserialize(reader);

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

            if (IsObjectInAnySeat(bagController, targetObject))
            {
                Log.Debug($"[HandleGrabObjectMessage] {targetObject.name} is already in a seat, skipping grab");
                return;
            }

            if (ProjectileRecoveryPatches.IsUndergoingThrowOperation(targetObject))
            {
                Log.Warning($"[HandleGrabObjectMessage] Blocking grab request for {targetObject.name} - object is currently undergoing throw operation");
                return;
            }

            bagController.AssignPassenger(targetObject);
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS (CLIENT)
        // ========================================================================================
        public static void HandleBagStateUpdatedMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new BagStateUpdatedMessage();
            msg.Deserialize(reader);

            var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (controllerObj == null)
            {
                Log.Debug($"[HandleBagStateUpdatedMessage] Controller (netId={msg.controllerNetId.Value}) not found - likely destroyed");
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

            NetworkUtils.LogNetworkOperation("HandleBagStateUpdatedMessage", controllerObj, isServer: false, new Dictionary<string, object>
            {
                { "selectedIndex", msg.selectedIndex },
                { "isThrowOperation", msg.isThrowOperation },
                { "removedObjectNetId", msg.removedObjectNetId.Value },
                { "baggedCount", msg.baggedIds.Length }
            });

            var previousMainPassenger = BagPatches.GetMainSeatObject(bagController);

            netController.ApplyStateFromMessage(msg.selectedIndex, msg.baggedIds, msg.seatIds, msg.scrollDirection,
                msg.elapsedBreakoutTimes, msg.breakoutAttempts, msg.breakoutTimes);

            if (msg.removedObjectNetId != NetworkInstanceId.Invalid)
            {
                var removedObj = ClientScene.FindLocalObject(msg.removedObjectNetId);
                if (removedObj != null)
                {
                    Log.Debug($"[HandleBagStateUpdatedMessage] Cleaning up removed object {removedObj.name}");
                    NetworkUtils.InvalidateReadyCache(removedObj);

                    PersistenceObjectsTracker.UntrackBaggedObject(removedObj, false);

                    if (!msg.isThrowOperation || !NetworkServer.active)
                    {
                        BaggedObjectStatePatches.PerformPassengerRestoration(bagController, removedObj);
                        BagPassengerManager.RemoveBaggedObject(bagController, removedObj, isDestroying: false);
                    }

                    if (previousMainPassenger == removedObj || msg.selectedIndex < 0)
                    {
                        BaggedObjectStatePatches.ForceCleanupOverrides(bagController, removedObj);
                    }
                }
                else
                {
                    Log.Debug($"[HandleBagStateUpdatedMessage] Removed object (netId={msg.removedObjectNetId.Value}) not found - likely destroyed/already thrown");
                    if (previousMainPassenger != null)
                    {
                        BaggedObjectStatePatches.ForceCleanupOverrides(bagController, previousMainPassenger);
                    }
                }
            }

            if (msg.selectedIndex < 0 || msg.baggedIds == null || msg.baggedIds.Length == 0)
            {
                BaggedObjectStatePatches.ForceCleanupOverrides(bagController, previousMainPassenger);
            }

            BagCarouselUpdater.UpdateCarousel(bagController);

            Log.Debug($"[HandleBagStateUpdatedMessage] About to sync bag state for {bagController.name} - baggedCount={(msg.baggedIds?.Length ?? 0)}, selectedIndex={msg.selectedIndex}, isThrow={msg.isThrowOperation}");

            var bagState = BagPatches.GetState(bagController);
            if (bagState != null && msg.baggedIds != null)
            {
                bagState.BaggedObjects.Clear();
                foreach (var idValue in msg.baggedIds)
                {
                    var obj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        bagState.BaggedObjects.Add(obj);

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

                BaggedObjectPatches.ClearAllTemporaryPreservation(bagController);
            }

            Log.Debug($"[HandleBagStateUpdatedMessage] Bag state updated for {bagController.name} - new selectedIndex={netController.selectedIndex}");
        }

        private static bool IsObjectInAnySeat(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;

            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                if (controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    return true;
            }

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
    }
}
