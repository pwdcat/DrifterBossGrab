using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    public static class ConfigSyncHandler
    {
        private const short MSG_SYNC_CONFIG = 208; // New message ID

        public static void Init()
        {
            // Register server handler if active (none for now as this is Server->Client)
            
            // Register client handler manually to be safe
            NetworkManagerSystem.onClientConnectGlobal += RegisterClientHandler;
        }

        private static void RegisterClientHandler(NetworkConnection conn)
        {
            if (NetworkManager.singleton == null || NetworkManager.singleton.client == null)
            {
                return;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Registering client handler for MSG_SYNC_CONFIG ({MSG_SYNC_CONFIG})");
            }
            NetworkManager.singleton.client.RegisterHandler(MSG_SYNC_CONFIG, OnClientReceiveConfig);
        }

        // Keep attribute just in case, but make public
        [NetworkMessageHandler(msgType = MSG_SYNC_CONFIG, client = true)]
        public static void OnClientReceiveConfig(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<SyncConfigMessage>();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Received full config from host.");
            }

            // General
            PluginConfig.Instance.EnableBossGrabbing.Value = msg.EnableBossGrabbing;
            PluginConfig.Instance.EnableNPCGrabbing.Value = msg.EnableNPCGrabbing;
            PluginConfig.Instance.EnableEnvironmentGrabbing.Value = msg.EnableEnvironmentGrabbing;
            PluginConfig.Instance.EnableLockedObjectGrabbing.Value = msg.EnableLockedObjectGrabbing;
            PluginConfig.Instance.EnableProjectileGrabbing.Value = msg.EnableProjectileGrabbing;
            PluginConfig.Instance.ProjectileGrabbingSurvivorOnly.Value = msg.ProjectileGrabbingSurvivorOnly;

            // Blacklists & Component Types
            PluginConfig.Instance.BodyBlacklist.Value = msg.BodyBlacklist;
            PluginConfig.Instance.RecoveryObjectBlacklist.Value = msg.RecoveryObjectBlacklist;
            PluginConfig.Instance.GrabbableComponentTypes.Value = msg.GrabbableComponentTypes; 
            PluginConfig.Instance.GrabbableKeywordBlacklist.Value = msg.GrabbableKeywordBlacklist;

            // Persistence
            PluginConfig.Instance.EnableObjectPersistence.Value = msg.EnableObjectPersistence;
            PluginConfig.Instance.EnableAutoGrab.Value = msg.EnableAutoGrab;
            PluginConfig.Instance.PersistBaggedBosses.Value = msg.PersistBaggedBosses;
            PluginConfig.Instance.PersistBaggedNPCs.Value = msg.PersistBaggedNPCs;
            PluginConfig.Instance.PersistBaggedEnvironmentObjects.Value = msg.PersistBaggedEnvironmentObjects;
            PluginConfig.Instance.PersistenceBlacklist.Value = msg.PersistenceBlacklist;

            // Trigger re-scan of grabbable objects to apply new settings to the current scene
            // This is crucial because objects might have already spawned with the old config
            GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[ConfigSyncHandler] Local config updated and scene objects re-scanned.");
            }
        }

        public static void SendConfigToClient(NetworkConnection conn)
        {
            if (!NetworkServer.active) return;
            
            if (!PluginConfig.Instance.EnableConfigSync.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ConfigSyncHandler] Sync disabled by host config. Skipping send to client {conn.connectionId}.");
                }
                return;
            }

            var msg = new SyncConfigMessage
            {
                // General
                EnableBossGrabbing = PluginConfig.Instance.EnableBossGrabbing.Value,
                EnableNPCGrabbing = PluginConfig.Instance.EnableNPCGrabbing.Value,
                EnableEnvironmentGrabbing = PluginConfig.Instance.EnableEnvironmentGrabbing.Value,
                EnableLockedObjectGrabbing = PluginConfig.Instance.EnableLockedObjectGrabbing.Value,
                EnableProjectileGrabbing = PluginConfig.Instance.EnableProjectileGrabbing.Value,
                ProjectileGrabbingSurvivorOnly = PluginConfig.Instance.ProjectileGrabbingSurvivorOnly.Value,

                // Blacklists & Component Types
                BodyBlacklist = PluginConfig.Instance.BodyBlacklist.Value,
                RecoveryObjectBlacklist = PluginConfig.Instance.RecoveryObjectBlacklist.Value,
                GrabbableComponentTypes = PluginConfig.Instance.GrabbableComponentTypes.Value,
                GrabbableKeywordBlacklist = PluginConfig.Instance.GrabbableKeywordBlacklist.Value,

                // Persistence
                EnableObjectPersistence = PluginConfig.Instance.EnableObjectPersistence.Value,
                EnableAutoGrab = PluginConfig.Instance.EnableAutoGrab.Value,
                PersistBaggedBosses = PluginConfig.Instance.PersistBaggedBosses.Value,
                PersistBaggedNPCs = PluginConfig.Instance.PersistBaggedNPCs.Value,
                PersistBaggedEnvironmentObjects = PluginConfig.Instance.PersistBaggedEnvironmentObjects.Value,
                PersistenceBlacklist = PluginConfig.Instance.PersistenceBlacklist.Value,
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Sending full config to client {conn.connectionId}");
            }

            conn.Send(MSG_SYNC_CONFIG, msg);
        }
    }
}
