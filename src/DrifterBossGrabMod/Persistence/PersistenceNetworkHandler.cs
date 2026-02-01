using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using RoR2.Projectile;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod
{
    public static class PersistenceNetworkHandler
    {
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 201;

        [NetworkMessageHandler(msgType = BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, client = true, server = false)]
        public static void HandleBaggedObjectsPersistenceMessage(NetworkMessage netMsg)
        {
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            message.Deserialize(netMsg.reader);

            if (!NetworkServer.active)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleBaggedObjectsPersistenceMessage] Client received persistence msg - processing to track persisted object");
                }
            }
              
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[HandleBaggedObjectsPersistenceMessage] Received bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
            }
            for (int i = 0; i < message.baggedObjectNetIds.Count; i++)
            {
                var netId = message.baggedObjectNetIds[i];
                string? ownerPlayerId = null;
                if (i < message.ownerPlayerIds.Count)
                {
                    ownerPlayerId = message.ownerPlayerIds[i];
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[HandleBaggedObjectsPersistenceMessage] Received owner ID for netId {netId.Value}: '{ownerPlayerId ?? "null"}'");
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[HandleBaggedObjectsPersistenceMessage] No owner ID available for netId {netId.Value} (index {i} >= ownerPlayerIds.Count {message.ownerPlayerIds.Count})");
                    }
                 }

                   GameObject? obj = FindObjectByNetIdWithRetry(netId, maxRetries: 3, retryDelay: 0.1f);

                   if (PluginConfig.Instance.EnableDebugLogs.Value)
                   {
                       var projectileController = obj != null ? obj.GetComponent<ThrownObjectProjectileController>() : null;
                       var isPersisted = obj != null && PersistenceObjectManager.IsObjectPersisted(obj);
                       var isValid = obj != null && PersistenceObjectManager.IsValidForPersistence(obj);
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] === VALIDATION CHECK ===");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] Object: {obj?.name ?? "null"} (NetID: {netId.Value})");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] obj != null: {obj != null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] IsValidForPersistence: {isValid}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] IsObjectPersisted: {isPersisted}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] Has ThrownObjectProjectileController: {projectileController != null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] IsBlacklisted: {obj != null && PluginConfig.IsBlacklisted(obj.name)}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] NetworkServer.active: {NetworkServer.active}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] Will process network message: {obj != null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] ================================");
                   }
                   // On client side, trust network messages and add object to persistence even if not already persisted locally
                   // This fixes the issue where host has object in persistence but client doesn't
                   // Note: We still check for thrown objects and blacklist to prevent invalid objects from being persisted
                   if (obj != null)
                   {
                       var projectileControllerCheck = obj.GetComponent<ThrownObjectProjectileController>();
                       var isBlacklisted = PluginConfig.IsBlacklisted(obj.name);
                       if (projectileControllerCheck != null && !isBlacklisted)
                       {
                           return;
                       }
                       else if (PluginConfig.Instance.EnableDebugLogs.Value)
                       {
                           if (projectileControllerCheck != null)
                           {
                               Log.Warning($"[HandleBaggedObjectsPersistenceMessage] SKIPPING object {obj.name} - has ThrownObjectProjectileController (thrown objects excluded from persistence)");
                           }
                           else if (isBlacklisted)
                           {
                               Log.Warning($"[HandleBaggedObjectsPersistenceMessage] SKIPPING object {obj.name} - is blacklisted");
                           }
                       }
                   }
                   if (obj == null && PluginConfig.Instance.EnableDebugLogs.Value)
                   {
                       Log.Warning($"[HandleBaggedObjectsPersistenceMessage] Could not find object with netId {netId.Value}");
                       return;
                   }
                   // Process the object - add to persistence and add ModelStatePreserver
                   var existingPreserver = obj.GetComponent<ModelStatePreserver>();
                   var modelLocator = obj.GetComponent<ModelLocator>();
                   if (PluginConfig.Instance.EnableDebugLogs.Value)
                   {
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] === CLIENT-SIDE PERSISTENCE CHECK ===");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] Object: {obj.name} (NetID: {netId.Value})");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] EnableObjectPersistence: {PluginConfig.Instance.EnableObjectPersistence.Value}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] ModelLocator exists: {modelLocator != null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] ModelTransform exists: {modelLocator != null && modelLocator.modelTransform != null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] ModelStatePreserver already exists: {existingPreserver != null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] NetworkServer.active: {NetworkServer.active}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] Will add ModelStatePreserver: {PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator != null && modelLocator.modelTransform != null && existingPreserver == null}");
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] =======================================");
                   }
                   if (PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator != null && modelLocator.modelTransform != null && existingPreserver == null)
                   {
                       if (PluginConfig.Instance.EnableDebugLogs.Value)
                           Log.Info($"[HandleBaggedObjectsPersistenceMessage] Adding ModelStatePreserver to persisted object {obj.name} (Persistence enabled)");
                       obj.AddComponent<ModelStatePreserver>();
                   }
                   else if (!PluginConfig.Instance.EnableObjectPersistence.Value && PluginConfig.Instance.EnableDebugLogs.Value)
                   {
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] SKIPPING ModelStatePreserver for {obj.name} - Persistence is DISABLED");
                   }
                   PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);

                   if (PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator != null && !modelLocator.autoUpdateModelTransform)
                   {
                       modelLocator.autoUpdateModelTransform = true;
                   }
                   
                   if (PluginConfig.Instance.EnableDebugLogs.Value)
                   {
                       Log.Info($"[HandleBaggedObjectsPersistenceMessage] Added object {obj.name} (netId: {netId}) to persistence from network message");
                   }
            }
        }
        
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
                         if (owner != null)
                        {
                            var ownerBody = owner.GetComponent<CharacterBody>();
                            if (ownerBody != null && ownerBody.master != null && ownerBody.master.playerCharacterMasterController != null)
                             {
                                 var networkUserId = ownerBody.master.playerCharacterMasterController.networkUser.id;
                                 var playerIdString = networkUserId.strValue != null
                                    ? networkUserId.strValue
                                    : $"{networkUserId.value}_{networkUserId.subId}";
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($"[SendBaggedObjectsPersistenceMessage] Adding owner ID for {obj.name}: {playerIdString} (value: {networkUserId.value}, strValue: {networkUserId.strValue}, subId: {networkUserId.subId})");
                                }
                                message.ownerPlayerIds.Add(playerIdString);
                            }
                            else
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($"[SendBaggedObjectsPersistenceMessage] No owner info for {obj.name} - ownerBody={ownerBody != null}, master={ownerBody?.master != null}, pcmc={ownerBody?.master?.playerCharacterMasterController != null}");
                                }
                                message.ownerPlayerIds.Add(string.Empty);
                            }
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[SendBaggedObjectsPersistenceMessage] Owner is null for {obj.name}");
                        }
                        message.ownerPlayerIds.Add(string.Empty);
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

        public static void RegisterNetworkHandlers()
        {
            if (NetworkServer.active)
            {
                Stage.onServerStageComplete += OnServerStageComplete;
            }
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
                if (DrifterBossGrabPlugin.Instance != null)
                {
                    DrifterBossGrabPlugin.Instance.StartCoroutine(RetryFindController(msg));
                }
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

            if (msg.baggedIds != null)
            {
                foreach (var netId in msg.baggedIds)
                    {
                         var obj = FindObjectByNetId(new NetworkInstanceId(netId));
                      if (obj != null)
                      {
                          var modelLocator = obj.GetComponent<ModelLocator>();
                          var existingPreserver = obj.GetComponent<ModelStatePreserver>();

                          if (PluginConfig.Instance.EnableDebugLogs.Value)
                         {
                             Log.Info($"[HandleUpdateBagStateMessage] === CLIENT-SIDE BAG STATE UPDATE ===");
                             Log.Info($"[HandleUpdateBagStateMessage] Object: {obj.name} (NetID: {netId})");
                             Log.Info($"[HandleUpdateBagStateMessage] EnableObjectPersistence: {PluginConfig.Instance.EnableObjectPersistence.Value}");
                             Log.Info($"[HandleUpdateBagStateMessage] ModelLocator exists: {modelLocator != null}");
                             Log.Info($"[HandleUpdateBagStateMessage] ModelTransform exists: {modelLocator != null && modelLocator.modelTransform != null}");
                             Log.Info($"[HandleUpdateBagStateMessage] ModelStatePreserver already exists: {existingPreserver != null}");
                             Log.Info($"[HandleUpdateBagStateMessage] NetworkServer.active: {NetworkServer.active}");
                             Log.Info($"[HandleUpdateBagStateMessage] Will add ModelStatePreserver: {PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator != null && modelLocator.modelTransform != null && existingPreserver == null}");
                             Log.Info($"[HandleUpdateBagStateMessage] =======================================");
                         }
                         if (PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator != null && modelLocator.modelTransform != null && existingPreserver == null)
                         {
                             if (PluginConfig.Instance.EnableDebugLogs.Value)
                                 Log.Info($"[HandleUpdateBagStateMessage] Adding ModelStatePreserver to mapped client object {obj.name} (Persistence enabled)");

                             obj.AddComponent<ModelStatePreserver>();
                        }
                        else if (!PluginConfig.Instance.EnableObjectPersistence.Value && PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[HandleUpdateBagStateMessage] SKIPPING ModelStatePreserver for {obj.name} - Persistence is DISABLED");
                        }

                         if (PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator != null && !modelLocator.autoUpdateModelTransform)
                        {
                            modelLocator.autoUpdateModelTransform = true;
                        }
                     }
                 }
             }

             netController.ApplyStateFromMessage(msg.selectedIndex, msg.baggedIds ?? Array.Empty<uint>(), msg.seatIds ?? Array.Empty<uint>(), msg.scrollDirection);
        }
        
        private static System.Collections.IEnumerator RetryFindController(UpdateBagStateMessage msg)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
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

        private static GameObject? FindObjectByNetId(NetworkInstanceId netId)
        {
            return FindObjectByNetIdWithRetry(netId, maxRetries: 1, retryDelay: 0f);
        }

        private static GameObject? FindObjectByNetIdWithRetry(NetworkInstanceId netId, int maxRetries, float retryDelay)
        {
            if (netId == NetworkInstanceId.Invalid) return null; 
            
            GameObject? foundObj = null;
            int attempt = 0;
            GameObject[] persistedObjects = PersistenceObjectManager.GetPersistedObjects();
            
                 while (attempt < maxRetries && foundObj == null)
                 {
                     attempt++;

                     if (!NetworkServer.active)
                {
                    var dontDestroyOnLoadScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("DontDestroyOnLoad");
                    if (dontDestroyOnLoadScene.IsValid() && dontDestroyOnLoadScene.isLoaded)
                    {
                        foreach (var rootObj in dontDestroyOnLoadScene.GetRootGameObjects())
                        {
                            if (rootObj != null)
                            {
                                var identity = rootObj.GetComponent<NetworkIdentity>();
                                if (identity != null && identity.netId == netId)
                                {
                                    foundObj = rootObj;
                                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"[FindObjectByNetIdWithRetry] Found object {rootObj.name} (NetID: {netId}) in DontDestroyOnLoad");
                                    }
                                    break;
                                }
                            }
                         }
                     }
                 }

                 if (foundObj == null)
                {
                    foreach (var key in persistedObjects)
                    {
                        if (key != null)
                        {
                            var identity = key.GetComponent<NetworkIdentity>();
                            if (identity != null && identity.netId == netId) 
                            {
                                foundObj = key;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"[FindObjectByNetIdWithRetry] Found object {key.name} (NetID: {netId}) in bagged objects");
                                    }
                                 break;
                             }
                         }
                     }
                 }

                 if (foundObj == null)
                {
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
                                        foundObj = obj;
                                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                                            {
                                                Log.Info($"[FindObjectByNetIdWithRetry] Found object {obj.name} (NetID: {netId}) in bagged objects");
                                            }
                                        break;
                                    }
                                }
                            }
                        }
                     }
                 }

                 if (foundObj == null)
                 {
                     foundObj = ClientScene.FindLocalObject(netId);
                 }

                 if (foundObj == null && NetworkServer.active)
                 {
                     try
                     {
                         foundObj = NetworkServer.FindLocalObject(netId);
                     }
                     catch { }
                 }
             }

             if (foundObj == null && PluginConfig.Instance.EnableDebugLogs.Value)
             {
                 Log.Warning($"[FindObjectByNetIdWithRetry] Failed to find object locally for NetID {netId.Value} after {maxRetries} attempts. Checked Persistence ({(persistedObjects != null ? persistedObjects.Length : 0)}), BaggedObjects, and NetworkServer.");
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value && maxRetries > 1)
            {
                Log.Info($"[FindObjectByNetIdWithRetry] Found object for NetID {netId.Value} on attempt {attempt}/{maxRetries}");
            }
            
            return foundObj;
        }
        
        private static void OnServerStageComplete(Stage stage)
        {
            if (!NetworkServer.active) return;

            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var controller in bagControllers)
            {
                Patches.BagPatches.UpdateNetworkBagState(controller, 0);

                if (Patches.BagPatches.baggedObjectsDict.TryGetValue(controller, out var list))
                {
                    SendBaggedObjectsPersistenceMessage(list, controller);
                }
            }
        }
    }
}
