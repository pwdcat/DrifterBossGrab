using DrifterBossGrabMod.Patches;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Balance
{
    // Capacity scaling modes for Drifter's bag
    public enum CapacityScalingMode
    {
        // Increases mass capacity limit based on total capacity
        IncreaseCapacity,

        // Reduces mass of bagged objects based on total capacity
        HalveMass
    }

    // Scaling type for capacity/mass calculations
    public enum ScalingType
    {
        // Linear scaling: adds/subtracts fixed amount per capacity point
        Linear,

        // Exponential scaling: multiplies/divides by factor per capacity point
        Exponential
    }

    // System for managing capacity scaling mechanics
    public static class CapacityScalingSystem
    {
        private const float MinimumMassPercentage = Constants.Limits.MinimumMassPercentage; // 10% minimum mass
        private const float MaximumMassCapacity = float.MaxValue; // Uncapped mass capacity

        // Validates if a capacity value is valid
        public static bool IsValidCapacity(int capacity)
        {
            return capacity >= 0; // 0 = disabled, 1+ = enabled
        }

        // Gets the total capacity (base + utility stocks)
        public static int GetTotalCapacity(DrifterBagController bagController)
        {
            if (bagController == null) return 1;

            var body = bagController.GetComponent<CharacterBody>();
            if (body == null || body.skillLocator == null || body.skillLocator.utility == null)
            {
                return 1;
            }

            int utilityStocks = body.skillLocator.utility.maxStock;
            int baseCapacity = PluginConfig.Instance.BottomlessBagBaseCapacity.Value;

            return baseCapacity + utilityStocks;
        }

        // Calculates the mass capacity limit based on scaling mode
        public static float CalculateMassCapacity(DrifterBagController bagController)
        {
            // If mass capacity is toggled off AND EnableBalance is true, return a very large value (effectively unlimited)
            if (PluginConfig.Instance.EnableBalance.Value && !PluginConfig.Instance.ToggleMassCapacity.Value)
            {
                return float.MaxValue;
            }

            // Only apply balance capacity scaling when EnableBalance is true
            var mode = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.CapacityScalingMode.Value : CapacityScalingMode.IncreaseCapacity;
            var scalingType = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.CapacityScalingType.Value : ScalingType.Linear;
            var bonusPerCapacity = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.CapacityScalingBonusPerCapacity.Value : 100f;
            int totalCapacity = GetTotalCapacity(bagController);

            // Validate capacity
            if (!IsValidCapacity(totalCapacity))
            {
                Log.Warning($"[CalculateMassCapacity] Invalid capacity value: {totalCapacity}. Defaulting to 1.");
                totalCapacity = 1;
            }

            // Handle capacity = 0 (disabled) - no capacity allowed
            if (totalCapacity <= 0)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CalculateMassCapacity] Capacity is 0 or negative ({totalCapacity}), returning 0f (no capacity allowed)");
                }
                return 0f;
            }

            // Handle capacity = 1 (default behavior without halving)
            if (totalCapacity == 1)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CalculateMassCapacity] Capacity is 1, using default mass capacity");
                }
                // Use base mass capacity for capacity = 1
                return DrifterBagController.maxMass;
            }

            if (mode == CapacityScalingMode.IncreaseCapacity)
            {
                float calculatedCapacity = CalculateIncreasedCapacity(scalingType, totalCapacity, bonusPerCapacity);

                // Validate result is not NaN or Infinity
                if (float.IsNaN(calculatedCapacity) || float.IsInfinity(calculatedCapacity))
                {
                    Log.Error($"[CalculateMassCapacity] Invalid calculated capacity (NaN/Infinity): {calculatedCapacity}. Returning base mass capacity.");
                    return DrifterBagController.maxMass;
                }

                return calculatedCapacity;
            }

            // If HalveMass mode, use the base max mass capacity (DrifterBagController.maxMass)
            // This ensures the bag still has a proper mass cap while halving object masses
            return DrifterBagController.maxMass;
        }

        // Calculates increased mass capacity
        private static float CalculateIncreasedCapacity(ScalingType scalingType, int totalCapacity, float bonusPerCapacity)
        {
            // Validate bonusPerCapacity
            if (float.IsNaN(bonusPerCapacity) || float.IsInfinity(bonusPerCapacity))
            {
                Log.Error($"[CalculateIncreasedCapacity] Invalid bonusPerCapacity: {bonusPerCapacity}. Using default value.");
                bonusPerCapacity = 100f; // Default fallback
            }

            // Ensure bonusPerCapacity is positive
            if (bonusPerCapacity <= 0)
            {
                Log.Warning($"[CalculateIncreasedCapacity] Non-positive bonusPerCapacity: {bonusPerCapacity}. Using default value 100f.");
                bonusPerCapacity = 100f;
            }

            float result;
            if (scalingType == ScalingType.Linear)
            {
                // Linear: Total * Bonus
                result = totalCapacity * bonusPerCapacity;

                // Validate result
                if (float.IsNaN(result) || float.IsInfinity(result))
                {
                    Log.Error($"[CalculateIncreasedCapacity] Invalid linear result: {result}. Returning base mass capacity.");
                    return DrifterBagController.maxMass;
                }

                return result;
            }
            else // Exponential
            {
                // Exponential: Bonus * (1 + Bonus/100) ^ (Total - 1)
                float multiplier = Constants.Multipliers.ScalingMultiplierBase + (bonusPerCapacity / Constants.Multipliers.PercentageDivisor);

                // Validate multiplier
                if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0)
                {
                    Log.Error($"[CalculateIncreasedCapacity] Invalid multiplier: {multiplier}. Using default linear calculation.");
                    return totalCapacity * bonusPerCapacity;
                }

                // For very large capacity values, cap the exponent to prevent overflow
                int exponent = Mathf.Min(totalCapacity - 1, 100); // Cap exponent at 100
                if (exponent != totalCapacity - 1)
                {
                    Log.Warning($"[CalculateIncreasedCapacity] Large capacity ({totalCapacity}) - capping exponent at {exponent}");
                }

                result = bonusPerCapacity * Mathf.Pow(multiplier, exponent);

                // Validate result
                if (float.IsNaN(result) || float.IsInfinity(result))
                {
                    Log.Error($"[CalculateIncreasedCapacity] Invalid exponential result: {result}. Returning linear calculation.");
                    return totalCapacity * bonusPerCapacity;
                }

                return result;
            }
        }

        // Applies mass scaling to an object's mass
        public static float ApplyMassScaling(DrifterBagController bagController, GameObject baggedObject, float baseMass)
        {
            // Validate baseMass
            if (float.IsNaN(baseMass) || float.IsInfinity(baseMass))
            {
                Log.Error($"[ApplyMassScaling] Invalid baseMass: {baseMass}. Returning 0f.");
                return 0f;
            }

            // Ensure baseMass is non-negative
            if (baseMass < 0)
            {
                Log.Warning($"[ApplyMassScaling] Negative baseMass: {baseMass}. Using absolute value.");
                baseMass = Mathf.Abs(baseMass);
            }

            var mode = PluginConfig.Instance.CapacityScalingMode.Value;
            var scalingType = PluginConfig.Instance.CapacityScalingType.Value;
            var bonusPerCapacity = PluginConfig.Instance.CapacityScalingBonusPerCapacity.Value;
            int totalCapacity = GetTotalCapacity(bagController);

            // Validate capacity
            if (!IsValidCapacity(totalCapacity))
            {
                Log.Warning($"[ApplyMassScaling] Invalid capacity value: {totalCapacity}. Defaulting to 1.");
                totalCapacity = 1;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ApplyMassScaling] Object: {baggedObject?.name ?? "null"}, BaseMass: {baseMass}, Mode: {mode}, ScalingType: {scalingType}, BonusPerCapacity: {bonusPerCapacity}, TotalCapacity: {totalCapacity}");
            }

            // Only apply mass scaling when EnableBalance is true and mode is HalveMass
            if (!PluginConfig.Instance.EnableBalance.Value || mode != CapacityScalingMode.HalveMass)
            {
                return baseMass; // No scaling in IncreaseCapacity mode
            }

            // Handle capacity = 0 (disabled) - no scaling allowed
            if (totalCapacity <= 0)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ApplyMassScaling] Capacity is 0 or negative ({totalCapacity}), returning base mass: {baseMass}");
                }
                return baseMass;
            }

            // When capacity = 1, use default functionality with no halving
            if (totalCapacity == 1)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ApplyMassScaling] Capacity = 1, returning base mass: {baseMass}");
                }
                return baseMass;
            }

            float scaledMass = CalculateHalvedMass(scalingType, totalCapacity, bonusPerCapacity, baseMass);

            // Validate scaledMass
            if (float.IsNaN(scaledMass) || float.IsInfinity(scaledMass))
            {
                Log.Error($"[ApplyMassScaling] Invalid scaledMass: {scaledMass}. Returning base mass.");
                return baseMass;
            }

            // Clamp to minimum 10% of original mass
            float minimumMass = baseMass * MinimumMassPercentage;
            float finalMass = Mathf.Max(scaledMass, minimumMass);

            // Validate finalMass
            if (float.IsNaN(finalMass) || float.IsInfinity(finalMass))
            {
                Log.Error($"[ApplyMassScaling] Invalid finalMass: {finalMass}. Returning base mass.");
                return baseMass;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ApplyMassScaling] ScaledMass: {scaledMass}, MinimumMass: {minimumMass}, FinalMass: {finalMass}");
            }

            return finalMass;
        }

        // Calculates halved mass
        private static float CalculateHalvedMass(ScalingType scalingType, int totalCapacity, float bonusPerCapacity, float baseMass)
        {
            // Validate inputs
            if (float.IsNaN(baseMass) || float.IsInfinity(baseMass))
            {
                Log.Error($"[CalculateHalvedMass] Invalid baseMass: {baseMass}. Returning 0f.");
                return 0f;
            }

            if (float.IsNaN(bonusPerCapacity) || float.IsInfinity(bonusPerCapacity))
            {
                Log.Error($"[CalculateHalvedMass] Invalid bonusPerCapacity: {bonusPerCapacity}. Using default value 100f.");
                bonusPerCapacity = 100f;
            }

            // Ensure bonusPerCapacity is positive
            if (bonusPerCapacity <= 0)
            {
                Log.Warning($"[CalculateHalvedMass] Non-positive bonusPerCapacity: {bonusPerCapacity}. Using default value 100f.");
                bonusPerCapacity = 100f;
            }

            if (totalCapacity <= 0)
            {
                Log.Warning($"[CalculateHalvedMass] Invalid totalCapacity: {totalCapacity}. Returning base mass.");
                return baseMass;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CalculateHalvedMass] ScalingType: {scalingType}, TotalCapacity: {totalCapacity}, BonusPerCapacity: {bonusPerCapacity}, BaseMass: {baseMass}");
            }

            float result;
            if (scalingType == ScalingType.Linear)
            {
                // Linear: Base * (Bonus / 100) / Total
                // When Bonus = 100, this is Base / Total (divide by capacity)
                // When Bonus = 50, this is Base * 0.5 / Total (half of divide by capacity)
                float divisor = totalCapacity * (100f / bonusPerCapacity);

                // Validate divisor
                if (float.IsNaN(divisor) || float.IsInfinity(divisor) || divisor == 0)
                {
                    Log.Error($"[CalculateHalvedMass] Invalid divisor: {divisor}. Returning base mass.");
                    return baseMass;
                }

                result = baseMass / divisor;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CalculateHalvedMass] Linear - Divisor: {divisor}, Result: {result}");
                }
            }
            else // Exponential
            {
                // Exponential: Base * 0.5 ^ Total
                // For very large capacity values, cap the exponent to prevent underflow
                int exponent = Mathf.Min(totalCapacity, 50); // Cap exponent at 50
                if (exponent != totalCapacity)
                {
                    Log.Warning($"[CalculateHalvedMass] Large capacity ({totalCapacity}) - capping exponent at {exponent}");
                }

                float power = Mathf.Pow(Constants.Multipliers.ExponentialScalingBase, exponent);
                result = baseMass * power;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CalculateHalvedMass] Exponential - Power: {power}, Result: {result}");
                }
            }

            // Validate result
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                Log.Error($"[CalculateHalvedMass] Invalid result: {result}. Returning base mass.");
                return baseMass;
            }

            return result;
        }

        // Recalculates capacity when utility stock changes
        public static void RecalculateCapacity(DrifterBagController bagController)
        {
            if (bagController == null) return;

            int totalCapacity = GetTotalCapacity(bagController);
            float massCapacity = CalculateMassCapacity(bagController);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CapacityScaling] Recalculating capacity: Total={totalCapacity}, MassCapacity={massCapacity}");
            }

            // Force recalculate mass to apply any scaling changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates state when state calculation mode changes
        public static void RecalculateState(DrifterBagController bagController)
        {
            if (bagController == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CapacityScaling] Recalculating state for bag controller");
            }

            // Force recalculate mass to apply any state calculation changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates mass when mass multiplier changes
        public static void RecalculateMass(DrifterBagController bagController)
        {
            if (bagController == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CapacityScaling] Recalculating mass for bag controller");
            }

            // Force recalculate mass to apply any mass multiplier changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates stats when stats multiplier changes
        public static void RecalculateStats(DrifterBagController bagController)
        {
            if (bagController == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CapacityScaling] Recalculating stats for bag controller");
            }

            // Force recalculate mass to apply any stats multiplier changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }

        // Recalculates penalty when penalty settings change
        public static void RecalculatePenalty(DrifterBagController bagController)
        {
            if (bagController == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CapacityScaling] Recalculating penalty for bag controller");
            }

            // Force recalculate mass to apply any penalty changes
            BagPassengerManager.ForceRecalculateMass(bagController);
        }
    }
}
