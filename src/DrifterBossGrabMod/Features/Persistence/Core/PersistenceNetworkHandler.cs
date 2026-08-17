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

                        bool collidersDisabled = false;
                        if (owner != null)
                        {
                            var bagState = BagPatches.GetState(owner);
                            if (bagState != null && bagState.DisabledCollidersByObject != null && bagState.DisabledCollidersByObject.ContainsKey(obj))
                            {
                                collidersDisabled = bagState.DisabledCollidersByObject[obj].Count > 0;
                            }
                        }
                        message.collidersDisabled.Add(collidersDisabled);

                    }
                }
            }

            if (message.baggedObjectNetIds.Count > 0)
            {
                NetworkMessageRegistry.SendToAll(Constants.Network.BaggedObjectsPersistenceSubMessageType, message);
            }
        }

        public static void RegisterMessages()
        {
            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.BaggedObjectsPersistenceSubMessageType, HandleBaggedObjectsPersistenceMessage);
            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.UpdateBagStateSubMessageType, HandleUpdateBagStateMessage);
        }

        public static void UnregisterMessages()
        {
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.BaggedObjectsPersistenceSubMessageType);
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.UpdateBagStateSubMessageType);
        }

        // ========================================================================================
        // INBOUND MESSAGE HANDLERS
        // ========================================================================================
        public static void HandleBaggedObjectsPersistenceMessage(NetworkReader reader, NetworkConnection conn)
        {
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            message.Deserialize(reader);

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

                if (PersistenceSceneHandler.IsRestoringFromSceneChange())
                {
                    PersistenceSceneHandler.HandleSpecialObjectRestoration(obj, duringSceneRestoration: true);
                }
                else
                {

                    var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();
                    if (teleporterInteraction != null)
                    {
                        Log.Debug($" Found TeleporterInteraction on {teleporterInteraction.gameObject.name} during cycling. Registering as secondary only.");
                        MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);
                    }
                }

                if (PersistenceSceneHandler.IsRestoringFromSceneChange())
                {
                    VisualRefreshUtility.Refresh(obj);
                    if (PersistenceObjectsTracker.IsObjectCurrentlyBagged(obj))
                    {
                        PersistenceObjectsTracker.SetBaggedObjectVisibility(obj, false);
                    }
                }

                PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);

                if (collidersDisabled && !NetworkServer.active)
                {

                    DrifterBagController? controller = null;
                    foreach (var ctrl in Patches.BagPatches.GetAllControllers())
                    {
                        var list = BagPatches.GetState(ctrl).BaggedObjects;
                        if (list != null && list.Contains(obj))
                        {
                            controller = ctrl;
                            break;
                        }
                    }

                    if (controller != null)
                    {
                        var bagState = BagPatches.GetState(controller);
                        if (bagState != null)
                        {
                            if (!bagState.DisabledCollidersByObject.ContainsKey(obj))
                            {
                                bagState.DisabledCollidersByObject[obj] = new Dictionary<Collider, bool>();
                            }
                            var objectDisabledStates = bagState.DisabledCollidersByObject[obj];

                            BodyColliderCache.DisableMovementColliders(obj, objectDisabledStates);
                        }
                    }
                }
            }
        }

        public static void HandleUpdateBagStateMessage(NetworkReader reader, NetworkConnection conn)
        {
            var msg = new UpdateBagStateMessage();
            msg.Deserialize(reader);

            var controllerObj = ClientScene.FindLocalObject(msg.controllerNetId);
            if (controllerObj == null)
            {
                if (DrifterBossGrabPlugin.Instance != null)
                {
                    DrifterBossGrabPlugin.Instance.StartCoroutine(RetryFindController(msg));
                }
                return;
            }

            ApplyBagStateUpdate(controllerObj, msg);
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

        private static void ApplyBagStateUpdate(GameObject controllerObj, UpdateBagStateMessage msg)
        {
            var netController = controllerObj.GetComponent<BottomlessBagNetworkController>();
            if (netController == null)
            {
                return;
            }

            var controller = netController.GetComponent<DrifterBagController>();
            if (controller == null) return;

            var bagState = BagPatches.GetState(controller);
            if (bagState == null) return;

            if (!NetworkServer.active)
            {
                var currentObjects = bagState.BaggedObjects;
                if (currentObjects != null)
                {
                    var receivedObjects = new List<GameObject>();
                    if (msg.baggedIds != null)
                    {
                        foreach (var netId in msg.baggedIds)
                        {
                            var obj = FindObjectByNetId(new NetworkInstanceId(netId));
                            if (obj != null)
                            {
                                receivedObjects.Add(obj);
                            }
                        }
                    }

                    foreach (var obj in currentObjects.ToList())
                    {
                        if (obj == null || !obj || !receivedObjects.Contains(obj))
                        {
                            if (obj != null)
                            {
                                currentObjects.Remove(obj);
                                bagState.RemoveInstanceId(obj.GetInstanceID());
                            }
                        }
                    }
                }
            }

            if (msg.baggedIds != null)
            {
                foreach (var netId in msg.baggedIds)
                {
                    var obj = FindObjectByNetId(new NetworkInstanceId(netId));
                    if (obj != null)
                    {

                        int objIndex = System.Array.IndexOf(msg.baggedIds, netId);
                        if (objIndex >= 0 && objIndex < msg.collidersDisabled.Length && msg.collidersDisabled[objIndex] && !NetworkServer.active)
                        {
                            var bagStateColliders = BagPatches.GetState(netController.GetComponent<DrifterBagController>());
                            if (bagStateColliders != null)
                            {
                                if (!bagStateColliders.DisabledCollidersByObject.ContainsKey(obj))
                                {
                                    bagStateColliders.DisabledCollidersByObject[obj] = new Dictionary<Collider, bool>();
                                }
                                var objectDisabledStates = bagStateColliders.DisabledCollidersByObject[obj];

                                BodyColliderCache.DisableMovementColliders(obj, objectDisabledStates);
                            }
                        }
                    }
                }
            }
            var previousMain = BagPatches.GetMainSeatObject(controller);

            netController.ApplyStateFromMessage(msg.selectedIndex, msg.baggedIds ?? Array.Empty<uint>(), msg.seatIds ?? Array.Empty<uint>(), msg.scrollDirection,
                msg.elapsedBreakoutTimes, msg.breakoutAttempts, msg.breakoutTimes);

            if (!NetworkServer.active && (msg.selectedIndex < 0 || msg.baggedIds == null || msg.baggedIds.Length == 0))
            {
                BaggedObjectStatePatches.ForceCleanupOverrides(controller, previousMain);
            }
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
                    ApplyBagStateUpdate(controllerObj, msg);
                    yield break;
                }
            }

            Log.Error($"[BagStateSync] Failed to find controller object with netId {msg.controllerNetId.Value} after 10 retries");
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
                    foreach (var controller in BagPatches.GetAllControllers())
                    {
                        var list = BagPatches.GetState(controller).BaggedObjects;
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

                var list = BagPatches.GetState(controller).BaggedObjects;
                if (list != null)
                {
                    SendBaggedObjectsPersistenceMessage(list, controller);
                }
            }
        }
    }
}
