#nullable enable
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{

    public static class ConfigSyncHandler
    {
        private static volatile bool _isBroadcastPending = false;

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

                EnableBossGrabbing = PluginConfig.Instance.EnableBossGrabbing.Value,
                EnableNPCGrabbing = PluginConfig.Instance.EnableNPCGrabbing.Value,
                EnableEnvironmentGrabbing = PluginConfig.Instance.EnableEnvironmentGrabbing.Value,
                EnableLockedObjectGrabbing = PluginConfig.Instance.EnableLockedObjectGrabbing.Value,
                ProjectileGrabbingMode = PluginConfig.Instance.ProjectileGrabbingMode.Value,
                SearchRadiusMultiplier = PluginConfig.Instance.SearchRadiusMultiplier.Value,
                ComponentChooserSortMode = PluginConfig.Instance.ComponentChooserSortModeEntry.Value,

                BreakoutTimeMultiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value,
                MaxSmacks = PluginConfig.Instance.MaxSmacks.Value,
                MaxLaunchSpeed = PluginConfig.Instance.MaxLaunchSpeed.Value,

                BodyBlacklist = PluginConfig.Instance.BodyBlacklist.Value,
                RecoveryObjectBlacklist = PluginConfig.Instance.RecoveryObjectBlacklist.Value,
                GrabbableComponentTypes = PluginConfig.Instance.GrabbableComponentTypes.Value,
                GrabbableKeywordBlacklist = PluginConfig.Instance.GrabbableKeywordBlacklist.Value,

                EnableObjectPersistence = PluginConfig.Instance.EnableObjectPersistence.Value,
                EnableAutoGrab = PluginConfig.Instance.EnableAutoGrab.Value,
                PersistBaggedBosses = PluginConfig.Instance.PersistBaggedBosses.Value,
                PersistBaggedNPCs = PluginConfig.Instance.PersistBaggedNPCs.Value,
                PersistBaggedEnvironmentObjects = PluginConfig.Instance.PersistBaggedEnvironmentObjects.Value,
                PersistenceBlacklist = PluginConfig.Instance.PersistenceBlacklist.Value,
                AutoGrabDelay = PluginConfig.Instance.AutoGrabDelay.Value,

                BottomlessBagEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value,
                EnableStockRefreshClamping = PluginConfig.Instance.EnableStockRefreshClamping.Value,
                EnableSuccessiveGrabStockRefresh = PluginConfig.Instance.EnableSuccessiveGrabStockRefresh.Value,
                CycleCooldown = PluginConfig.Instance.CycleCooldown.Value,

                EnableBalance = PluginConfig.Instance.EnableBalance.Value,
                AoEDamageDistribution = PluginConfig.Instance.AoEDamageDistribution.Value,
                BagScaleCap = PluginConfig.Instance.BagScaleCap.Value,
                MassCap = PluginConfig.Instance.MassCap.Value,
                StateCalculationMode = PluginConfig.Instance.StateCalculationMode.Value,
                OverencumbranceMax = PluginConfig.Instance.OverencumbranceMax.Value,
                SlotScalingFormula = PluginConfig.Instance.SlotScalingFormula.Value,
                MassCapacityFormula = PluginConfig.Instance.MassCapacityFormula.Value,
                MovespeedPenaltyFormula = PluginConfig.Instance.MovespeedPenaltyFormula.Value,

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

            ApplySyncedConfig(msg);
        }

        private static void ApplySyncedConfig(SyncConfigMessage msg)
        {

            PluginConfig.Instance.EnableBossGrabbing.Value = msg.EnableBossGrabbing;
            PluginConfig.Instance.EnableNPCGrabbing.Value = msg.EnableNPCGrabbing;
            PluginConfig.Instance.EnableEnvironmentGrabbing.Value = msg.EnableEnvironmentGrabbing;
            PluginConfig.Instance.EnableLockedObjectGrabbing.Value = msg.EnableLockedObjectGrabbing;
            PluginConfig.Instance.ProjectileGrabbingMode.Value = msg.ProjectileGrabbingMode;
            PluginConfig.Instance.SearchRadiusMultiplier.Value = msg.SearchRadiusMultiplier;
            PluginConfig.Instance.ComponentChooserSortModeEntry.Value = msg.ComponentChooserSortMode;

            PluginConfig.Instance.BreakoutTimeMultiplier.Value = msg.BreakoutTimeMultiplier;
            PluginConfig.Instance.MaxSmacks.Value = msg.MaxSmacks;
            PluginConfig.Instance.MaxLaunchSpeed.Value = msg.MaxLaunchSpeed;

            PluginConfig.Instance.BodyBlacklist.Value = msg.BodyBlacklist;
            PluginConfig.Instance.RecoveryObjectBlacklist.Value = msg.RecoveryObjectBlacklist;
            PluginConfig.Instance.GrabbableComponentTypes.Value = msg.GrabbableComponentTypes;
            PluginConfig.Instance.GrabbableKeywordBlacklist.Value = msg.GrabbableKeywordBlacklist;

            PluginConfig.Instance.EnableObjectPersistence.Value = msg.EnableObjectPersistence;
            PluginConfig.Instance.EnableAutoGrab.Value = msg.EnableAutoGrab;
            PluginConfig.Instance.PersistBaggedBosses.Value = msg.PersistBaggedBosses;
            PluginConfig.Instance.PersistBaggedNPCs.Value = msg.PersistBaggedNPCs;
            PluginConfig.Instance.PersistBaggedEnvironmentObjects.Value = msg.PersistBaggedEnvironmentObjects;
            PluginConfig.Instance.PersistenceBlacklist.Value = msg.PersistenceBlacklist;
            PluginConfig.Instance.AutoGrabDelay.Value = msg.AutoGrabDelay;

            PluginConfig.Instance.BottomlessBagEnabled.Value = msg.BottomlessBagEnabled;
            PluginConfig.Instance.EnableStockRefreshClamping.Value = msg.EnableStockRefreshClamping;
            PluginConfig.Instance.EnableSuccessiveGrabStockRefresh.Value = msg.EnableSuccessiveGrabStockRefresh;
            PluginConfig.Instance.CycleCooldown.Value = msg.CycleCooldown;

            PluginConfig.Instance.EnableBalance.Value = msg.EnableBalance;
            PluginConfig.Instance.AoEDamageDistribution.Value = msg.AoEDamageDistribution;
            PluginConfig.Instance.BagScaleCap.Value = msg.BagScaleCap;
            PluginConfig.Instance.MassCap.Value = msg.MassCap;
            PluginConfig.Instance.StateCalculationMode.Value = msg.StateCalculationMode;
            PluginConfig.Instance.OverencumbranceMax.Value = msg.OverencumbranceMax;
            PluginConfig.Instance.SlotScalingFormula.Value = msg.SlotScalingFormula;
            PluginConfig.Instance.MassCapacityFormula.Value = msg.MassCapacityFormula;
            PluginConfig.Instance.MovespeedPenaltyFormula.Value = msg.MovespeedPenaltyFormula;

            PluginConfig.Instance.EliteFlagMultiplier.Value = msg.EliteFlagMultiplier;
            PluginConfig.Instance.BossFlagMultiplier.Value = msg.BossFlagMultiplier;
            PluginConfig.Instance.ChampionFlagMultiplier.Value = msg.ChampionFlagMultiplier;
            PluginConfig.Instance.PlayerFlagMultiplier.Value = msg.PlayerFlagMultiplier;
            PluginConfig.Instance.MinionFlagMultiplier.Value = msg.MinionFlagMultiplier;
            PluginConfig.Instance.DroneFlagMultiplier.Value = msg.DroneFlagMultiplier;
            PluginConfig.Instance.MechanicalFlagMultiplier.Value = msg.MechanicalFlagMultiplier;
            PluginConfig.Instance.VoidFlagMultiplier.Value = msg.VoidFlagMultiplier;
            PluginConfig.Instance.AllFlagMultiplier.Value = msg.AllFlagMultiplier;

            PluginConfig.InvalidateAllCaches();

            GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[ConfigSyncHandler] Local config updated and scene objects re-scanned.");
            }
        }

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
