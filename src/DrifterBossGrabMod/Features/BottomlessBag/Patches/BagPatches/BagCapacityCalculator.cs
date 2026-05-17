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
        // Prevents per-operation allocations during capacity calculations
        private static readonly Dictionary<string, float> _capacityVarsBuffer = new Dictionary<string, float>();
        private static readonly HashSet<int> _countedInstanceIdsBuffer = new HashSet<int>();
        // Gets utility max stock, accounting for AddedCapacity and slot scaling formulas
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

                // Predictive capacity: prevent overfilling by checking mass cap before grab
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
                    // Overencumbrance multiplier only applies when balance system is enabled
                    float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                        ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
                        : Constants.Multipliers.DefaultMassMultiplier;
                    float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                    // Check if we're at or would be at 200% mass cap
                    bool isAtMassCap = totalMass >= maxMassCapacity;
                    if (isAtMassCap)
                    {
                        // Enforce slot capacity to used count (minimum 1) to prevent overfilling when at mass cap
                        slotCapacity = Math.Max(1, usedCapacity);
                    }
                }

                return slotCapacity;
            }
            return Constants.ParseCapacityString(PluginConfig.Instance.AddedCapacity.Value);
        }

        // Calculates total mass including incoming object for predictive capacity checks
        public static float CalculateTotalBagMass(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            if (drifterBagController == null) return 0f;

            float totalMass = drifterBagController.baggedMass;

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

        // Gets current count of bagged objects
        public static int GetCurrentBaggedCount(DrifterBagController controller)
        {
            if (controller == null) return 0;

            // Use authoritative NetID count if possible (handles unspawned objects on clients)
            var netController = controller.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController != null)
            {
                return netController.GetTotalObjectCount();
            }

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
                float totalMass = controller.baggedMass;

                // Calculate mass capacity with overencumbrance limit
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
                // Overencumbrance multiplier only applies when balance system is enabled
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
            return controller.baggedMass;
        }
    }
}
