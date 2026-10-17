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

        public static void SendBagStateUpdate(DrifterBagController bagController, NetworkInstanceId removedObjectNetId, bool isThrowOperation = false)
        {
            if (!NetworkServer.active) return;

            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController == null) return;

            var baggedObjects = netController.GetBaggedObjects();
            var baggedIds = new uint[baggedObjects.Count];
            for (int i = 0; i < baggedObjects.Count; i++)
            {
                var netId = baggedObjects[i].GetComponent<NetworkIdentity>();
                baggedIds[i] = netId != null ? netId.netId.Value : 0;
            }

            var bagState = BagPatches.GetState(bagController);
            var seatIds = new List<uint>();
            var seenSeatIds = new HashSet<uint>();
            foreach (var seat in bagState.AdditionalSeats.Values)
            {
                if (seat == null || !seat.hasPassenger || !baggedObjects.Contains(seat.NetworkpassengerBodyObject)) continue;
                var identity = seat.GetComponent<NetworkIdentity>();
                if (identity != null && identity.netId != NetworkInstanceId.Invalid && seenSeatIds.Add(identity.netId.Value))
                    seatIds.Add(identity.netId.Value);
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
                emptySlotIndex = bagState.EmptySlotIndex,
                removedObjectNetId = removedObjectNetId,
                baggedIds = baggedIds,
                seatIds = seatIds.ToArray(),
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
            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.BreakoutPresentationSubMessageType, HandleBreakoutPresentation);
            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.SlamFeedbackSubMessageType, HandleSlamFeedback);
            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.CardDepartureSubMessageType, HandleCardDeparture);
        }

        public static void UnregisterMessages()
        {
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.ClientPreferencesSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.CycleRequestSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.ClientUpdateBagStateSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.GrabObjectSubMessageType);
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.BagStateUpdatedSubMessageType);
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.BreakoutPresentationSubMessageType);
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.SlamFeedbackSubMessageType);
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.CardDepartureSubMessageType);
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS (SERVER)
        // ========================================================================================
        internal static bool IsRequestOwner(GameObject controllerObject, NetworkConnection connection)
        {
            if (!NetworkServer.active || controllerObject == null || connection == null) return false;
            var identity = controllerObject.GetComponent<NetworkIdentity>();
            if (identity == null) return false;
            var owner = identity.clientAuthorityOwner ?? identity.connectionToClient ??
                controllerObject.GetComponent<CharacterBody>()?.master?.playerCharacterMasterController?.networkUser?.connectionToClient;
            return owner != null && ReferenceEquals(owner, connection);
        }

        public static void HandleClientPreferencesMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new ClientPreferencesMessage();
            msg.Deserialize(reader);

            var controllerObj = NetworkServer.FindLocalObject(msg.controllerNetId);
            if (!controllerObj) return;
            if (!IsRequestOwner(controllerObj, conn)) return;

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
            if (!IsRequestOwner(controllerObj, conn)) return;

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
            if (!IsRequestOwner(controllerObj, conn)) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null) return;

            var netController = bagController.GetComponent<BottomlessBagNetworkController>();
            if (netController != null) netController.SendAuthoritativeBagState();
        }

        public static void HandleGrabObjectMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new GrabObjectMessage();
            msg.Deserialize(reader);

            var controllerObj = NetworkUtils.FindLocalObjectWithLogging(msg.bagControllerNetId, "HandleGrabObjectMessage", isServer: true);
            if (controllerObj == null) return;
            if (!IsRequestOwner(controllerObj, conn)) return;

            var bagController = controllerObj.GetComponent<DrifterBagController>();
            if (bagController == null)
            {
                Log.Warning($"[HandleGrabObjectMessage] {controllerObj.name} does not have DrifterBagController component");
                return;
            }

            var targetObject = NetworkUtils.FindLocalObjectWithLogging(msg.targetObjectNetId, "HandleGrabObjectMessage", isServer: true);
            if (targetObject == null) return;

            if (targetObject == controllerObj && (!controllerObj.TryGetComponent<CharacterBody>(out var selfBody)
                || !SelfGrabPatches.CanSelfGrab(selfBody))) return;

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
                Log.Warning($"[HandleGrabObjectMessage] Blocking grab request for {targetObject.name} - object is being thrown");
                return;
            }

            bagController.AssignPassenger(targetObject);
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS (CLIENT)
        // ========================================================================================
        private static void HandleSlamFeedback(NetworkReader reader, NetworkConnection conn)
        {
            if (NetworkServer.active) return;
            var message = new SlamFeedbackMessage();
            message.Deserialize(reader);
            var obj = ClientScene.FindLocalObject(message.controllerNetId);
            var controller = obj != null ? obj.GetComponent<DrifterBagController>() : null;
            var passenger = ClientScene.FindLocalObject(message.passengerNetId);
            if (controller != null && passenger != null)
                UI.BreakoutWarningOverlay.ApplySlamFeedback(controller, passenger, message.amplitude);
        }

        private static void HandleBreakoutPresentation(NetworkReader reader, NetworkConnection conn)
        {
            if (NetworkServer.active) return;
            var msg = new BreakoutPresentationMessage();
            msg.Deserialize(reader);
            var obj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (obj != null) obj.GetComponent<BottomlessBagNetworkController>()?.ApplyBreakoutPresentation(msg);
        }

        private static void HandleCardDeparture(NetworkReader reader, NetworkConnection conn)
        {
            if (NetworkServer.active) return;
            var msg = new CardDepartureMessage();
            msg.Deserialize(reader);
            var obj = ClientScene.FindLocalObject(msg.controllerNetId);
            var controller = obj != null ? obj.GetComponent<DrifterBagController>() : null;
            if (controller != null) BagCarouselUpdater.AnimatePassengerExit(controller, msg.passengerNetId.Value, msg.wasDestroyed);
        }

        internal static void SendCardDeparture(DrifterBagController controller, GameObject passenger, bool wasDestroyed)
        {
            if (!NetworkServer.active || passenger == null) return;
            var controllerIdentity = controller.GetComponent<NetworkIdentity>();
            var passengerIdentity = passenger.GetComponent<NetworkIdentity>();
            if (controllerIdentity == null || passengerIdentity == null) return;
            NetworkMessageRegistry.SendToAll(Constants.Network.CardDepartureSubMessageType, new CardDepartureMessage
            {
                controllerNetId = controllerIdentity.netId,
                passengerNetId = passengerIdentity.netId,
                wasDestroyed = wasDestroyed
            });
        }

        public static void HandleBagStateUpdatedMessage(NetworkReader reader, NetworkConnection conn)
        {
            if (NetworkServer.active) return;
            var msg = new BagStateUpdatedMessage();
            msg.Deserialize(reader);
            var revision = PersistenceNetworkHandler.RegisterClientBagSnapshot(msg.controllerNetId.Value, msg.baggedIds ?? Array.Empty<uint>());
            ApplyBagStateUpdatedMessage(msg, revision);
        }

        private static void ApplyBagStateUpdatedMessage(BagStateUpdatedMessage msg, object revision, bool retry = true)
        {
            var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (controllerObj == null)
            {
                if (retry && DrifterBossGrabPlugin.Instance != null)
                    DrifterBossGrabPlugin.Instance.StartCoroutine(RetryBagStateUpdatedMessage(msg, revision));
                Log.Debug($"[HandleBagStateUpdatedMessage] Waiting for controller netId={msg.controllerNetId.Value}");
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
                msg.elapsedBreakoutTimes, msg.breakoutAttempts, msg.breakoutTimes, msg.emptySlotIndex);

            if (msg.removedObjectNetId != NetworkInstanceId.Invalid)
            {
                PersistenceNetworkHandler.CancelPendingPersistence(msg.removedObjectNetId.Value);
                var removedObj = ClientScene.FindLocalObject(msg.removedObjectNetId);
                if (removedObj != null)
                {
                    Log.Debug($"[HandleBagStateUpdatedMessage] Cleaning up removed object {removedObj.name}");
                    NetworkUtils.InvalidateReadyCache(removedObj);

                    PersistenceObjectsTracker.UntrackBaggedObject(removedObj, false);

                    if (!msg.isThrowOperation || !NetworkServer.active)
                    {
                        BaggedObjectStatePatches.PerformPassengerRestoration(bagController, removedObj);
                        BagPassengerManager.RemoveBaggedObject(bagController, removedObj, isDestroying: false, preserveStateDuringThrow: msg.isThrowOperation);
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

            Log.Debug($"[HandleBagStateUpdatedMessage] Syncing bag state for {bagController.name} - baggedCount={(msg.baggedIds?.Length ?? 0)}, selectedIndex={msg.selectedIndex}, isThrow={msg.isThrowOperation}");

            var bagState = BagPatches.GetState(bagController);
            if (bagState != null && msg.baggedIds != null)
            {
                foreach (var idValue in msg.baggedIds)
                {
                    var obj = ClientScene.FindLocalObject(new NetworkInstanceId(idValue));
                    if (obj != null)
                    {
                        var existingState = BaggedObjectPatches.LoadObjectState(bagController, obj);
                        if (existingState == null && msg.isThrowOperation)
                        {
                            BaggedObjectPatches.RestorePreservedState(bagController, obj);
                        }
                    }
                    else
                    {
                        Log.Debug($"[HandleBagStateUpdatedMessage] Waiting for object netId={idValue}");
                    }
                }
            }

            Log.Debug($"[HandleBagStateUpdatedMessage] Bag state updated for {bagController.name} - new selectedIndex={netController.selectedIndex}");
        }

        private static System.Collections.IEnumerator RetryBagStateUpdatedMessage(BagStateUpdatedMessage msg, object revision)
        {
            var run = Run.instance;
            var client = NetworkManager.singleton?.client;
            float deadline = UnityEngine.Time.realtimeSinceStartup + 5f;
            while (UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (Run.instance != run || client == null || !client.isConnected || NetworkManager.singleton?.client != client ||
                    !PersistenceNetworkHandler.IsCurrentClientBagSnapshot(msg.controllerNetId.Value, revision)) yield break;
                if (ClientScene.FindLocalObject(msg.controllerNetId) == null) continue;
                ApplyBagStateUpdatedMessage(msg, revision, false);
                yield break;
            }
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
