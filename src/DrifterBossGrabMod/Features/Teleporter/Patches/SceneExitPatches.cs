#nullable enable
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch(typeof(SceneExitController))]
    public static class SceneExitPatches
    {
        private static bool _hasCapturedForScene = false;

        public static void ResetCaptureFlag()
        {
            _hasCapturedForScene = false;
        }

        [HarmonyPatch("SetState")]
        [HarmonyPrefix]
        private static void SetStatePrefix(SceneExitController __instance, SceneExitController.ExitState newState)
        {
            if (newState != SceneExitController.ExitState.Idle)
            {
                ExecutePersistenceCapture();
            }
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

