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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CharacterMaster_OnBodyStart] Body: {body.name}, Master: {__instance.name}");
                }

                if (body.name.StartsWith("DrifterBody"))
                {
                    DrifterBossGrabPlugin.IsDrifterPresent = true;

                    if (PluginConfig.Instance.EnableCarouselHUD.Value)
                    {
                        var ui = body.gameObject.AddComponent<UI.BaggedObjectUIController>();
                        ui.slotPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Bag UI.prefab").WaitForCompletion();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[CharacterMaster_OnBodyStart] Added BaggedObjectUIController to DrifterBody, slot prefab loaded: {ui.slotPrefab != null}");
                        }
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CharacterMaster_OnBodyStart] Carousel HUD disabled, skipping BaggedObjectUIController creation");
                    }
                }
                if (!PluginConfig.Instance.EnableObjectPersistence.Value ||
                    !PluginConfig.Instance.EnableAutoGrab.Value ||
                    body == null)
                {
                    return;
                }

                if (body.name == "DrifterBody")
                {

                    PersistenceManager.ScheduleAutoGrab(__instance);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CharacterMaster_OnBodyStart] Scheduled auto-grab for Drifter respawn");
                    }
                }

                Patches.ZoneDetectionPatches.DetectZoneInversion(body.transform.position);
            }
        }

    }
}
