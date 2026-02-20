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
                Log.Info($"[ConfigSyncHandler] Received config from host (general, bottomlessbag, persistence, balance).");
            }

            // General
            PluginConfig.Instance.EnableBossGrabbing.Value = msg.EnableBossGrabbing;
            PluginConfig.Instance.EnableNPCGrabbing.Value = msg.EnableNPCGrabbing;
            PluginConfig.Instance.EnableEnvironmentGrabbing.Value = msg.EnableEnvironmentGrabbing;
            PluginConfig.Instance.EnableLockedObjectGrabbing.Value = msg.EnableLockedObjectGrabbing;
            PluginConfig.Instance.ProjectileGrabbingMode.Value = msg.ProjectileGrabbingMode;

            // Bottomless Bag
            PluginConfig.Instance.BottomlessBagEnabled.Value = msg.BottomlessBagEnabled;
            PluginConfig.Instance.BottomlessBagBaseCapacity.Value = msg.BottomlessBagBaseCapacity;

            // Persistence
            PluginConfig.Instance.EnableObjectPersistence.Value = msg.EnableObjectPersistence;
            PluginConfig.Instance.EnableAutoGrab.Value = msg.EnableAutoGrab;
            PluginConfig.Instance.PersistBaggedBosses.Value = msg.PersistBaggedBosses;
            PluginConfig.Instance.PersistBaggedNPCs.Value = msg.PersistBaggedNPCs;
            PluginConfig.Instance.PersistBaggedEnvironmentObjects.Value = msg.PersistBaggedEnvironmentObjects;
            PluginConfig.Instance.AutoGrabDelay.Value = msg.AutoGrabDelay;

            // Balance - All toggle fields
            PluginConfig.Instance.EnableBalance.Value = msg.EnableBalance;
            PluginConfig.Instance.EnableAoESlamDamage.Value = msg.EnableAoESlamDamage;
            PluginConfig.Instance.EnableOverencumbrance.Value = msg.EnableOverencumbrance;
            PluginConfig.Instance.UncapCapacity.Value = msg.UncapCapacity;
            PluginConfig.Instance.ToggleMassCapacity.Value = msg.ToggleMassCapacity;
            PluginConfig.Instance.StateCalculationModeEnabled.Value = msg.StateCalculationModeEnabled;
            PluginConfig.Instance.UncapBagScale.Value = msg.UncapBagScale;
            PluginConfig.Instance.UncapMass.Value = msg.UncapMass;

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
                ProjectileGrabbingMode = PluginConfig.Instance.ProjectileGrabbingMode.Value,

                // Bottomless Bag
                BottomlessBagEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value,
                BottomlessBagBaseCapacity = PluginConfig.Instance.BottomlessBagBaseCapacity.Value,

                // Persistence
                EnableObjectPersistence = PluginConfig.Instance.EnableObjectPersistence.Value,
                EnableAutoGrab = PluginConfig.Instance.EnableAutoGrab.Value,
                PersistBaggedBosses = PluginConfig.Instance.PersistBaggedBosses.Value,
                PersistBaggedNPCs = PluginConfig.Instance.PersistBaggedNPCs.Value,
                PersistBaggedEnvironmentObjects = PluginConfig.Instance.PersistBaggedEnvironmentObjects.Value,
                AutoGrabDelay = PluginConfig.Instance.AutoGrabDelay.Value,

                // Balance
                EnableBalance = PluginConfig.Instance.EnableBalance.Value,
                EnableAoESlamDamage = PluginConfig.Instance.EnableAoESlamDamage.Value,
                EnableOverencumbrance = PluginConfig.Instance.EnableOverencumbrance.Value,
                UncapCapacity = PluginConfig.Instance.UncapCapacity.Value,
                ToggleMassCapacity = PluginConfig.Instance.ToggleMassCapacity.Value,
                StateCalculationModeEnabled = PluginConfig.Instance.StateCalculationModeEnabled.Value,
                UncapBagScale = PluginConfig.Instance.UncapBagScale.Value,
                UncapMass = PluginConfig.Instance.UncapMass.Value,
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Sending config to client {conn.connectionId} (general, bottomlessbag, persistence, balance)");
            }

            conn.Send(MSG_SYNC_CONFIG, msg);
        }
    }
}
