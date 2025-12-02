using RoR2;

namespace DrifterBossGrabMod.Patches
{
    public static class TeleporterPatches
    {
        public static void Initialize()
        {
            // Subscribe to teleporter charged event
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterChargedGlobal;
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
    }
}