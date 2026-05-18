#nullable enable
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
namespace DrifterBossGrabMod.Patches
{
    public static class SceneExitPatches
    {
        private static bool _hasCapturedForScene = false;

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
            Log.Debug($"[SceneExitPatches] SceneExitController.onBeginExit called. Executing persistence capture.");
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
                    Log.Debug("[SceneExitPatches] Persistence capture already executed for this scene transition. Skipping.");
                return;
            }

            _hasCapturedForScene = true;

            var baggedObjects = PersistenceManager.GetCurrentlyBaggedObjects();

            if (NetworkServer.active)
            {
                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(baggedObjects);
            }

            PersistenceManager.CaptureCurrentlyBaggedObjects();

            PersistenceManager.MoveObjectsToPersistenceContainer();

            Log.Debug($" Captured {baggedObjects.Count} bagged objects on scene exit{(NetworkServer.active ? " and sent persistence message" : "")}");
        }
    }
}
