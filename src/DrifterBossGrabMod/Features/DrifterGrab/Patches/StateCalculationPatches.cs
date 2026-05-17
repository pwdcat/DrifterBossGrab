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
    // Harmony patches for state calculation integration
    public static class StateCalculationPatches
    {
        // Patch BluntForceHit3.OnEnter to apply the configured slam damage formula to bludgeon hits
        [HarmonyPatch(typeof(BluntForceHit3), "OnEnter")]
        public class BluntForceHit3_OnEnter_UseFormula
        {
            [HarmonyPostfix]
            public static void Postfix(BluntForceHit3 __instance)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                // Apply formula-based coefficient to bludgeon damage
                __instance.damageCoefficient = SlamDamageCalculator.GetEffectiveCoefficient(bagController);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BluntForceHit3_OnEnter] Applied formula-based bludgeon damage coefficient: {__instance.damageCoefficient:F2}");
                }
            }
        }

        // Patch SuffocateSlam.OnEnter to use the configured slam damage formula
        [HarmonyPatch(typeof(SuffocateSlam), "OnEnter")]
        public class SuffocateSlam_OnEnter_UseDynamicCapacity
        {
            [HarmonyPostfix]
            public static void Postfix(SuffocateSlam __instance)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                // Apply formula-based coefficient from SlamDamageCalculator (respects user's configured formula)
                __instance.damageCoefficient = SlamDamageCalculator.GetEffectiveCoefficient(bagController);

                // Recalculate duration scaling using dynamic capacity
                float damageCapacity = CapacityScalingSystem.CalculateMassCapacity(bagController);
                float originalMassFraction = bagController.baggedMass / DrifterBagController.maxMass;
                float massFraction = bagController.baggedMass / damageCapacity;

                float originalDurationIncrease = __instance.durationIncreaseWithMass * originalMassFraction;
                __instance.baseDuration -= originalDurationIncrease; // Undo original
                __instance.baseDuration += __instance.durationIncreaseWithMass * massFraction; // Apply new

                // Recalculate durationBeforeInterruptable
                float num2 = __instance.baseDurationBeforeInterruptable / __instance.baseDuration;
                __instance.durationBeforeInterruptable = __instance.baseDuration * num2;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[SuffocateSlam_OnEnter] Applied formula-based damage:");
                    Log.Info($"  Damage Coef: {__instance.damageCoefficient:F2} (mass={bagController.baggedMass:F1}, capacity={damageCapacity:F1})");
                    Log.Info($"  Base Duration: {__instance.baseDuration:F2}s");
                }
            }
        }

        // Patch SuffocateSlam.AuthorityModifyOverlapAttack to apply custom damage formula
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

                // Use SlamDamageCalculator to get custom damage coefficient
                float effectiveCoef = SlamDamageCalculator.GetEffectiveCoefficient(bagController);
                var drifterBody = __instance.characterBody;

                if (drifterBody != null)
                {
                    // Apply custom damage to the OverlapAttack
                    overlapAttack.damage = drifterBody.damage * effectiveCoef;

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[SuffocateSlam_AuthorityModifyOverlapAttack] Applied custom damage:");
                        Log.Info($"  OverlapAttack Damage: {overlapAttack.damage:F2} (coef={effectiveCoef:F2}, baseDamage={drifterBody.damage:F2})");
                    }
                }
            }
        }
    }
}
