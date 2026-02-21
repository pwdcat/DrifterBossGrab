using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.Patches
{
    // Provides static helper methods for calculating bag capacity and mass
    public static class BagCapacityCalculator
    {
        // Calculates total mass of bagged objects + incoming object
        public static float CalculateTotalBagMass(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            if (drifterBagController == null) return 0f;

            float totalMass = 0f;
            var list = BagPatches.GetState(drifterBagController).BaggedObjects;
            if (list != null)
            {
                foreach (var obj in list)
                {
                    if (obj != null && !OtherPatches.IsInProjectileState(obj))
                    {
                        totalMass += drifterBagController.CalculateBaggedObjectMass(obj);
                    }
                }
            }

            // Check if there's an incoming object being tracked
            GameObject? predictiveIncomingObject = incomingObject;
            if (predictiveIncomingObject == null)
            {
                predictiveIncomingObject = BagPatches.GetState(drifterBagController).IncomingObject;
            }

            if (predictiveIncomingObject != null && !OtherPatches.IsInProjectileState(predictiveIncomingObject))
            {
                totalMass += drifterBagController.CalculateBaggedObjectMass(predictiveIncomingObject);
            }

            return totalMass;
        }

        // Gets maximum utility stock capacity for bag
        public static int GetUtilityMaxStock(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            // If UncapCapacity is enabled AND EnableBalance is true, check mass capacity
            // The bag will rely purely on mass capacity instead of slot count
            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapCapacity.Value)
            {
                // Check if we're at or would be at mass cap
                int usedCapacity = GetCurrentBaggedCount(drifterBagController);
                float totalMass = CalculateTotalBagMass(drifterBagController, incomingObject);
                
                // Calculate mass capacity with overencumbrance limit
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(drifterBagController);
                float overencumbranceMultiplier = Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor);
                float maxMassCapacity = massCapacity * overencumbranceMultiplier;
                
                // Check if we're at or would be at 200% mass cap
                bool isAtMassCap = totalMass >= maxMassCapacity;
                if (isAtMassCap)
                {
                    // Enforce slot capacity to used capacity (minimum 1)
                    return Math.Max(1, usedCapacity);
                }
                
                // Not at mass cap - return a large value (effectively unlimited)
                return int.MaxValue;
            }

            if (!FeatureState.IsCyclingEnabled)
            {
                return Constants.Limits.SingleCapacity;
            }
            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int maxStock = body.skillLocator.utility.maxStock;
                int baseSlots = maxStock + PluginConfig.Instance.BottomlessBagBaseCapacity.Value;

                int extraSlots = 0;

                // Add Capacity slots using new formula (Health and Level sliders)
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    float healthSlots = 0;
                    float levelSlots = 0;

                    float healthPerSlot = PluginConfig.Instance.HealthPerExtraSlot.Value;
                    if (healthPerSlot > 0)
                    {
                        float health = body.maxHealth;
                        healthSlots = Mathf.Floor(health / healthPerSlot);
                    }

                    int levelsPerSlot = PluginConfig.Instance.LevelsPerExtraSlot.Value;
                    if (levelsPerSlot > 0)
                    {
                        levelSlots = Mathf.Floor((body.level - 1f) / levelsPerSlot);
                    }

                    extraSlots = (int)healthSlots + (int)levelSlots;
                }

                int slotCapacity = baseSlots + extraSlots;

                // If overencumbrance is enabled, check if we're at 200% mass cap
                if (PluginConfig.Instance.EnableOverencumbrance.Value && PluginConfig.Instance.EnableBalance.Value)
                {
                    // Predictive capacity calculation: Check if we WOULD be at mass cap after adding incoming object
                    int usedCapacity = GetCurrentBaggedCount(drifterBagController);
                    float totalMass = CalculateTotalBagMass(drifterBagController, incomingObject);

                    // Calculate mass capacity with overencumbrance limit
                    float massCapacity = CapacityScalingSystem.CalculateMassCapacity(drifterBagController);
                    // Only apply overencumbrance settings when EnableBalance is true
                    float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                        ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor)
                        : Constants.Multipliers.DefaultMassMultiplier;
                    float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                    // Check if we're at or would be at 200% mass cap
                    bool isAtMassCap = totalMass >= maxMassCapacity;
                    if (isAtMassCap)
                    {
                        // Enforce slot capacity to used capacity (minimum 1) to make it "maxed out"
                        slotCapacity = Math.Max(1, usedCapacity);
                    }
                }

                return slotCapacity;
            }
            return PluginConfig.Instance.BottomlessBagBaseCapacity.Value;
        }

        // Checks if bag is at or above 200% mass capacity cap
        private static bool IsAtMassCapacityCap(DrifterBagController drifterBagController)
        {
            if (drifterBagController == null) return false;

            // Calculate mass capacity with overencumbrance limit
            float massCapacity = CapacityScalingSystem.CalculateMassCapacity(drifterBagController);
            // Only apply overencumbrance settings when EnableBalance is true
            float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor)
                : Constants.Multipliers.DefaultMassMultiplier;
            float maxMassCapacity = massCapacity * overencumbranceMultiplier;

            // Get current total mass
            float totalMass = CalculateTotalBagMass(drifterBagController);

            // Check if we're at or above 200% cap
            return totalMass >= maxMassCapacity;
        }

        // Gets current count of bagged objects
        public static int GetCurrentBaggedCount(DrifterBagController controller)
        {
            if (controller == null) return 0;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null)
            {
                return 0;
            }

            int objectsInBag = 0;
            var countedInstanceIds = new HashSet<int>();

            foreach (var obj in list)
            {
                if (obj != null && !OtherPatches.IsInProjectileState(obj))
                {
                    int instanceId = obj.GetInstanceID();
                    if (!countedInstanceIds.Contains(instanceId))
                    {
                        countedInstanceIds.Add(instanceId);
                        objectsInBag++;
                    }
                }
            }

            return objectsInBag;
        }

        // Checks if there is room for grabbing another object
        public static bool HasRoomForGrab(DrifterBagController controller)
        {
            if (controller == null) return false;

            // When UncapCapacity is enabled AND EnableBalance is true, check mass capacity instead of slot capacity
            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapCapacity.Value)
            {
                // Calculate total mass
                float totalMass = 0f;
                var list = BagPatches.GetState(controller).BaggedObjects;
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                        if (obj != null && !OtherPatches.IsInProjectileState(obj))
                        {
                            totalMass += controller.CalculateBaggedObjectMass(obj);
                        }
                    }
                }

                // Calculate mass capacity with overencumbrance limit
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
                // Only apply overencumbrance settings when EnableBalance is true
                float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                    ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor)
                    : Constants.Multipliers.DefaultMassMultiplier;
                float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                return totalMass < maxMassCapacity;
            }

            int effectiveCapacity = GetUtilityMaxStock(controller, null);
            int currentCount = GetCurrentBaggedCount(controller);
            return currentCount < effectiveCapacity;
        }

        // Gets total mass of all bagged objects
        public static float GetBaggedObjectMass(DrifterBagController controller)
        {
            if (controller == null) return 0f;

            float totalMass = 0f;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list != null)
            {
                foreach (var obj in list)
                {
                    if (obj != null && !OtherPatches.IsInProjectileState(obj))
                    {
                        totalMass += controller.CalculateBaggedObjectMass(obj);
                    }
                }
            }

            return totalMass;
        }
    }
}
