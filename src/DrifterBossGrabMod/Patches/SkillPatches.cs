using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    public static class SkillPatches
    {
        [HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.RestockSteplike))]
        public class GenericSkill_RestockSteplike
        {
            [HarmonyPrefix]
            public static bool Prefix(GenericSkill __instance)
            {
                if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !PluginConfig.Instance.EnableStockRefreshClamping.Value)
                {
                    return true;
                }

                // Check if this is Drifter's utility skill
                if (__instance.characterBody && __instance.characterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody") && __instance.characterBody.skillLocator && __instance.characterBody.skillLocator.utility == __instance)
                {
                    // Get the bag controller
                    var bagController = __instance.characterBody.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        int baggedCount = BagPatches.GetCurrentBaggedCount(bagController);
                        int clampedMax = Mathf.Max(1, __instance.maxStock - baggedCount);
                        if (__instance.stock >= clampedMax)
                        {
                            // Skip the restock - stock is already at or above clamped max
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[GenericSkill_RestockSteplike] Skipping restock - stock {__instance.stock} is already at clamped max {clampedMax} (baggedCount: {baggedCount})");
                            }
                            return false;
                        }
                    }
                }
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(GenericSkill __instance)
            {
                ClampDrifterUtilityStock(__instance);
            }
        }

        [HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.Reset))]
        public class GenericSkill_Reset
        {
            [HarmonyPrefix]
            public static bool Prefix(GenericSkill __instance)
            {
                if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !PluginConfig.Instance.EnableStockRefreshClamping.Value)
                {
                    return true;
                }

                // Check if this is Drifter's utility skill
                if (__instance.characterBody && __instance.characterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody") && __instance.characterBody.skillLocator && __instance.characterBody.skillLocator.utility == __instance)
                {
                    // Get the bag controller
                    var bagController = __instance.characterBody.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        int baggedCount = BagPatches.GetCurrentBaggedCount(bagController);
                        int clampedMax = Mathf.Max(1, __instance.maxStock - baggedCount);
                        if (__instance.stock >= clampedMax)
                        {
                            // Skip the reset - stock is already at or above clamped max
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[GenericSkill_Reset] Skipping reset - stock {__instance.stock} is already at clamped max {clampedMax} (baggedCount: {baggedCount})");
                            }
                            return false;
                        }
                    }
                }
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(GenericSkill __instance)
            {
                ClampDrifterUtilityStock(__instance);
            }
        }

        [HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.RunRecharge))]
        public class GenericSkill_RunRecharge
        {
            [HarmonyPrefix]
            public static bool Prefix(GenericSkill __instance)
            {
                if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !PluginConfig.Instance.EnableStockRefreshClamping.Value)
                {
                    return true;
                }

                // Check if this is Drifter's utility skill
                if (__instance.characterBody && __instance.characterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody") && __instance.characterBody.skillLocator && __instance.characterBody.skillLocator.utility == __instance)
                {
                    // Get the bag controller
                    var bagController = __instance.characterBody.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        int baggedCount = BagPatches.GetCurrentBaggedCount(bagController);
                        int clampedMax = Mathf.Max(1, __instance.maxStock - baggedCount);
                        if (__instance.stock >= clampedMax)
                        {
                            __instance.rechargeStopwatch = 0f; // Reset timer when at max
                            return false; // Skip recharging
                        }
                    }
                }
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(GenericSkill __instance)
            {
                ClampDrifterUtilityStock(__instance);
            }
        }

        private static void ClampDrifterUtilityStock(GenericSkill skill)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !PluginConfig.Instance.EnableStockRefreshClamping.Value)
            {
                return;
            }

            // Check if this is Drifter's utility skill
            if (skill.characterBody && skill.characterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody") && skill.characterBody.skillLocator && skill.characterBody.skillLocator.utility == skill)
            {
                // Get the bag controller
                var bagController = skill.characterBody.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    int baggedCount = BagPatches.GetCurrentBaggedCount(bagController);
                    int maxAllowedStock = skill.maxStock - baggedCount;
                    if (skill.stock > maxAllowedStock)
                    {
                        skill.stock = Mathf.Max(1, maxAllowedStock);
                    }
                }
            }
        }
    }
}