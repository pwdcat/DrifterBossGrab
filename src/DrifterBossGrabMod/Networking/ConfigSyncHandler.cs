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
                Log.Debug($"[ConfigSyncHandler] Sync disabled by host config. Skipping send to client {conn.connectionId}.");
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

                EnableRecoveryFeature = PluginConfig.Instance.EnableRecoveryFeature.Value,
                EnemyRecoveryMode = PluginConfig.Instance.EnemyRecoveryMode.Value,
                RecoverBaggedBosses = PluginConfig.Instance.RecoverBaggedBosses.Value,
                RecoverBaggedNPCs = PluginConfig.Instance.RecoverBaggedNPCs.Value,
                RecoverBaggedEnvironmentObjects = PluginConfig.Instance.RecoverBaggedEnvironmentObjects.Value,

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

            Log.Debug($"[ConfigSyncHandler] Sending config to client {conn.connectionId} (general, bottomlessbag, persistence, balance, recovery)");

            NetworkMessageRegistry.SendToClient(conn, Constants.Network.SyncConfigSubMessageType, msg);
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

        public class RequestConfigMessage : MessageBase
        {
            public override void Serialize(NetworkWriter writer) { }
            public override void Deserialize(NetworkReader reader) { }
        }

        public static void RegisterMessages()
        {
            if (PluginConfig.Instance == null || !PluginConfig.Instance.EnableConfigSync.Value)
            {
                Log.Debug("[ConfigSyncHandler] EnableConfigSync is disabled. Sub-handlers will not be registered.");
                UnregisterMessages();
                return;
            }

            NetworkMessageRegistry.RegisterClientSubHandler(Constants.Network.SyncConfigSubMessageType, HandleSyncConfigMessage);
            NetworkMessageRegistry.RegisterServerSubHandler(Constants.Network.RequestConfigSubMessageType, HandleRequestConfigMessage);

            NetworkUser.onNetworkUserDiscovered -= OnNetworkUserDiscovered;
            NetworkUser.onNetworkUserDiscovered += OnNetworkUserDiscovered;

            Stage.onStageStartGlobal -= OnStageStartClient;
            Stage.onStageStartGlobal += OnStageStartClient;

            Log.Debug("[ConfigSyncHandler] SyncConfig client/server sub-handlers and join hooks registered.");
        }

        public static void UnregisterMessages()
        {
            NetworkMessageRegistry.UnregisterClientSubHandler(Constants.Network.SyncConfigSubMessageType);
            NetworkMessageRegistry.UnregisterServerSubHandler(Constants.Network.RequestConfigSubMessageType);

            NetworkUser.onNetworkUserDiscovered -= OnNetworkUserDiscovered;
            Stage.onStageStartGlobal -= OnStageStartClient;

            Log.Debug("[ConfigSyncHandler] SyncConfig client/server sub-handlers and join hooks unregistered.");
        }

        public static void UpdateRegistration()
        {
            RegisterMessages();
        }

        public static void RequestConfigFromServer()
        {
            if (NetworkServer.active || !NetworkClient.active) return;

            if (PluginConfig.Instance == null || !PluginConfig.Instance.EnableConfigSync.Value) return;

            var client = NetworkManager.singleton?.client;
            if (client != null && client.isConnected)
            {
                Log.Debug("[ConfigSyncHandler] Requesting current config from host...");
                NetworkMessageRegistry.SendToServer(Constants.Network.RequestConfigSubMessageType, new RequestConfigMessage());
            }
        }

        public static void HandleRequestConfigMessage(NetworkReader reader, NetworkConnection conn)
        {
            if (!NetworkServer.active) return;

            if (!PluginConfig.Instance.EnableConfigSync.Value)
            {
                Log.Debug($"[ConfigSyncHandler] Received RequestConfig from client {conn.connectionId}, but config sync is disabled on host.");
                return;
            }

            Log.Debug($"[ConfigSyncHandler] Received RequestConfig from client {conn.connectionId}. Sending active config...");
            SendConfigToClient(conn);
        }

        private static void OnNetworkUserDiscovered(NetworkUser user)
        {
            if (!NetworkServer.active || !PluginConfig.Instance.EnableConfigSync.Value) return;

            if (user != null && DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(DelayedSendConfigToUser(user));
            }
        }

        private static System.Collections.IEnumerator DelayedSendConfigToUser(NetworkUser user)
        {
            float elapsed = 0f;
            const float timeout = 5.0f;

            while (elapsed < timeout)
            {
                if (user == null) yield break;

                var conn = user.connectionToClient;
                if (conn != null && conn.isReady)
                {
                    Log.Debug($"[ConfigSyncHandler] Discovered new player '{user.userName}' (connId: {conn.connectionId}). Pushing config...");
                    SendConfigToClient(conn);
                    yield break;
                }

                yield return new WaitForSeconds(0.3f);
                elapsed += 0.3f;
            }
        }

        private static void OnStageStartClient(Stage stage)
        {
            if (!NetworkServer.active && NetworkClient.active)
            {
                RequestConfigFromServer();
            }
        }

        public static void HandleSyncConfigMessage(NetworkReader reader, NetworkConnection conn)
        {
            if (NetworkServer.active) return;

            if (!PluginConfig.Instance.EnableConfigSync.Value)
            {
                Log.Debug("[ConfigSyncHandler] Config sync disabled by client setting. Ignoring config from host.");
                return;
            }

            var msg = new SyncConfigMessage();
            msg.Deserialize(reader);

            Log.Debug($"[ConfigSyncHandler] Received config from host (general, bottomlessbag, persistence, balance, recovery).");

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

            PluginConfig.Instance.EnableRecoveryFeature.Value = msg.EnableRecoveryFeature;
            PluginConfig.Instance.EnemyRecoveryMode.Value = msg.EnemyRecoveryMode;
            PluginConfig.Instance.RecoverBaggedBosses.Value = msg.RecoverBaggedBosses;
            PluginConfig.Instance.RecoverBaggedNPCs.Value = msg.RecoverBaggedNPCs;
            PluginConfig.Instance.RecoverBaggedEnvironmentObjects.Value = msg.RecoverBaggedEnvironmentObjects;

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

            Log.Debug("[ConfigSyncHandler] Local config updated and scene objects re-scanned.");
        }

        private static System.Collections.IEnumerator DelayBroadcast()
        {
            yield return new WaitForEndOfFrame();
            _isBroadcastPending = false;

            if (!NetworkServer.active) yield break;

            if (!PluginConfig.Instance.EnableConfigSync.Value)
            {
                Log.Debug($"[ConfigSyncHandler] Sync disabled by host config. Skipping broadcast.");
                yield break;
            }

            Log.Debug($"[ConfigSyncHandler] Broadcasting updated config to all connected clients.");

            foreach (var conn in NetworkServer.connections)
            {
                if (conn == null || !conn.isReady) continue;
                SendConfigToClient(conn);
            }
        }

    }
}
