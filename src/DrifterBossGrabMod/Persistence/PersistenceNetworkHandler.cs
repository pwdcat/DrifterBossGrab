using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.Networking;
using RoR2;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod
{

    public static class PersistenceNetworkHandler
    {
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 201;

        // Handle incoming bagged objects persistence messages
        [NetworkMessageHandler(msgType = BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, client = true, server = false)]
        public static void HandleBaggedObjectsPersistenceMessage(NetworkMessage netMsg)
        {
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            message.Deserialize(netMsg.reader);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[HandleBaggedObjectsPersistenceMessage] Received bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
            }
            // Add the received objects to persistence
            for (int i = 0; i < message.baggedObjectNetIds.Count; i++)
            {
                var netId = message.baggedObjectNetIds[i];
                string? ownerPlayerId = null;
                if (i < message.ownerPlayerIds.Count)
                {
                    ownerPlayerId = message.ownerPlayerIds[i];
                }
                GameObject obj = ClientScene.FindLocalObject(netId);
                if (obj != null && PersistenceObjectManager.IsValidForPersistence(obj))
                {
                    PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[HandleBaggedObjectsPersistenceMessage] Added object {obj.name} (netId: {netId}) to persistence from network message");
                    }
                }
                else
                {
                    // Start retry coroutine for unfound objects
                    DrifterBossGrabPlugin.Instance.StartCoroutine(RetryFindObject(netId, ownerPlayerId));
                }
            }
        }

        private static System.Collections.IEnumerator RetryFindObject(NetworkInstanceId netId, string? ownerPlayerId = null)
        {
            // Increase retries to handle latency or slow object spawning
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Wait 10 frames
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return null;
                }
                GameObject obj = ClientScene.FindLocalObject(netId);
                if (obj != null && PersistenceObjectManager.IsValidForPersistence(obj))
                {
                    PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[RetryFindObject] Added object {obj.name} (netId: {netId}) to persistence after retry (attempt {attempt + 1})");
                    }
                    yield break;
                }
            }
            Log.Error($"[RetryFindObject] Failed to find object with netId {netId} after 10 retries");
        }

        // Send bagged objects persistence message to all clients
        public static void SendBaggedObjectsPersistenceMessage(List<GameObject> baggedObjects, DrifterBagController? owner = null)
        {
            if (baggedObjects == null || baggedObjects.Count == 0) return;
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            foreach (var obj in baggedObjects)
            {
                if (obj != null)
                {
                    NetworkIdentity? identity = obj.GetComponent<NetworkIdentity>();
                    if (identity != null)
                    {
                        message.baggedObjectNetIds.Add(identity.netId);
                        // Add owner player id if available
                        if (owner != null)
                        {
                            var ownerBody = owner.GetComponent<CharacterBody>();
                            if (ownerBody != null && ownerBody.master != null && ownerBody.master.playerCharacterMasterController != null)
                            {
                                message.ownerPlayerIds.Add(ownerBody.master.playerCharacterMasterController.networkUser.id.ToString());
                            }
                            else
                            {
                                message.ownerPlayerIds.Add(string.Empty);
                            }
                        }
                        else
                        {
                            message.ownerPlayerIds.Add(string.Empty);
                        }
                    }
                }
            }
            if (message.baggedObjectNetIds.Count > 0)
            {
                NetworkServer.SendToAll(BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, message);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[SendBaggedObjectsPersistenceMessage] Sent bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
                }
            }
        }

        private const short MSG_UPDATE_BAG_STATE = 206;

        // Register network message handler
        public static void RegisterNetworkHandlers()
        {
            if (NetworkServer.active)
            {
                Stage.onServerStageComplete += OnServerStageComplete;
            }
            // Explicitly register client handlers if client is active
            if (NetworkManager.singleton != null && NetworkManager.singleton.client != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info("[PersistenceNetworkHandler] Registering client handlers manually");
                NetworkManager.singleton.client.RegisterHandler(MSG_UPDATE_BAG_STATE, HandleUpdateBagStateMessage);
                NetworkManager.singleton.client.RegisterHandler(BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, HandleBaggedObjectsPersistenceMessage);
            }
        }

        [NetworkMessageHandler(msgType = MSG_UPDATE_BAG_STATE, client = true, server = false)]
        public static void HandleUpdateBagStateMessage(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<UpdateBagStateMessage>();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[HandleUpdateBagStateMessage] Received update for controller NetID: {msg.controllerNetId.Value}, index: {msg.selectedIndex}, objects: {msg.baggedIds.Length}");
            }

            var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (controllerObj == null)
            {
                 if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Warning($"[HandleUpdateBagStateMessage] Could not find controller object with NetID {msg.controllerNetId.Value}");
                 return;
            }

            var netController = controllerObj.GetComponent<BottomlessBagNetworkController>();
            if (netController == null)
            {
                 if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Warning("[HandleUpdateBagStateMessage] Object does not have BottomlessBagNetworkController");
                 return;
            }

            // Manually trigger the update on the component
            netController.ApplyStateFromMessage(msg.selectedIndex, msg.baggedIds, msg.seatIds);
        }

        private static void OnServerStageComplete(Stage stage)
        {
            if (!NetworkServer.active) return;

            // Re-sync all bag states to all clients after scene load
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var controller in bagControllers)
            {
                Patches.BagPatches.UpdateNetworkBagState(controller);
                
                // Also send persistence messages
                if (Patches.BagPatches.baggedObjectsDict.TryGetValue(controller, out var list))
                {
                    SendBaggedObjectsPersistenceMessage(list, controller);
                }
            }
        }
    }
}
