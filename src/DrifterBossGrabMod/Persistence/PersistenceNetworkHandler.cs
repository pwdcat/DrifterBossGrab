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
                GameObject? obj = FindObjectByNetId(netId);
                if (obj != null && PersistenceObjectManager.IsValidForPersistence(obj))
                {
                    PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);
                    
                    // Also ensure ModelStatePreserver is attached for client-side model state restoration
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null && obj.GetComponent<ModelStatePreserver>() == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[HandleBaggedObjectsPersistenceMessage] Adding ModelStatePreserver to persisted object {obj.name}");
                        obj.AddComponent<ModelStatePreserver>();
                        if (!modelLocator.autoUpdateModelTransform) modelLocator.autoUpdateModelTransform = true;
                    }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[HandleBaggedObjectsPersistenceMessage] Added object {obj.name} (netId: {netId}) to persistence from network message");
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Warning($"[HandleBaggedObjectsPersistenceMessage] Could not find object with netId {netId} to persist (attempt failed)");
                    }
                }
            }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Warning($"[HandleUpdateBagStateMessage] Could not find controller object with NetID {msg.controllerNetId.Value} - will retry.");
                // Retry in coroutine handles race condition during scene loading
                DrifterBossGrabPlugin.Instance.StartCoroutine(RetryFindController(msg));
                return;
            }

            ApplyBagStateUpdate(controllerObj, msg);
        }

        private static void ApplyBagStateUpdate(GameObject controllerObj, UpdateBagStateMessage msg)
        {
            var netController = controllerObj.GetComponent<BottomlessBagNetworkController>();
            if (netController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Warning("[HandleUpdateBagStateMessage] Object does not have BottomlessBagNetworkController");
                return;
            }

            // Ensure clients have ModelStatePreserver attached to bagged objects to prevent desync on throw
            if (msg.baggedIds != null)
            {
                foreach (var netId in msg.baggedIds)
                {
                    var obj = FindObjectByNetId(new NetworkInstanceId(netId));
                    if (obj != null)
                    {
                        var modelLocator = obj.GetComponent<ModelLocator>();
                        // Only add if not already present and model exists
                        if (modelLocator != null && modelLocator.modelTransform != null && obj.GetComponent<ModelStatePreserver>() == null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[HandleUpdateBagStateMessage] Adding ModelStatePreserver to mapped client object {obj.name}");
                            
                            // Attach component to preserve state (Capture happens in Awake)
                            obj.AddComponent<ModelStatePreserver>();
                            
                            // Also ensure autoUpdateModelTransform is true so the model follows the object while bagged
                            if (!modelLocator.autoUpdateModelTransform)
                            {
                               modelLocator.autoUpdateModelTransform = true;
                            }
                        }
                    }
                }
            }

            // Manually trigger the update on the component
            netController.ApplyStateFromMessage(msg.selectedIndex, msg.baggedIds, msg.seatIds, msg.scrollDirection);
        }

        private static System.Collections.IEnumerator RetryFindController(UpdateBagStateMessage msg)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Wait 10 frames
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return null;
                }

                var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
                if (controllerObj != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[RetryFindController] Found controller object {controllerObj.name} (netId: {msg.controllerNetId.Value}) after retry (attempt {attempt + 1})");
                    }
                    ApplyBagStateUpdate(controllerObj, msg);
                    yield break;
                }
            }
            Log.Error($"[RetryFindController] Failed to find controller object with netId {msg.controllerNetId.Value} after 10 retries");
        }

        // Helper to find object by NetID with multiple fallback strategies
        private static GameObject? FindObjectByNetId(NetworkInstanceId netId)
        {
            if (netId == NetworkInstanceId.Invalid) return null;

            // 1. Check if we already have it in persistence (Fastest, avoids ClientScene lookup issues on Host)
            var persistedObjects = PersistenceObjectManager.GetPersistedObjects();
            foreach (var key in persistedObjects)
            {
                if (key != null)
                {
                    var identity = key.GetComponent<NetworkIdentity>();
                    if (identity != null)
                    {
                        if (identity.netId == netId) return key;
                        // On Host, netId matches might fail if the internal value is 0 but the object is valid.
                        // We trust our persistence list.
                    }
                }
            }

            // 2. Fallback: Iterate the actual Persistence Container children (brute force, failsafe)
            // This catches cases where the HashSet might be out of sync or the object was restored but not fully re-registered in the set yet?
            // (Unlikely given the code, but the user insists on using the container)
            var container = GameObject.Find("DBG_PersistenceContainer");
            if (container != null)
            {
                 var identities = container.GetComponentsInChildren<NetworkIdentity>(true);
                 foreach(var id in identities)
                 {
                     if (id.netId == netId) return id.gameObject;
                 }
            }

            // 3. Check currently bagged objects (in case it's in a bag but not yet persisted)
            var baggedObjects = Patches.BagPatches.baggedObjectsDict;
            foreach (var kvp in baggedObjects)
            {
                if (kvp.Value != null)
                {
                    foreach (var obj in kvp.Value)
                    {
                        if (obj != null)
                        {
                            var identity = obj.GetComponent<NetworkIdentity>();
                            if (identity != null && identity.netId == netId)
                            {
                                return obj;
                            }
                        }
                    }
                }
            }

            // 4. Standard Network Client Lookup
            GameObject? foundObj = ClientScene.FindLocalObject(netId);
            
            // 5. Standard Network Server Lookup (Host/Server fallback)
            if (foundObj == null && NetworkServer.active)
            {
                try
                {
                     foundObj = NetworkServer.FindLocalObject(netId);
                }
                catch { /* Ignore server lookup errors */ }
            }
            
            if (foundObj == null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                 // Log why we failed to help debug Host issues
                 Log.Warning($"[FindObjectByNetId] Failed to find object locally for NetID {netId.Value}. Checked Persistence ({persistedObjects.Length}), BaggedObjects, and NetworkServer.");
            }

            return foundObj;
        }

        private static void OnServerStageComplete(Stage stage)
        {
            if (!NetworkServer.active) return;

            // Re-sync all bag states to all clients after scene load
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var controller in bagControllers)
            {
                Patches.BagPatches.UpdateNetworkBagState(controller, 0);
                
                // Also send persistence messages
                if (Patches.BagPatches.baggedObjectsDict.TryGetValue(controller, out var list))
                {
                    SendBaggedObjectsPersistenceMessage(list, controller);
                }
            }
        }
    }
}
