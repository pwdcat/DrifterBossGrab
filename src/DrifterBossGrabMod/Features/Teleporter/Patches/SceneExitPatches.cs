#nullable enable
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
namespace DrifterBossGrabMod.Patches
{
    public static class SceneExitPatches
    {
        private static bool _hasCapturedForScene = false;

        // Reset the capture flag when a new scene loads so we are ready for the next transition
        public static void ResetCaptureFlag()
        {
            _hasCapturedForScene = false;
        }

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
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[SceneExitPatches] SceneExitController.onBeginExit called. Executing persistence capture.");
            }
            ExecutePersistenceCapture();
        }

        private static void ExecutePersistenceCapture()
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value)
            {
                return;
            }

            if (_hasCapturedForScene)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[SceneExitPatches] Persistence capture already executed for this scene transition. Skipping.");
                }
                return;
            }

            _hasCapturedForScene = true;

            // Get currently bagged objects
            var baggedObjects = PersistenceManager.GetCurrentlyBaggedObjects();

            // Send persistence message to all clients (only from server to avoid duplicates)
            if (NetworkServer.active)
            {
                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(baggedObjects);
            }

            // Capture currently bagged objects before scene transition
            PersistenceManager.CaptureCurrentlyBaggedObjects();

            // Move objects to persistence container
            PersistenceManager.MoveObjectsToPersistenceContainer();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Captured {baggedObjects.Count} bagged objects on scene exit{(NetworkServer.active ? " and sent persistence message" : "")}");
            }
        }
    }
}
