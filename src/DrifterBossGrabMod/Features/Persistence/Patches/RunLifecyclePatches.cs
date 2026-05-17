#nullable enable
using RoR2;
namespace DrifterBossGrabMod.Patches
{
    public static class RunLifecyclePatches
    {
        public static void Initialize()
        {

            Run.onRunStartGlobal += OnRunStartGlobal;
            Run.onRunDestroyGlobal += OnRunDestroyGlobal;
        }
        public static void Cleanup()
        {

            Run.onRunStartGlobal -= OnRunStartGlobal;
            Run.onRunDestroyGlobal -= OnRunDestroyGlobal;
        }
        private static void OnRunStartGlobal(Run run)
        {

            PersistenceManager.Initialize();

            PersistenceManager.ClearPersistedObjects();
            PersistenceObjectsTracker.ClearTrackedObjects();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Persistence system initialized on run start");
            }
        }
        private static void OnRunDestroyGlobal(Run run)
        {

            PersistenceManager.Cleanup();
            PersistenceObjectsTracker.ClearTrackedObjects();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Persistence system cleaned up on run destroy");
            }
        }
    }
}
