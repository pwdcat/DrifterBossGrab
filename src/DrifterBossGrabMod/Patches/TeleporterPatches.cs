using RoR2;
using HarmonyLib;

namespace DrifterBossGrabMod.Patches
{
    public static class TeleporterPatches
    {
        public static void Initialize()
        {
            // No initialization needed for teleporter disabling logic
        }

        public static void Cleanup()
        {
            // No cleanup needed for teleporter disabling logic
        }


        [HarmonyPatch(typeof(TeleporterInteraction), "FixedUpdate")]
        [HarmonyPrefix]
        private static bool FixedUpdatePrefix(TeleporterInteraction __instance)
        {
            // Check if this teleporter should be disabled
            bool shouldDisable = PersistenceManager.ShouldDisableTeleporter(__instance.gameObject);
            if (shouldDisable)
            {
                // Skip original method for persisted teleporters
                return false;
            }

            return true; // Continue with original method
        }

        [HarmonyPatch(typeof(TeleporterInteraction), "PingTeleporter")]
        [HarmonyPrefix]
        private static bool PingTeleporterPrefix(TeleporterInteraction __instance)
        {
            // Check if this teleporter should be disabled
            bool shouldDisable = PersistenceManager.ShouldDisableTeleporter(__instance.gameObject);
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} PingTeleporterPrefix for {__instance.gameObject.name}, shouldDisable: {shouldDisable}");
            }
            if (shouldDisable)
            {
                return false; // Skip pinging disabled teleporters
            }

            return true; // Continue with original method
        }

        [HarmonyPatch(typeof(TeleporterInteraction), "CancelTeleporterPing")]
        [HarmonyPrefix]
        private static bool CancelTeleporterPingPrefix(TeleporterInteraction __instance)
        {
            // Check if this teleporter should be disabled
            bool shouldDisable = PersistenceManager.ShouldDisableTeleporter(__instance.gameObject);
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} CancelTeleporterPingPrefix for {__instance.gameObject.name}, shouldDisable: {shouldDisable}");
            }
            if (shouldDisable)
            {
                return false; // Skip canceling ping for disabled teleporters
            }

            return true; // Continue with original method
        }
    }
}