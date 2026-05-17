#nullable enable
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    // Handles config synchronization from host to clients.
    public static class ConfigSyncHandler
    {
        private static volatile bool _isBroadcastPending = false;

        // Sends config to a specific client connection.
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
                SearchRadiusMultiplier = PluginConfig.Instance.SearchRadiusMultiplier.Value,
                ComponentChooserSortMode = PluginConfig.Instance.ComponentChooserSortModeEntry.Value,

                // Skill Scalars
                BreakoutTimeMultiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value,
                MaxSmacks = PluginConfig.Instance.MaxSmacks.Value,
                MaxLaunchSpeed = PluginConfig.Instance.MaxLaunchSpeed.Value,

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
                AutoGrabDelay = PluginConfig.Instance.AutoGrabDelay.Value,

                // Bottomless Bag
                BottomlessBagEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value,
                AddedCapacity = PluginConfig.Instance.AddedCapacity.Value,
                EnableStockRefreshClamping = PluginConfig.Instance.EnableStockRefreshClamping.Value,
                EnableSuccessiveGrabStockRefresh = PluginConfig.Instance.EnableSuccessiveGrabStockRefresh.Value,
                CycleCooldown = PluginConfig.Instance.CycleCooldown.Value,

                // Balance
                EnableBalance = PluginConfig.Instance.EnableBalance.Value,
                AoEDamageDistribution = PluginConfig.Instance.AoEDamageDistribution.Value,
                BagScaleCap = PluginConfig.Instance.BagScaleCap.Value,
                MassCap = PluginConfig.Instance.MassCap.Value,
                StateCalculationMode = PluginConfig.Instance.StateCalculationMode.Value,
                OverencumbranceMax = PluginConfig.Instance.OverencumbranceMax.Value,
                SlotScalingFormula = PluginConfig.Instance.SlotScalingFormula.Value,
                MassCapacityFormula = PluginConfig.Instance.MassCapacityFormula.Value,
                MovespeedPenaltyFormula = PluginConfig.Instance.MovespeedPenaltyFormula.Value,

                // Balance - Flag Multipliers
                EliteFlagMultiplier = PluginConfig.Instance.EliteFlagMultiplier.Value,
                BossFlagMultiplier = PluginConfig.Instance.BossFlagMultiplier.Value,
                ChampionFlagMultiplier = PluginConfig.Instance.ChampionFlagMultiplier.Value,
                PlayerFlagMultiplier = PluginConfig.Instance.PlayerFlagMultiplier.Value,
                MinionFlagMultiplier = PluginConfig.Instance.MinionFlagMultiplier.Value,
                DroneFlagMultiplier = PluginConfig.Instance.DroneFlagMultiplier.Value,
                MechanicalFlagMultiplier = PluginConfig.Instance.MechanicalFlagMultiplier.Value,
                VoidFlagMultiplier = PluginConfig.Instance.VoidFlagMultiplier.Value,
                AllFlagMultiplier = PluginConfig.Instance.AllFlagMultiplier.Value,
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Sending config to client {conn.connectionId} (general, bottomlessbag, persistence, balance)");
            }

            conn.Send(Constants.Network.SyncConfigMessageType, msg);
        }

        // Broadcasts config to all connected clients.
        public static void BroadcastConfigToClients()
        {
            if (!NetworkServer.active) return;

            if (_isBroadcastPending) return;

            _isBroadcastPending = true;
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(DelayBroadcast());
            }
        }

        // Handles config sync message from host (Server -> Client).
        [NetworkMessageHandler(msgType = Constants.Network.SyncConfigMessageType, client = true, server = false)]
        public static void HandleSyncConfigMessage(NetworkMessage netMsg)
        {
            if (NetworkServer.active) return;

            if (!PluginConfig.Instance.EnableConfigSync.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[ConfigSyncHandler] Config sync disabled by client setting. Ignoring config from host.");
                }
                return;
            }

            var msg = netMsg.ReadMessage<SyncConfigMessage>();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Received config from host (general, bottomlessbag, persistence, balance).");
            }

            // Apply all config values from the message
            ApplySyncedConfig(msg);
        }

        // Applies the synced config values from the host message.
        private static void ApplySyncedConfig(SyncConfigMessage msg)
        {
            // General
            PluginConfig.Instance.EnableBossGrabbing.Value = msg.EnableBossGrabbing;
            PluginConfig.Instance.EnableNPCGrabbing.Value = msg.EnableNPCGrabbing;
            PluginConfig.Instance.EnableEnvironmentGrabbing.Value = msg.EnableEnvironmentGrabbing;
            PluginConfig.Instance.EnableLockedObjectGrabbing.Value = msg.EnableLockedObjectGrabbing;
            PluginConfig.Instance.ProjectileGrabbingMode.Value = msg.ProjectileGrabbingMode;
            PluginConfig.Instance.SearchRadiusMultiplier.Value = msg.SearchRadiusMultiplier;
            PluginConfig.Instance.ComponentChooserSortModeEntry.Value = msg.ComponentChooserSortMode;

            // Skill Scalars
            PluginConfig.Instance.BreakoutTimeMultiplier.Value = msg.BreakoutTimeMultiplier;
            PluginConfig.Instance.MaxSmacks.Value = msg.MaxSmacks;
            PluginConfig.Instance.MaxLaunchSpeed.Value = msg.MaxLaunchSpeed;

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
            PluginConfig.Instance.AutoGrabDelay.Value = msg.AutoGrabDelay;

            // Bottomless Bag
            PluginConfig.Instance.BottomlessBagEnabled.Value = msg.BottomlessBagEnabled;
            PluginConfig.Instance.AddedCapacity.Value = msg.AddedCapacity;
            PluginConfig.Instance.EnableStockRefreshClamping.Value = msg.EnableStockRefreshClamping;
            PluginConfig.Instance.EnableSuccessiveGrabStockRefresh.Value = msg.EnableSuccessiveGrabStockRefresh;
            PluginConfig.Instance.CycleCooldown.Value = msg.CycleCooldown;

            // Balance
            PluginConfig.Instance.EnableBalance.Value = msg.EnableBalance;
            PluginConfig.Instance.AoEDamageDistribution.Value = msg.AoEDamageDistribution;
            PluginConfig.Instance.BagScaleCap.Value = msg.BagScaleCap;
            PluginConfig.Instance.MassCap.Value = msg.MassCap;
            PluginConfig.Instance.StateCalculationMode.Value = msg.StateCalculationMode;
            PluginConfig.Instance.OverencumbranceMax.Value = msg.OverencumbranceMax;
            PluginConfig.Instance.SlotScalingFormula.Value = msg.SlotScalingFormula;
            PluginConfig.Instance.MassCapacityFormula.Value = msg.MassCapacityFormula;
            PluginConfig.Instance.MovespeedPenaltyFormula.Value = msg.MovespeedPenaltyFormula;

            // Balance - Flag Multipliers
            PluginConfig.Instance.EliteFlagMultiplier.Value = msg.EliteFlagMultiplier;
            PluginConfig.Instance.BossFlagMultiplier.Value = msg.BossFlagMultiplier;
            PluginConfig.Instance.ChampionFlagMultiplier.Value = msg.ChampionFlagMultiplier;
            PluginConfig.Instance.PlayerFlagMultiplier.Value = msg.PlayerFlagMultiplier;
            PluginConfig.Instance.MinionFlagMultiplier.Value = msg.MinionFlagMultiplier;
            PluginConfig.Instance.DroneFlagMultiplier.Value = msg.DroneFlagMultiplier;
            PluginConfig.Instance.MechanicalFlagMultiplier.Value = msg.MechanicalFlagMultiplier;
            PluginConfig.Instance.VoidFlagMultiplier.Value = msg.VoidFlagMultiplier;
            PluginConfig.Instance.AllFlagMultiplier.Value = msg.AllFlagMultiplier;

            // Invalidate caches to ensure new blacklist/component type values are used
            PluginConfig.InvalidateAllCaches();

            // Trigger re-scan of grabbable objects to apply new settings to the current scene
            GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[ConfigSyncHandler] Local config updated and scene objects re-scanned.");
            }
        }

        // Coroutine to delay config broadcast until the end of the frame,
        // coalescing multiple config changes into a single broadcast.
        private static System.Collections.IEnumerator DelayBroadcast()
        {
            yield return new WaitForEndOfFrame();
            _isBroadcastPending = false;

            if (!NetworkServer.active) yield break;

            if (!PluginConfig.Instance.EnableConfigSync.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ConfigSyncHandler] Sync disabled by host config. Skipping broadcast.");
                }
                yield break;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ConfigSyncHandler] Broadcasting updated config to all connected clients.");
            }

            foreach (var conn in NetworkServer.connections)
            {
                if (conn == null || !conn.isReady) continue;
                SendConfigToClient(conn);
            }
        }

    }
}
