#nullable enable
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.Patches;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Balance
{

    public static class CapacityScalingSystem
    {
        private const float MinimumMassPercentage = Constants.Limits.MinimumMassPercentage;

        public static int GetTotalCapacity(DrifterBagController? bagController)
        {
            if (bagController == null) return 1;
            return BagCapacityCalculator.GetUtilityMaxStock(bagController);
        }

        public static bool IsMassCapacityUnlimited(DrifterBagController? bagController)
        {
            if (!PluginConfig.Instance.EnableBalance.Value) return true;

            float massCapacity = CalculateMassCapacity(bagController);
            return massCapacity >= float.MaxValue || float.IsInfinity(massCapacity) || massCapacity <= 0f;
        }

        public static float CalculateMassCapacity(DrifterBagController? bagController)
        {

            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                int totalCapacity = GetTotalCapacity(bagController);

                if (totalCapacity == int.MaxValue)
                {
                    return float.MaxValue;
                }
                return totalCapacity * Constants.Limits.DefaultMassPerStock;
            }

            var body = bagController?.GetComponent<CharacterBody>();

            string formula = PluginConfig.Instance.MassCapacityFormula.Value;
            float result = FormulaParser.Evaluate(formula, body, null);

            if (float.IsNaN(result))
            {
                Log.Warning($"[CalculateMassCapacity] Formula '{formula}' returned NaN. Returning base mass capacity.");
                return DrifterBagController.maxMass;
            }

            if (result <= 0f && !float.IsPositiveInfinity(result))
            {
                Log.Debug($"[CalculateMassCapacity] Formula returned {result}, mass capacity is disabled (unlimited)");
                return float.MaxValue;
            }

            return result;
        }

        public static float CalculateMaxMassCapacity(DrifterBagController? bagController)
        {
            float baseCapacity = CalculateMassCapacity(bagController);
            if (baseCapacity == float.MaxValue) return float.MaxValue;

            float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
                : Constants.Multipliers.DefaultMassMultiplier;

            return baseCapacity * overencumbranceMultiplier;
        }

        public static void RecalculateCapacity(DrifterBagController? bagController)
        {
            if (bagController == null) return;

            int totalCapacity = GetTotalCapacity(bagController);
            float massCapacity = CalculateMassCapacity(bagController);

            Log.Debug($"[CapacityScaling] Recalculating capacity: Total={totalCapacity}, MassCapacity={massCapacity}");

            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        public static void RecalculateState(DrifterBagController? bagController)
        {
            if (bagController == null) return;

            Log.Debug($"[CapacityScaling] Recalculating state for bag controller");

            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        public static void RecalculateMass(DrifterBagController? bagController)
        {
            if (bagController == null) return;

            Log.Debug($"[CapacityScaling] Recalculating mass for bag controller");

            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        public static void RecalculatePenalty(DrifterBagController? bagController)
        {
            if (bagController == null) return;

            Log.Debug($"[CapacityScaling] Recalculating penalty for bag controller");

            BagPassengerManager.ForceRecalculateMass(bagController);
        }
    }
}
