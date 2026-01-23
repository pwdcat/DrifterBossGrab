using HarmonyLib;
using RoR2;
using RoR2.HudOverlay;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
namespace DrifterBossGrabMod.Patches
{
    public static class CharacterSpawnPatches
    {
        [HarmonyPatch(typeof(CharacterMaster), "OnBodyStart")]
        public class CharacterMaster_OnBodyStart
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterMaster __instance, CharacterBody body)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CharacterMaster_OnBodyStart] Body: {body.name}, Master: {__instance.name}");
                }
                // Check if this is a Drifter player respawn
                if (body.name.StartsWith("DrifterBody"))
                {
                    DrifterBossGrabPlugin.IsDrifterPresent = true;
                    // Add carousel UI for bagged objects using Bag UI
                    var ui = body.gameObject.AddComponent<UI.BaggedObjectUIController>();
                    ui.slotPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Bag UI.prefab").WaitForCompletion();
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CharacterMaster_OnBodyStart] Added BaggedObjectUIController to DrifterBody, slot prefab loaded: {ui.slotPrefab != null}");
                    }
                }
                if (!PluginConfig.Instance.EnableObjectPersistence.Value ||
                    !PluginConfig.Instance.EnableAutoGrab.Value ||
                    body == null)
                {
                    return;
                }
                // Check if this is a Drifter player respawn for auto-grab
                if (body.name == "DrifterBody")
                {
                    // Schedule auto-grab with delay to ensure bag controller is ready
                    PersistenceManager.ScheduleAutoGrab(__instance);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CharacterMaster_OnBodyStart] Scheduled auto-grab for Drifter respawn");
                    }
                }
                // Detect zone inversion on first player spawn
                Patches.OtherPatches.DetectZoneInversion(body.transform.position);
            }
        }


        [HarmonyPatch(typeof(BaggedCardController), "set_sourceBody")]
        public class BaggedCardController_set_sourceBody
        {
            [HarmonyPostfix]
            public static void Postfix(BaggedCardController __instance, CharacterBody value)
            {
                Debug.Log($"[BaggedCardController] set_sourceBody: {value}, transform={__instance.transform}");
            }
        }

        [HarmonyPatch(typeof(BaggedCardController), "set_sourcePassengerAttributes")]
        public class BaggedCardController_set_sourcePassengerAttributes
        {
            [HarmonyPostfix]
            public static void Postfix(BaggedCardController __instance, SpecialObjectAttributes value)
            {
                Debug.Log($"[BaggedCardController] set_sourcePassengerAttributes: {value}, transform={__instance.transform}");
            }
        }


    }
}
