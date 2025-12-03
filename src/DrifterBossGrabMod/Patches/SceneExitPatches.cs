using HarmonyLib;
using RoR2;

namespace DrifterBossGrabMod.Patches
{
    public static class SceneExitPatches
    {
        [HarmonyPatch(typeof(SceneExitController), "OnEnable")]
        public class SceneExitController_OnEnable
        {
            [HarmonyPostfix]
            public static void Postfix(SceneExitController __instance)
            {
                // Subscribe to the begin exit event
                SceneExitController.onBeginExit += OnBeginExit;
            }
        }

        [HarmonyPatch(typeof(SceneExitController), "OnDisable")]
        public class SceneExitController_OnDisable
        {
            [HarmonyPostfix]
            public static void Postfix(SceneExitController __instance)
            {
                SceneExitController.onBeginExit -= OnBeginExit;
            }
        }

        private static void OnBeginExit(SceneExitController exitController)
        {
            if (!PluginConfig.EnableObjectPersistence.Value)
            {
                return;
            }

            // Capture currently bagged objects before scene transition
            PersistenceManager.CaptureCurrentlyBaggedObjects();

            // Move objects to persistence container
            PersistenceManager.MoveObjectsToPersistenceContainer();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Captured bagged objects on scene exit");
            }
        }
    }
}