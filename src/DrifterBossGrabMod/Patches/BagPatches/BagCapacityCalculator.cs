#nullable enable
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
        // Static cached collections to avoid per-operation allocations
        private static readonly Dictionary<string, float> _capacityVarsBuffer = new Dictionary<string, float>();
        private static readonly HashSet<int> _countedInstanceIdsBuffer = new HashSet<int>();
        // Gets maximum stock (capacity) for the utility skill
        public static int GetUtilityMaxStock(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                return Constants.Limits.SingleCapacity;
            }
            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int addedSlots = Constants.ParseCapacityString(PluginConfig.Instance.AddedCapacity.Value);
                int utilityStocks = body.skillLocator.utility.maxStock;
                
                // Handle overflow case when addedSlots is int.MaxValue (INF)
                int baseSlots = addedSlots == int.MaxValue ? int.MaxValue : utilityStocks + addedSlots;

                int extraSlots = 0;

                // Add Capacity slots using formula-based scaling
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    var vars = _capacityVarsBuffer;
                    vars["H"] = body.maxHealth;
                    vars["L"] = body.level;
                    vars["C"] = body.skillLocator.utility.maxStock;
                    vars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
                    extraSlots = Balance.FormulaParser.EvaluateInt(
                        PluginConfig.Instance.SlotScalingFormula.Value, vars);
                }

                // Handle overflow case when baseSlots is int.MaxValue (INF)
                int slotCapacity = baseSlots == int.MaxValue ? int.MaxValue : baseSlots + extraSlots;

                // If overencumbrance is enabled (OverencumbranceMax > 0), check if we're at mass cap
                if (PluginConfig.Instance.OverencumbranceMax.Value > 0 && PluginConfig.Instance.EnableBalance.Value)
                {
                    // Predictive capacity calculation: Check if we WOULD be at mass cap after adding incoming object
                    int usedCapacity = GetCurrentBaggedCount(drifterBagController);
                    float totalMass = CalculateTotalBagMass(drifterBagController, incomingObject);

                    // Calculate mass capacity with overencumbrance limit
                    float massCapacity = CapacityScalingSystem.CalculateMassCapacity(drifterBagController);
                    // Only apply overencumbrance settings when EnableBalance is true
                    float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                        ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
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
            return Constants.ParseCapacityString(PluginConfig.Instance.AddedCapacity.Value);
        }

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
                    if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                    {
                        totalMass += drifterBagController.CalculateBaggedObjectMass(obj);
                    }
                }
            }

            // Include incoming object mass for predictive capacity calculation
            GameObject? predictiveIncomingObject = incomingObject;
            if (predictiveIncomingObject == null)
            {
                predictiveIncomingObject = BagPatches.GetState(drifterBagController).IncomingObject;
            }

            if (predictiveIncomingObject != null && !ProjectileRecoveryPatches.IsInProjectileState(predictiveIncomingObject))
            {
                totalMass += drifterBagController.CalculateBaggedObjectMass(predictiveIncomingObject);
            }

            return totalMass;
        }

        // Checks if bag is at or above 200% mass capacity cap
        private static bool IsAtMassCapacityCap(DrifterBagController drifterBagController)
        {
            if (drifterBagController == null) return false;

            // Calculate mass capacity with overencumbrance limit
            float massCapacity = CapacityScalingSystem.CalculateMassCapacity(drifterBagController);
            // Only apply overencumbrance settings when EnableBalance is true
            float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
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
            var countedInstanceIds = _countedInstanceIdsBuffer;
            countedInstanceIds.Clear();

            foreach (var obj in list)
            {
                if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
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

            // When AddedCapacity is INF AND EnableBalance is true, check mass capacity instead of slot capacity
            if (PluginConfig.Instance.BottomlessBagEnabled.Value && PluginConfig.Instance.IsAddedCapacityInfinite)
            {
                // Calculate total mass
                float totalMass = 0f;
                var list = BagPatches.GetState(controller).BaggedObjects;
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                    if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                        {
                            totalMass += controller.CalculateBaggedObjectMass(obj);
                        }
                    }
                }

                // Calculate mass capacity with overencumbrance limit
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
                // Only apply overencumbrance settings when EnableBalance is true
                float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                    ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
                    : Constants.Multipliers.DefaultMassMultiplier;
                float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                bool hasRoom = totalMass < maxMassCapacity;
                if (!hasRoom)
                {
                    API.DrifterBagAPI.InvokeOnBagFull(controller);
                }
                return hasRoom;
            }

            int effectiveCapacity = GetUtilityMaxStock(controller, null);
            int currentCount = GetCurrentBaggedCount(controller);
            bool hasRoomSlot = currentCount < effectiveCapacity;
            if (!hasRoomSlot)
            {
                API.DrifterBagAPI.InvokeOnBagFull(controller);
            }
            return hasRoomSlot;
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
                    if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                    {
                        totalMass += controller.CalculateBaggedObjectMass(obj);
                    }
                }
            }

            return totalMass;
        }
    }
}
