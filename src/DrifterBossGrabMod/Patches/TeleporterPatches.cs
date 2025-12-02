using RoR2;
using HarmonyLib;

namespace DrifterBossGrabMod.Patches
{
    public static class TeleporterPatches
    {
        public static void Initialize()
        {
            // Subscribe to teleporter charged event
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterChargedGlobal;

            // Patch FixedUpdate to debug NullReferenceException
            var harmony = new Harmony("com.DrifterBossGrab.TeleporterDebug");
            harmony.PatchAll(typeof(TeleporterPatches));
        }

        public static void Cleanup()
        {
            // Unsubscribe from teleporter charged event
            TeleporterInteraction.onTeleporterChargedGlobal -= OnTeleporterChargedGlobal;
        }

        private static void OnTeleporterChargedGlobal(TeleporterInteraction teleporter)
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} OnTeleporterChargedGlobal called - EnableObjectPersistence: {PluginConfig.EnableObjectPersistence.Value}, OnlyPersistCurrentlyBagged: {PluginConfig.OnlyPersistCurrentlyBagged.Value}");
            }

            if (!PluginConfig.EnableObjectPersistence.Value)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Persistence disabled, skipping teleporter persistence logic");
                }
                return;
            }

            // Activate persistence window - thrown objects can now be marked for persistence
            PersistenceManager.ActivatePersistenceWindow();

            // Capture all currently bagged objects when teleporter completes
            PersistenceManager.CaptureCurrentlyBaggedObjects();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Teleporter completed - persistence window activated and bagged objects captured");
            }
        }

        [HarmonyPatch(typeof(TeleporterInteraction), "FixedUpdate")]
        [HarmonyPrefix]
        private static bool FixedUpdatePrefix(TeleporterInteraction __instance)
        {
            // Check if this teleporter should be disabled
            bool shouldDisable = PersistenceManager.ShouldDisableTeleporter(__instance.gameObject);
            if (shouldDisable)
            {
                return false; // Skip original method for persisted teleporters
            }

            return true; // Continue with original method
        }

        [HarmonyPatch(typeof(TeleporterInteraction), "PingTeleporter")]
        [HarmonyPrefix]
        private static bool PingTeleporterPrefix(TeleporterInteraction __instance)
        {
            // Check if this is a persisted teleporter
            bool isPersisted = PersistenceManager.ShouldDisableTeleporter(__instance.gameObject);
            if (isPersisted)
            {
                return false; // Skip pinging persisted teleporters
            }

            return true; // Continue with original method
        }

        [HarmonyPatch(typeof(TeleporterInteraction), "CancelTeleporterPing")]
        [HarmonyPrefix]
        private static bool CancelTeleporterPingPrefix(TeleporterInteraction __instance)
        {
            // Check if this is a persisted teleporter
            bool isPersisted = PersistenceManager.ShouldDisableTeleporter(__instance.gameObject);
            if (isPersisted)
            {
                return false; // Skip canceling ping for persisted teleporters
            }

            return true; // Continue with original method
        }
    }
}