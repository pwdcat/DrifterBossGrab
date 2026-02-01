using System;
using HarmonyLib;
using RoR2;
using RoR2.HudOverlay;
using UnityEngine;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class UIPatches
    {
        [HarmonyPatch(typeof(BaggedObject), "OnUIOverlayInstanceAdded")]
        public class BaggedObject_OnUIOverlayInstanceAdded
        {
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, OverlayController controller, GameObject instance)
            {
                // If Carousel HUD is disabled, we do NOT want to interfere with the vanilla UI.
                // Just return and let the vanilla UI overlay exist.
                if (!PluginConfig.Instance.EnableCarouselHUD.Value)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info(" [OnUIOverlayInstanceAdded] Carousel HUD disabled, preserving vanilla UI overlay.");
                    }
                    return;
                }

                // If Carousel HUD is enabled, we ALWAYS remove the vanilla overlay to avoid conflict/duplication.
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [OnUIOverlayInstanceAdded] Carousel HUD enabled, removing vanilla overlay for {__instance.targetObject?.name ?? "null"}");
                }

                if (controller != null)
                {
                    HudOverlayManager.RemoveOverlay(controller);
                    // Null out the field to prevent OnExit from trying to remove again
                    var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                    uiOverlayField.SetValue(__instance, null);
                }
            }
        }
    }
}
