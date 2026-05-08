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
    // ========================================================================================
    // STATE CALCULATOR
    // ========================================================================================

    public static class StateCalculator
    {
        public static BaggedObjectStateData CalculateState(
            DrifterBagController controller,
            GameObject targetObject,
            StateCalculationMode mode)
        {
            if (mode == StateCalculationMode.Current || targetObject == null)
            {
                return GetIndividualObjectState(controller, targetObject!);
            }

            return GetAggregateState(controller);
        }

        // ========================================================================================
        // STATE RETRIEVAL
        // ========================================================================================

        public static BaggedObjectStateData GetIndividualObjectState(
            DrifterBagController controller,
            GameObject targetObject,
            BaggedObjectStateData? output = null)
        {
            if (targetObject == null)
            {
                Log.DebugIfEnabled("[STATE CREATION] GetIndividualObjectState returning empty state for null targetObject");
                return output ?? new BaggedObjectStateData();
            }

            Log.DebugIfEnabled("[GetIndividualObjectState] Checking for existing state for {0}", targetObject.name);

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

            var storedState = API.DrifterBagAPI.LoadObjectState(controller, targetObject);
            if (storedState != null)
            {
                Log.DebugIfEnabled("[STATE REUSE] GetIndividualObjectState reusing stored state for {0}: baseMaxHealth={1}", targetObject.name, storedState.baseMaxHealth);
                if (output != null)
                {
                    storedState.CopyTo(output);
                    state = output;
                }
                else
                {
                    state = storedState;
                }
            }
            else
            {
                Log.DebugIfEnabled("[STATE CREATION] GetIndividualObjectState creating new state for {0}", targetObject.name);
                state = output ?? new BaggedObjectStateData();
                state.CalculateFromObject(targetObject, controller);

                API.DrifterBagAPI.SaveObjectState(controller, targetObject, state);
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

        public static BaggedObjectStateData GetAggregateState(
            DrifterBagController controller,
            BaggedObjectStateData? output = null)
        {
            var baggedObjects = API.DrifterBagAPI.GetBaggedObjects(controller);
            if (baggedObjects == null)
            {
                Log.DebugIfEnabled("[STATE CREATION] GetAggregateState returning empty state - baggedObjects is null");
                return output ?? new BaggedObjectStateData();
            }

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
                    var msStoredState = API.DrifterBagAPI.LoadObjectState(controller, currentBaggedObject.targetObject);
                    if (msStoredState != null && msStoredState.elapsedBreakoutTime > preservedElapsedBreakoutTime)
                    {
                        preservedElapsedBreakoutTime = msStoredState.elapsedBreakoutTime;
                    }
                }
            }

            var aggregateState = output ?? new BaggedObjectStateData();

            var mainPassenger = API.DrifterBagAPI.GetMainPassenger(controller);
            if (mainPassenger == null)
            {
                mainPassenger = API.DrifterBagAPI.GetMainSeatOccupant(controller);
            }

            if (mainPassenger != null)
            {
                var storedMainState = API.DrifterBagAPI.LoadObjectState(controller, mainPassenger);
                if (storedMainState != null)
                {
                    Log.DebugIfEnabled("[GetAggregateState] Using stored state for main passenger {0}: level={1}", mainPassenger.name, storedMainState.level);

                    aggregateState.targetObject = storedMainState.targetObject ?? mainPassenger;
                    aggregateState.targetBody = storedMainState.targetBody;
                    aggregateState.isBody = storedMainState.isBody;
                    aggregateState.vehiclePassengerAttributes = storedMainState.vehiclePassengerAttributes;

                    aggregateState.baseMaxHealth = storedMainState.baseMaxHealth;
                    aggregateState.baseRegen = storedMainState.baseRegen;
                    aggregateState.baseMaxShield = storedMainState.baseMaxShield;
                    aggregateState.baseMoveSpeed = storedMainState.baseMoveSpeed;
                    aggregateState.baseDamage = storedMainState.baseDamage;
                    aggregateState.baseAttackSpeed = storedMainState.baseAttackSpeed;
                    aggregateState.baseArmor = storedMainState.baseArmor;
                    aggregateState.baseCrit = storedMainState.baseCrit;
                    aggregateState.level = storedMainState.level;
                    aggregateState.experience = storedMainState.experience;
                    aggregateState.teamIndex = storedMainState.teamIndex;
                    aggregateState.isElite = storedMainState.isElite;
                    aggregateState.bodyFlags = storedMainState.bodyFlags;
                    aggregateState.subtitleNameToken = storedMainState.subtitleNameToken;
                    aggregateState.skinIndex = storedMainState.skinIndex;
                }
                else
                {
                    Log.DebugIfEnabled("[GetAggregateState] No stored state for {0}, calculating from object", mainPassenger.name);
                    aggregateState.CalculateFromObject(mainPassenger, controller);
                }
            }
            else
            {
                aggregateState.targetObject = null;
            }

            aggregateState.baggedMass = controller.baggedMass;

            float totalDamage = 0f, totalAttackSpeed = 0f, totalCrit = 0f, totalMoveSpeed = 0f;
            int totalJunkCount = 0;
            int statObjectCount = 0;

            foreach (var obj in baggedObjects)
            {
                if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                {
                    var objState = API.DrifterBagAPI.LoadObjectState(controller, obj);
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
            else
            {
                // Reset stats if no objects contribute to them
                aggregateState.damageStat = 0f;
                aggregateState.attackSpeedStat = 0f;
                aggregateState.critStat = 0f;
                aggregateState.moveSpeedStat = 0f;
                aggregateState.junkSpawnCount = 0;
            }

            aggregateState.movespeedPenalty = CalculateMovespeedPenalty(
                controller, aggregateState.baggedMass);

            var mainSeatObj = API.DrifterBagAPI.GetMainPassenger(controller);
            if (mainSeatObj != null)
            {
                aggregateState.targetObject = mainSeatObj;
                var healthComp = mainSeatObj.GetComponent<HealthComponent>();
                aggregateState.targetBody = healthComp?.body;
                aggregateState.isBody = healthComp != null;
                aggregateState.vehiclePassengerAttributes = mainSeatObj.GetComponent<SpecialObjectAttributes>();
            }

            aggregateState.bagScale01 = CalculateBagScale01(controller, aggregateState.baggedMass);

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

        // ========================================================================================
        // PENALTY CALCULATIONS
        // ========================================================================================

        private static readonly Dictionary<string, float> _penaltyVarsBuffer = new Dictionary<string, float>();
        public static Dictionary<string, float> PenaltyVarsBuffer => _penaltyVarsBuffer;

        public static void UpdatePenaltyVarsBuffer(float totalMass, float massCapacity, float totalCapacity, float health, float level, float massCap)
        {
            _penaltyVarsBuffer.Clear();
            _penaltyVarsBuffer["T"] = totalMass;
            _penaltyVarsBuffer["M"] = massCapacity;
            _penaltyVarsBuffer["C"] = totalCapacity;
            _penaltyVarsBuffer["H"] = health;
            _penaltyVarsBuffer["L"] = level;
            _penaltyVarsBuffer["MC"] = massCap;
            _penaltyVarsBuffer["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
        }

        public static float CalculateMovespeedPenalty(
            DrifterBagController controller,
            float totalMass)
        {
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

                float massCap = 700f;
                string massCapStr = PluginConfig.Instance.MassCap.Value;
                if (string.Equals(massCapStr, "INF", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(massCapStr, "Infinity", StringComparison.OrdinalIgnoreCase))
                {
                    massCap = float.MaxValue;
                }
                else if (!float.TryParse(massCapStr, out massCap))
                {
                    massCap = 700f;
                }

                UpdatePenaltyVarsBuffer(totalMass, massCapacity, totalCapacity, health, level, massCap);
                penalty = FormulaParser.Evaluate(PluginConfig.Instance.MovespeedPenaltyFormula.Value, _penaltyVarsBuffer);
            }

            return penalty;
        }

        // ========================================================================================
        // VISUAL SCALING
        // ========================================================================================

        public static float CalculateBagScale01(DrifterBagController controller, float mass)
        {
            float maxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;
            float value = mass;
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
