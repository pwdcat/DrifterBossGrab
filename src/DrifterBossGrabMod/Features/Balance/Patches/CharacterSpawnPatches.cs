#nullable enable
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
                Log.DebugIfEnabled("[CharacterMaster_OnBodyStart] Body: {0}, Master: {1}", body.name, __instance.name);
                // Check if this is a Drifter player respawn
                if (body.name.StartsWith("DrifterBody"))
                {
                    DrifterBossGrabPlugin.IsDrifterPresent = true;
                    // Add carousel UI for bagged objects using Bag UI
                    if (PluginConfig.Instance.EnableCarouselHUD.Value)
                    {
                        var ui = body.gameObject.AddComponent<UI.BaggedObjectUIController>();
                        ui.slotPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Bag UI.prefab").WaitForCompletion();
                        Log.DebugIfEnabled("[CharacterMaster_OnBodyStart] Added BaggedObjectUIController to DrifterBody, slot prefab loaded: {0}", ui.slotPrefab != null);
                    }
                    Log.DebugIfEnabled("[CharacterMaster_OnBodyStart] Carousel HUD disabled, skipping BaggedObjectUIController creation");
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
                    Log.DebugIfEnabled("[CharacterMaster_OnBodyStart] Scheduled auto-grab for Drifter respawn");
                }
                // Detect zone inversion on first player spawn
                Patches.ZoneDetectionPatches.DetectZoneInversion(body.transform.position);
            }
        }

    }
}
