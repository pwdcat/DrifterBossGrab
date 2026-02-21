using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Core
{
    // Utility class for calculating BaggedObject state in both Current and All modes
    public static class StateCalculator
    {
        // controller: The DrifterBagController
        // targetObject: The target GameObject (main seat object)
        // mode: The calculation mode (Current or All)
        // Returns: The calculated BaggedObjectStateData
        public static BaggedObjectStateData CalculateState(
            DrifterBagController controller,
            GameObject targetObject,
            StateCalculationMode mode)
        {
            if (mode == StateCalculationMode.Current || targetObject == null)
            {
                // Return individual object's state
                return GetIndividualObjectState(controller, targetObject!);
            }

            // All Mode: Aggregate across all bagged objects
            return GetAggregateState(controller);
        }

        // Gets the individual object's state
        // controller: The DrifterBagController
        // targetObject: The target GameObject
        // Returns: The individual object's BaggedObjectStateData
        public static BaggedObjectStateData GetIndividualObjectState(
            DrifterBagController controller,
            GameObject targetObject)
        {
            if (targetObject == null)
            {
                return new BaggedObjectStateData();
            }

            // Breakout data from current BaggedObject state before calculating new state
            float preservedBreakoutTime = 0f;
            float preservedBreakoutAttempts = 0f;
            bool shouldPreserve = false;

            var currentBaggedObject = GetCurrentBaggedObjectState(controller);
            if (currentBaggedObject != null && currentBaggedObject.targetObject == targetObject)
            {
                shouldPreserve = true;
                var breakoutTimeField = AccessTools.Field(typeof(BaggedObject), "breakoutTime");
                var breakoutAttemptsField = AccessTools.Field(typeof(BaggedObject), "breakoutAttempts");

                if (breakoutTimeField != null)
                {
                    preservedBreakoutTime = (float)breakoutTimeField.GetValue(currentBaggedObject);
                }
                if (breakoutAttemptsField != null)
                {
                    preservedBreakoutAttempts = (float)breakoutAttemptsField.GetValue(currentBaggedObject);
                }
            }

            BaggedObjectStateData state;

            // Load stored state or calculate fresh
            var storedState = BaggedObjectPatches.LoadObjectState(controller, targetObject);
            if (storedState != null)
            {
                state = storedState;
            }
            else
            {
                // Calculate fresh state for new object
                state = new BaggedObjectStateData();
                state.CalculateFromObject(targetObject, controller);
            }

            if (shouldPreserve)
            {
                state.breakoutTime = preservedBreakoutTime;
                state.breakoutAttempts = preservedBreakoutAttempts;
            }

            return state;
        }

        // All mode
        // controller: The DrifterBagController
        // Returns: The aggregated BaggedObjectStateData
        public static BaggedObjectStateData GetAggregateState(
            DrifterBagController controller)
        {
            var baggedObjects = BagPatches.GetState(controller).BaggedObjects;
            if (baggedObjects == null)
            {
                return new BaggedObjectStateData(); // Empty state
            }

            // Breakout data from current BaggedObject state before calculating new state
            float preservedBreakoutTime = 0f;
            float preservedBreakoutAttempts = 0f;
            var currentBaggedObject = GetCurrentBaggedObjectState(controller);
            if (currentBaggedObject != null)
            {
                var breakoutTimeField = AccessTools.Field(typeof(BaggedObject), "breakoutTime");
                var breakoutAttemptsField = AccessTools.Field(typeof(BaggedObject), "breakoutAttempts");

                if (breakoutTimeField != null)
                {
                    preservedBreakoutTime = (float)breakoutTimeField.GetValue(currentBaggedObject);
                }
                if (breakoutAttemptsField != null)
                {
                    preservedBreakoutAttempts = (float)breakoutAttemptsField.GetValue(currentBaggedObject);
                }
            }

            var aggregateState = new BaggedObjectStateData();

            // Aggregate mass with multiplier
            float totalMass = 0f;
            int validObjectCount = 0;

            foreach (var obj in baggedObjects)
            {
                if (obj != null && !OtherPatches.IsInProjectileState(obj))
                {
                    var objState = BaggedObjectPatches.LoadObjectState(controller, obj);
                    if (objState != null)
                    {
                        totalMass += objState.baggedMass;
                        validObjectCount++;
                    }
                    else
                    {
                        // Calculate on-the-fly if not stored
                        totalMass += controller.CalculateBaggedObjectMass(obj);
                        validObjectCount++;
                    }
                }
            }

            // Apply mass multiplier (1.0 = sum, 0.5 = half of sum, etc.)
            float massMultiplier = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.AllModeMassMultiplier.Value : 1.0f;
            aggregateState.baggedMass = totalMass * massMultiplier;

            // Aggregate stats with multiplier
            float totalDamage = 0f, totalAttackSpeed = 0f, totalCrit = 0f, totalMoveSpeed = 0f;
            int statObjectCount = 0;

            foreach (var obj in baggedObjects)
            {
                if (obj != null && !OtherPatches.IsInProjectileState(obj))
                {
                    var objState = BaggedObjectPatches.LoadObjectState(controller, obj);
                    if (objState != null)
                    {
                        totalDamage += objState.damageStat;
                        totalAttackSpeed += objState.attackSpeedStat;
                        totalCrit += objState.critStat;
                        totalMoveSpeed += objState.moveSpeedStat;
                        statObjectCount++;
                    }
                }
            }

            if (statObjectCount > 0)
            {
                aggregateState.damageStat = totalDamage / statObjectCount;
                aggregateState.attackSpeedStat = totalAttackSpeed / statObjectCount;
                aggregateState.critStat = totalCrit / statObjectCount;
                aggregateState.moveSpeedStat = totalMoveSpeed / statObjectCount;
            }

            // Calculate movement penalty from total mass
            aggregateState.movespeedPenalty = CalculateMovespeedPenalty(
                controller, aggregateState.baggedMass);

            // Set target references to main seat object (if exists)
            var mainSeatObj = BagPatches.GetMainSeatObject(controller);
            if (mainSeatObj != null)
            {
                aggregateState.targetObject = mainSeatObj;
                var healthComp = mainSeatObj.GetComponent<HealthComponent>();
                aggregateState.targetBody = healthComp?.body;
                aggregateState.isBody = healthComp != null;
                aggregateState.vehiclePassengerAttributes = mainSeatObj.GetComponent<SpecialObjectAttributes>();
            }

            // Calculate bag scale from aggregated mass
            aggregateState.bagScale01 = CalculateBagScale01(controller, aggregateState.baggedMass);

            // Restore breakout data to prevent immediate breakout
            aggregateState.breakoutTime = preservedBreakoutTime;
            aggregateState.breakoutAttempts = preservedBreakoutAttempts;

            return aggregateState;
        }

        // Gets the current active BaggedObject state
        // controller: The DrifterBagController
        // Returns: The current BaggedObject state, or null if not found
        private static BaggedObject? GetCurrentBaggedObjectState(DrifterBagController controller)
        {
            if (controller == null) return null;

            var stateMachines = controller.GetComponentsInChildren<RoR2.EntityStateMachine>(true);
            foreach (var sm in stateMachines)
            {
                if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                {
                    return (BaggedObject)sm.state;
                }
            }
            return null;
        }

        // Calculates the movement penalty based on total mass
        // controller: The DrifterBagController instance
        // totalMass: The total mass to calculate penalty for
        // Returns: The calculated movement penalty
        public static float CalculateMovespeedPenalty(
            DrifterBagController controller,
            float totalMass)
        {
            // Use existing penalty calculation logic
            // Only apply balance penalty settings when EnableBalance is true
            var minPenalty = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.MinMovespeedPenalty.Value : 0f;
            var maxPenalty = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.MaxMovespeedPenalty.Value : 0f;
            var finalLimit = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.FinalMovespeedPenaltyLimit.Value : 0f;
            // Calculate penalty based on mass (simplified version of existing logic)
            float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
            float massRatio = Mathf.Clamp01(totalMass / massCapacity);
            float penalty = Mathf.Lerp(minPenalty, maxPenalty, massRatio);

            // Clamp to final limit
            return Mathf.Min(penalty, finalLimit);
        }

        // Calculates the bag scale (0-1 range) from mass
        // controller: The DrifterBagController instance
        // mass: The mass to calculate scale for
        // Returns: The calculated bag scale (0-1 range)
        public static float CalculateBagScale01(DrifterBagController controller, float mass)
        {
            float maxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;
            float value = mass;
            // Only apply UncapBagScale when EnableBalance is true
            if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.UncapBagScale.Value)
            {
                value = Mathf.Clamp(mass, 1f, maxCapacity);
            }
            else
            {
                value = Mathf.Max(mass, 1f);
            }

            float t = (value - 1f) / (maxCapacity - 1f);
            return 0.5f + 0.5f * t;
        }
    }
}
