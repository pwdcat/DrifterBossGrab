using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drifter.Bag;
using EntityStates.Drifter;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Balance;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{

    // ========================================================================================
    // STATE CALCULATION PATCHES
    // ========================================================================================
    public static class StateCalculationPatches
    {

        [HarmonyPatch(typeof(BluntForceHit3), "OnEnter")]
        public class BluntForceHit3_OnEnter_UseFormula
        {
            [HarmonyPostfix]
            public static void Postfix(BluntForceHit3 __instance)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                bagController.maxSmacks = Core.StateCalculator.CalculateMaxSmacks(bagController);
                __instance.damageCoefficient = SlamDamageCalculator.GetEffectiveCoefficient(bagController);
            }
        }

        [HarmonyPatch(typeof(SuffocateSlam), "OnEnter")]
        public class SuffocateSlam_OnEnter_UseDynamicCapacity
        {
            [HarmonyPostfix]
            public static void Postfix(SuffocateSlam __instance)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                bagController.maxSmacks = Core.StateCalculator.CalculateMaxSmacks(bagController);
                __instance.damageCoefficient = SlamDamageCalculator.GetEffectiveCoefficient(bagController);

                float damageCapacity = CapacityScalingSystem.CalculateMassCapacity(bagController);
                float originalMassFraction = bagController.baggedMass / DrifterBagController.maxMass;
                float massFraction = bagController.baggedMass / damageCapacity;

                float originalDurationIncrease = __instance.durationIncreaseWithMass * originalMassFraction;
                __instance.baseDuration -= originalDurationIncrease;
                __instance.baseDuration += __instance.durationIncreaseWithMass * massFraction;

                float num2 = __instance.baseDurationBeforeInterruptable / __instance.baseDuration;
                __instance.durationBeforeInterruptable = __instance.baseDuration * num2;
            }
        }

        [HarmonyPatch(typeof(SuffocateSlam), "AuthorityModifyOverlapAttack")]
        public class SuffocateSlam_AuthorityModifyOverlapAttack_ApplyCustomDamage
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static void Prefix(SuffocateSlam __instance, OverlapAttack overlapAttack)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                float effectiveCoef = SlamDamageCalculator.GetEffectiveCoefficient(bagController);
                var drifterBody = __instance.characterBody;

                if (drifterBody != null)
                {

                    overlapAttack.damage = drifterBody.damage * effectiveCoef;
                }
            }
        }
    }

    [HarmonyPatch(typeof(DrifterBagController), "IncreaseSmacks")]
    public static class UnlimitedHitsPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(DrifterBagController __instance)
        {
            if (!PluginConfig.Instance.EnableBalance.Value) return true;
            return Core.StateCalculator.CalculateMaxSmacks(__instance) != int.MaxValue;
        }
    }
}
