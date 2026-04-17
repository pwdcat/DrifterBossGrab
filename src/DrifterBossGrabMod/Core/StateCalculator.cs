#nullable enable
using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;
using EntityStates;
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
            float preservedElapsedBreakoutTime = 0f;
            bool shouldPreserve = false;

                var currentBaggedObject = GetCurrentBaggedObjectState(controller);
                if (currentBaggedObject != null && currentBaggedObject.targetObject == targetObject)
                {
                    shouldPreserve = true;

                    if (ReflectionCache.BaggedObject.BreakoutTime != null)
                    {
                        preservedBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(currentBaggedObject);
                    }
                    if (ReflectionCache.BaggedObject.BreakoutAttempts != null)
                    {
                        preservedBreakoutAttempts = (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(currentBaggedObject);
                    }
                    if (ReflectionCache.EntityState.FixedAge != null)
                    {
                        preservedElapsedBreakoutTime = (float)ReflectionCache.EntityState.FixedAge.GetValue(currentBaggedObject);
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
                if (preservedBreakoutTime > 0f || state.breakoutTime == 0f)
                {
                    state.breakoutTime = preservedBreakoutTime;
                }
                if (preservedBreakoutAttempts > 0f || state.breakoutAttempts == 0f)
                {
                    state.breakoutAttempts = preservedBreakoutAttempts;
                }
                
                if (preservedElapsedBreakoutTime > state.elapsedBreakoutTime)
                {
                    state.elapsedBreakoutTime = preservedElapsedBreakoutTime;
                }
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
            float preservedElapsedBreakoutTime = 0f;
            var currentBaggedObject = GetCurrentBaggedObjectState(controller);
            if (currentBaggedObject != null)
            {
                if (ReflectionCache.BaggedObject.BreakoutTime != null)
                {
                    preservedBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(currentBaggedObject);
                }
                if (ReflectionCache.BaggedObject.BreakoutAttempts != null)
                {
                    preservedBreakoutAttempts = (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(currentBaggedObject);
                }
                if (ReflectionCache.EntityState.FixedAge != null)
                {
                    preservedElapsedBreakoutTime = (float)ReflectionCache.EntityState.FixedAge.GetValue(currentBaggedObject);
                }

                if (currentBaggedObject.targetObject != null)
                {
                    var msStoredState = BaggedObjectPatches.LoadObjectState(controller, currentBaggedObject.targetObject);
                    if (msStoredState != null && msStoredState.elapsedBreakoutTime > preservedElapsedBreakoutTime)
                    {
                        preservedElapsedBreakoutTime = msStoredState.elapsedBreakoutTime;
                    }
                }
            }

            var aggregateState = new BaggedObjectStateData();

            // Set targetObject to the current main passenger if available, to avoid 'null' references in logs
            var mainPassenger = BaggedObjectPatches.GetMainSeatOccupant(controller);
            if (mainPassenger != null)
            {
                aggregateState.targetObject = mainPassenger;
            }

            // Aggregate mass with multiplier
            float totalMass = 0f;
            int validObjectCount = 0;

            foreach (var obj in baggedObjects)
            {
                if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
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

            aggregateState.baggedMass = totalMass;

            // Aggregate stats with multiplier
            float totalDamage = 0f, totalAttackSpeed = 0f, totalCrit = 0f, totalMoveSpeed = 0f;
            int totalJunkCount = 0;
            int statObjectCount = 0;

            foreach (var obj in baggedObjects)
            {
                if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                {
                    var objState = BaggedObjectPatches.LoadObjectState(controller, obj);
                    if (objState != null)
                    {
                        totalDamage += objState.damageStat;
                        totalAttackSpeed += objState.attackSpeedStat;
                        totalCrit += objState.critStat;
                        totalMoveSpeed += objState.moveSpeedStat;
                        totalJunkCount += objState.junkSpawnCount;
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
                aggregateState.junkSpawnCount = totalJunkCount;
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
            if (preservedBreakoutTime > 0f || aggregateState.breakoutTime == 0f)
            {
                aggregateState.breakoutTime = preservedBreakoutTime;
            }
            if (preservedBreakoutAttempts > 0f || aggregateState.breakoutAttempts == 0f)
            {
                aggregateState.breakoutAttempts = preservedBreakoutAttempts;
            }
            aggregateState.elapsedBreakoutTime = preservedElapsedBreakoutTime;

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
            // Use formula-based penalty calculation when EnableBalance is true
            float penalty = 0f;
            if (PluginConfig.Instance.EnableBalance.Value)
            {
                var body = controller.GetComponent<CharacterBody>();
                float health = body != null ? body.maxHealth : 0f;
                float level = body != null ? body.level : 1f;
                float stocks = body != null && body.skillLocator != null && body.skillLocator.utility != null
                    ? body.skillLocator.utility.maxStock : 1f;
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
                float totalCapacity = CapacityScalingSystem.GetTotalCapacity(controller);

                // Parse MassCap value (supports "INF" or "Infinity" for unlimited)
                float massCap = 700f; // Default value
                string massCapStr = PluginConfig.Instance.MassCap.Value;
                if (string.Equals(massCapStr, "INF", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(massCapStr, "Infinity", StringComparison.OrdinalIgnoreCase))
                {
                    massCap = float.MaxValue;
                }
                else if (!float.TryParse(massCapStr, out massCap))
                {
                    massCap = 700f; // Fallback to default if parsing fails
                }

                var penaltyVars = new Dictionary<string, float>
                {
                    ["T"] = totalMass,
                    ["M"] = massCapacity,
                    ["C"] = totalCapacity,
                    ["H"] = health,
                    ["L"] = level,
                    ["MC"] = massCap,
                    ["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1
                };

                penalty = FormulaParser.Evaluate(PluginConfig.Instance.MovespeedPenaltyFormula.Value, penaltyVars);
            }

            return penalty;
        }

        // Calculates the bag scale (0-1 range) from mass
        // controller: The DrifterBagController instance
        // mass: The mass to calculate scale for
        // Returns: The calculated bag scale (0-1 range)
        public static float CalculateBagScale01(DrifterBagController controller, float mass)
        {
            float maxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;
            float value = mass;
            // Only apply BagScaleCap when EnableBalance is true
            if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.IsBagScaleCapInfinite)
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
