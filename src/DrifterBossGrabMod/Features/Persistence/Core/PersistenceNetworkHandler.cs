#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using RoR2.Projectile;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Networking;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod
{
    // ========================================================================================
    // PERSISTENCE NETWORK HANDLER
    // ========================================================================================

    public static class PersistenceNetworkHandler
    {
        // ========================================================================================
        // OUTBOUND MESSAGES
        // ========================================================================================

        public static void SendBaggedObjectsPersistenceMessage(List<GameObject> baggedObjects, DrifterBagController? owner = null)
        {
            if (baggedObjects == null || baggedObjects.Count == 0) return;

            var message = new BaggedObjectsPersistenceMessage();
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
                                message.ownerPlayerIds.Add(playerIdString);
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

                        // Check if colliders are disabled for this object
                        bool collidersDisabled = false;
                        if (owner != null)
                        {
                            collidersDisabled = API.DrifterBagAPI.AreCollidersDisabled(owner, obj);
                        }
                        message.collidersDisabled.Add(collidersDisabled);

                    }
                }
            }

            if (message.baggedObjectNetIds.Count > 0)
            {
                NetworkServer.SendToAll(Constants.Network.BaggedObjectsPersistenceMessageType, message);
            }
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS
        // ========================================================================================

        [NetworkMessageHandler(msgType = Constants.Network.BaggedObjectsPersistenceMessageType, client = true, server = false)]
        public static void HandleBaggedObjectsPersistenceMessage(NetworkMessage netMsg)
        {
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            message.Deserialize(netMsg.reader);

            for (int i = 0; i < message.baggedObjectNetIds.Count; i++)
            {
                var netId = message.baggedObjectNetIds[i];
                string? ownerPlayerId = null;
                bool collidersDisabled = false;

                if (i < message.ownerPlayerIds.Count)
                {
                    ownerPlayerId = message.ownerPlayerIds[i];
                }

                if (i < message.collidersDisabled.Count)
                {
                    collidersDisabled = message.collidersDisabled[i];
                }

                GameObject? obj = FindObjectByNetIdWithRetry(netId, maxRetries: 3, retryDelay: 0.1f);

                if (obj != null)
                {
                    var projectileControllerCheck = obj.GetComponent<ThrownObjectProjectileController>();
                    var isBlacklisted = PluginConfig.IsBlacklisted(obj.name);
                    if (projectileControllerCheck != null && !isBlacklisted)
                    {
                        return;
                    }
                }

                if (obj == null)
                {
                    return;
                }

                // Only patch stale references for special objects (teleporters, etc) during scene restoration, not during cycling/network sync
                if (PersistenceSceneHandler.IsRestoringFromSceneChange())
                {
                    PersistenceSceneHandler.HandleSpecialObjectRestoration(obj, duringSceneRestoration: true);
                }
                else
                {
                    // During cycling, just register as secondary without applying state changes
                    var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();
                    if (teleporterInteraction != null)
                    {
                        Log.DebugIfEnabled(" Found TeleporterInteraction on {0} during cycling. Registering as secondary only.", teleporterInteraction.gameObject.name);
                        MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);
                    }
                }

                // Refresh visual state to clear pink textures or shader artifacts after stage transition
                if (PersistenceSceneHandler.IsRestoringFromSceneChange())
                {
                    VisualRefreshUtility.Refresh(obj);
                }

                PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);

                // Apply collider disabled state if needed
                if (collidersDisabled && !NetworkServer.active)
                {
                    // Find controller for this object
                    DrifterBagController? controller = null;
                    foreach (var ctrl in API.DrifterBagAPI.GetAllControllers())
                    {
                        var list = API.DrifterBagAPI.GetBaggedObjects(ctrl);
                        if (list != null && list.Contains(obj))
                        {
                            controller = ctrl;
                            break;
                        }
                    }

                    if (controller != null)
                    {
                        if (controller != null)
                        {
                            var objectDisabledStates = API.DrifterBagAPI.GetOrCreateDisabledColliders(controller, obj);
                            // Disable colliders on client side
                            BodyColliderCache.DisableMovementColliders(obj, objectDisabledStates);
                        }
                    }
                }
            }
        }

        // ========================================================================================
        // LIFECYCLE HOOKS
        // ========================================================================================

        public static void RegisterServerHooks()
        {
            if (NetworkServer.active)
            {
                Stage.onServerStageComplete += OnServerStageComplete;
            }
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
                                break;
                            }
                        }
                    }
                }

                if (foundObj == null)
                {
                    foreach (var controller in API.DrifterBagAPI.GetAllControllers())
                    {
                        var list = API.DrifterBagAPI.GetBaggedObjects(controller);
                        if (list != null)
                        {
                            foreach (var obj in list)
                            {
                                if (obj != null)
                                {
                                    var identity = obj.GetComponent<NetworkIdentity>();
                                    if (identity != null && identity.netId == netId)
                                    {
                                        foundObj = obj;
                                        break;
                                    }
                                }
                            }
                        }
                        if (foundObj != null) break;
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
                    catch
                    {
                        foundObj = null;
                    }
                }
            }

            return foundObj;
        }

        private static void OnServerStageComplete(Stage stage)
        {
            if (!NetworkServer.active) return;

            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var controller in bagControllers)
            {
                BagCarouselUpdater.UpdateNetworkBagState(controller, 0);

                var list = API.DrifterBagAPI.GetBaggedObjects(controller);
                if (list != null)
                {
                    SendBaggedObjectsPersistenceMessage(list, controller);
                }
            }
        }
    }
}
