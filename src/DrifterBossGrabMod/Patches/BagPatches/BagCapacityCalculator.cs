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

        // Gets the maximum utility stock capacity for the bag
        public static int GetUtilityMaxStock(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            // If UncapCapacity is enabled AND EnableBalance is true, return a very large value (effectively unlimited slots)
            // The bag will rely purely on mass capacity instead
            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapCapacity.Value)
            {
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
                int slotCapacity = maxStock + PluginConfig.Instance.BottomlessBagBaseCapacity.Value;

                // If overencumbrance is enabled, check if we're at the 200% mass cap
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

                    // Check if we're at or would be at the 200% mass cap
                    bool isAtMassCap = totalMass >= maxMassCapacity;
                    if (isAtMassCap)
                    {
                        // Set slot capacity to used capacity (current object count) to make it "maxed out"
                        bool hasIncoming = incomingObject != null || BagPatches.GetState(drifterBagController).IncomingObject != null;
                        slotCapacity = hasIncoming ? usedCapacity + 1 : usedCapacity;
                    }
                }

                return slotCapacity;
            }
            return PluginConfig.Instance.BottomlessBagBaseCapacity.Value;
        }

        // Checks if the bag is at or above the 200% mass capacity cap
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

            // Check if we're at or above the 200% cap
            return totalMass >= maxMassCapacity;
        }

        // Gets the current count of bagged objects
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

        // Gets the total mass of all bagged objects
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
