using System;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    // Patches for balance features (capacity scaling, elite bonus, overencumbrance)
    public static class BalancePatches
    {
        [HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.maxStock), MethodType.Setter)]
        public class GenericSkill_maxStock_Setter_Patch
        {
            static void Postfix(GenericSkill __instance)
            {
                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    Balance.CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), nameof(DrifterBagController.CalculateBaggedObjectMass))]
        public class DrifterBagController_CalculateBaggedObjectMass_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject targetObject, ref float __result)
            {
                // Only apply balance features when EnableBalance is true
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                if (targetObject != null)
                {
                    if (PluginConfig.Instance.CapacityScalingMode.Value == Balance.CapacityScalingMode.HalveMass)
                    {
                        __result = Balance.CapacityScalingSystem.ApplyMassScaling(__instance, targetObject, __result);
                    }

                    // Apply character flag mass bonus using the new system
                    __result = Balance.CharacterFlagMassBonus.ApplyFlagBonus(targetObject, __result);
                }
            }
        }

        [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
        public class CharacterBody_RecalculateStats_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterBody __instance)
            {
                // Only apply overencumbrance when EnableBalance is true
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
                if (drifterBagController != null && drifterBagController.hasAuthority)
                {
                    if (PluginConfig.Instance.HealthPerExtraSlot.Value > 0 || PluginConfig.Instance.LevelsPerExtraSlot.Value > 0)
                    {
                        UIPatches.UpdateMassCapacityUIOnCapacityChange(drifterBagController);
                    }

                    if (PluginConfig.Instance.EnableOverencumbrance.Value)
                    {
                        Balance.OverencumbranceSystem.ApplyOverencumbrance(__instance, drifterBagController);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.OnDestroy))]
        public class CharacterBody_OnDestroy_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(CharacterBody __instance)
            {
                Balance.OverencumbranceSystem.CleanupCharacterBody(__instance);
            }
        }
    }
}
