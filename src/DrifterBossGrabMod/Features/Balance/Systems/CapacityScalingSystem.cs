#nullable enable
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.Patches;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Balance
{
    // System for managing capacity scaling mechanics
    public static class CapacityScalingSystem
    {
        private const float MinimumMassPercentage = Constants.Limits.MinimumMassPercentage; // 10% minimum mass

        // Gets the total capacity (base + utility stocks)
        public static int GetTotalCapacity(DrifterBagController bagController)
        {
            if (bagController == null) return 1;
            return BagCapacityCalculator.GetUtilityMaxStock(bagController);
        }

        // Calculates the mass capacity limit using the MassCapacityFormula
        public static float CalculateMassCapacity(DrifterBagController bagController)
        {
            // When balance is disabled, use a sensible default
            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                int totalCapacity = GetTotalCapacity(bagController);
                // Handle INF capacity case
                if (totalCapacity == int.MaxValue)
                {
                    return float.MaxValue;
                }
                return totalCapacity * Constants.Limits.DefaultMassPerStock; // Default linear: 100 per stock
            }

            // Get character stats for formula variables
            var body = bagController.GetComponent<CharacterBody>();

            string formula = PluginConfig.Instance.MassCapacityFormula.Value;
            float result = FormulaParser.Evaluate(formula, body, null);

            // Validate result
            if (float.IsNaN(result))
            {
                Log.DebugIfEnabled($"[CalculateMassCapacity] Formula '{formula}' returned NaN. Returning base mass capacity.");
                return DrifterBagController.maxMass;
            }

            // If MassCapacityFormula evaluates to 0, return unlimited (mass capacity disabled)
            if (result <= 0f && !float.IsPositiveInfinity(result))
            {
                Log.DebugIfEnabled("[CalculateMassCapacity] Formula returned {0}, mass capacity is disabled (unlimited)", result);
                return float.MaxValue;
            }

            return result;
        }

        // Recalculates capacity when utility stock changes
        public static void RecalculateCapacity(DrifterBagController bagController)
        {
            if (bagController == null) return;

            int totalCapacity = GetTotalCapacity(bagController);
            float massCapacity = CalculateMassCapacity(bagController);

            Log.DebugIfEnabled("[CapacityScaling] Recalculating capacity: Total={0}, MassCapacity={1}", totalCapacity, massCapacity);

            // Force recalculate mass to apply any scaling changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates state when state calculation mode changes
        public static void RecalculateState(DrifterBagController bagController)
        {
            if (bagController == null) return;

            Log.DebugIfEnabled("[CapacityScaling] Recalculating state for bag controller");

            // Force recalculate mass to apply any state calculation changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates mass when mass multiplier changes
        public static void RecalculateMass(DrifterBagController bagController)
        {
            if (bagController == null) return;

            Log.DebugIfEnabled("[CapacityScaling] Recalculating mass for bag controller");

            // Force recalculate mass to apply any mass multiplier changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates penalty when penalty settings change
        public static void RecalculatePenalty(DrifterBagController bagController)
        {
            if (bagController == null) return;

            Log.DebugIfEnabled("[CapacityScaling] Recalculating penalty for bag controller");

            // Force recalculate mass to apply any penalty changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }
    }
}

