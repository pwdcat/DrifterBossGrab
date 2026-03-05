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
                    // Update UI if slot scaling formula is active (not "0" or empty)
                    string slotFormula = PluginConfig.Instance.SlotScalingFormula.Value?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(slotFormula) && slotFormula != "0")
                    {
                        UIPatches.UpdateMassCapacityUIOnCapacityChange(drifterBagController);
                    }

                    if (PluginConfig.Instance.OverencumbranceMax.Value > 0)
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
