using RoR2;
namespace DrifterBossGrabMod.Patches
{
    public static class RunLifecyclePatches
    {
        public static void Initialize()
        {
            // Subscribe to run lifecycle events
            Run.onRunStartGlobal += OnRunStartGlobal;
            Run.onRunDestroyGlobal += OnRunDestroyGlobal;
        }
        public static void Cleanup()
        {
            // Unsubscribe from run lifecycle events
            Run.onRunStartGlobal -= OnRunStartGlobal;
            Run.onRunDestroyGlobal -= OnRunDestroyGlobal;
        }
        private static void OnRunStartGlobal(Run run)
        {
            // Initialize persistence system
            PersistenceManager.Initialize();
            // Clear any stale data from previous runs
            PersistenceManager.ClearPersistedObjects();
            PersistenceObjectsTracker.ClearTrackedObjects();
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($" Persistence system initialized on run start");
            }
        }
        private static void OnRunDestroyGlobal(Run run)
        {
            // Cleanup persistence system
            PersistenceManager.Cleanup();
            PersistenceObjectsTracker.ClearTrackedObjects();
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($" Persistence system cleaned up on run destroy");
            }
        }
    }
}