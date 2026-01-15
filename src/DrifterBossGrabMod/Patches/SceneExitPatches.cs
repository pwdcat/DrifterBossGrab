using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
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
            // Get currently bagged objects
            var baggedObjects = PersistenceManager.GetCurrentlyBaggedObjects();
            // Send persistence message to all clients (only from server to avoid duplicates)
            if (NetworkServer.active)
            {
                PersistenceManager.SendBaggedObjectsPersistenceMessage(baggedObjects);
            }
            // Capture currently bagged objects before scene transition
            PersistenceManager.CaptureCurrentlyBaggedObjects();
            // Move objects to persistence container
            PersistenceManager.MoveObjectsToPersistenceContainer();
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($" Captured bagged objects on scene exit{(NetworkServer.active ? " and sent persistence message" : "")}");
            }
        }
    }
}