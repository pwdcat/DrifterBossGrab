using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drifter.Bag;
using EntityStates.Drifter;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.Patches
{
    // Harmony patches for state calculation integration
    public static class StateCalculationPatches
    {
        // Patch to ensure state calculation is applied when a new passenger is assigned to the bag
        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger_StateCalculation
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AssignPassenger_StateCalculation] Called with passengerObject={passengerObject?.name ?? "null"}, EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}");
                }

                if (passengerObject == null || !PluginConfig.Instance.EnableBalance.Value) return;

                // Trigger state recalculation with current mode
                BaggedObjectPatches.SynchronizeBaggedObjectState(__instance, passengerObject);
            }
        }

        // Patch to ensure state calculation is applied when cycling to a new object
        [HarmonyPatch(typeof(BottomlessBagPatches), "CycleToNextObject")]
        public class CycleToNextObject_StateCalculation
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject newObject)
            {
                if (newObject == null || !PluginConfig.Instance.EnableBalance.Value) return;

                // Trigger state recalculation with current mode
                BaggedObjectPatches.SynchronizeBaggedObjectState(__instance, newObject);
            }
        }

        // Patch SuffocateSlam.OnEnter to use dynamic capacity from balance feature instead of hardcoded maxMass
        [HarmonyPatch(typeof(SuffocateSlam), "OnEnter")]
        public class SuffocateSlam_OnEnter_UseDynamicCapacity
        {
            [HarmonyPostfix]
            public static void Postfix(SuffocateSlam __instance)
            {
                // Only apply if balance feature is enabled
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                float damageCapacity = DrifterBagController.maxMass;

                float massFraction = bagController.baggedMass / damageCapacity;
                float baseCoef = __instance.damageCoefficient - (__instance.damageCoefficientIncreaseWithMass * (bagController.baggedMass / DrifterBagController.maxMass));
                __instance.damageCoefficient = baseCoef + (__instance.damageCoefficientIncreaseWithMass * massFraction);

                // Also recalculate duration scaling using dynamic capacity
                // The original code does: baseDuration += durationIncreaseWithMass * num
                // We need to undo the original and recalculate
                float originalMassFraction = bagController.baggedMass / DrifterBagController.maxMass;
                float originalDurationIncrease = __instance.durationIncreaseWithMass * originalMassFraction;
                __instance.baseDuration -= originalDurationIncrease; // Undo original
                __instance.baseDuration += __instance.durationIncreaseWithMass * massFraction; // Apply new

                // Recalculate durationBeforeInterruptable
                float num2 = __instance.baseDurationBeforeInterruptable / __instance.baseDuration;
                __instance.durationBeforeInterruptable = __instance.baseDuration * num2;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[SuffocateSlam_OnEnter] Recalculated using base mass capacity:");
                    Log.Info($"  Bagged Mass: {bagController.baggedMass:F2}");
                    Log.Info($"  Damage Capacity: {damageCapacity:F2} (vs maxMass: {DrifterBagController.maxMass:F2})");
                    Log.Info($"  Mass Fraction: {massFraction:F2} (vs original: {originalMassFraction:F2})");
                    Log.Info($"  Base Coef: {baseCoef:F2}");
                    Log.Info($"  Final Coef: {__instance.damageCoefficient:F2}");
                    Log.Info($"  Base Duration: {__instance.baseDuration:F2}s");
                }
            }
        }
    }
}
