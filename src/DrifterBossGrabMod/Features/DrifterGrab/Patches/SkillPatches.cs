using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    public static class SkillPatches
    {
        [HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.RunRecharge), new Type[] { typeof(float) })]
        public class GenericSkill_RunRecharge
        {
            [HarmonyPrefix]
            public static bool Prefix(GenericSkill __instance)
            {
                if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !PluginConfig.Instance.EnableStockRefreshClamping.Value)
                {
                    return true;
                }

                if (__instance.characterBody && __instance.characterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody") && __instance.characterBody.skillLocator && __instance.characterBody.skillLocator.utility == __instance)
                {
                    var bagController = __instance.characterBody.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        int baggedCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                        int clampedMax = Mathf.Max(1, __instance.maxStock - baggedCount);

                        // If stock is GEQ clamped max, prevent RunRecharge from incrementing the stopwatch
                        // and prevent it from ever reaching RestockSteplike
                        if (__instance.stock >= clampedMax)
                        {
                            __instance.rechargeStopwatch = 0f;
                            return false; // Skip the original RunRecharge
                        }
                    }
                }
                return true;
            }
        }
    }
}
