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

        public static BaggedObjectStateData GetIndividualObjectState(
            DrifterBagController controller,
            GameObject targetObject)
        {
            if (targetObject == null)
            {
                return new BaggedObjectStateData();
            }

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
                if (ReflectionCache.BaggedObject.BreakoutTimer != null)
                {
                    preservedElapsedBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTimer.GetValue(currentBaggedObject);
                }
            }

            var additionalTimer = targetObject.GetComponent<Patches.AdditionalSeatBreakoutTimer>();
            if (additionalTimer != null && additionalTimer.isActiveAndEnabled &&
                BagHelpers.GetAdditionalSeat(controller, targetObject) != null)
            {
                shouldPreserve = true;
                preservedBreakoutTime = additionalTimer.breakoutTime;
                preservedBreakoutAttempts = additionalTimer.breakoutAttempts;
                preservedElapsedBreakoutTime = additionalTimer.GetElapsedBreakoutTime();
            }

            BaggedObjectStateData state;

            var storedState = BaggedObjectPatches.LoadObjectState(controller, targetObject);
            if (storedState != null)
            {
                state = storedState;
            }
            else
            {
                state = new BaggedObjectStateData();
                state.CalculateFromObject(targetObject, controller);
            }

            if (shouldPreserve)
            {
                if (preservedBreakoutTime > 0f || state.breakoutTime == 0f)
                {
                    state.breakoutTime = preservedBreakoutTime;
                }
                state.breakoutAttempts = preservedBreakoutAttempts;

                state.elapsedBreakoutTime = preservedElapsedBreakoutTime;
            }

            return state;
        }

        public static BaggedObjectStateData GetAggregateState(
            DrifterBagController controller)
        {
            var baggedObjects = BagPatches.GetState(controller).BaggedObjects;
            if (baggedObjects == null)
            {
                return new BaggedObjectStateData();
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
                if (ReflectionCache.BaggedObject.BreakoutTimer != null)
                {
                    preservedElapsedBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTimer.GetValue(currentBaggedObject);
                }
            }

            var aggregateState = new BaggedObjectStateData();

            var mainPassenger = BagPatches.GetMainSeatObject(controller);
            if (mainPassenger == null)
            {
                mainPassenger = BaggedObjectPatches.GetMainSeatOccupant(controller);
            }

            if (mainPassenger != null)
            {
                var storedMainState = BaggedObjectPatches.LoadObjectState(controller, mainPassenger);
                if (storedMainState != null)
                {

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

            aggregateState.movespeedPenalty = CalculateMovespeedPenalty(
                controller, aggregateState.baggedMass);

            var mainSeatObj = BagPatches.GetMainSeatObject(controller);
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

                float massCap = PluginConfig.Instance.ParsedMassCap;

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

        public static float CalculateBreakoutTimeMultiplier(DrifterBagController? controller = null, GameObject? passenger = null, float baseTime = 0f)
        {
            var cfg = PluginConfig.Instance;
            return Balance.BagFormulaContext.EvaluatePositive(cfg.BreakoutTimeFormula.Value, 1.0f,
                controller != null ? controller.GetComponent<CharacterBody>() : null, controller, passenger, baseTime);
        }

        public static float CalculateGrabRangeMultiplier(CharacterBody body)
        {
            return Balance.BagFormulaContext.EvaluatePositive(PluginConfig.Instance.GrabRangeFormula.Value,
                1.0f, body, body.GetComponent<DrifterBagController>(), includeFlags: false);
        }

        public static int CalculateMaxSmacks(DrifterBagController controller)
        {
            var formula = PluginConfig.Instance.MaxSmacksFormula.Value;
            if (string.IsNullOrWhiteSpace(formula)) return 3;
            var carrier = controller.GetComponent<CharacterBody>();
            var variables = Balance.FormulaRegistry.GetVariables(carrier,
                Balance.BagFormulaContext.Create(carrier, controller, BagPatches.GetMainSeatObject(controller)));
            if (!Balance.FormulaParser.TryPreview(formula, variables, out float value, out _))
                return 3;
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            return (int)Mathf.Floor(value);
        }

        public static float CalculateBagScale01(DrifterBagController controller, float mass)
        {
            float maxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;
            if (float.IsNaN(mass)) return 0.5f;
            float reference = GetBagScaleReferenceMass(maxCapacity);
            float ratio = (mass - 1f) / Math.Max(reference - 1f, 0.0001f);
            return 0.5f + 0.5f * Math.Max(0f, Math.Min(1f, ratio));
        }

        internal static float CalculateBagExtraScale(float mass, float capacity, float cap)
        {
            float reference = GetBagScaleReferenceMass(capacity);
            if (float.IsNaN(mass) || float.IsInfinity(mass) || mass <= reference) return 1f;
            float scale = 1f + (mass - reference) / Math.Max(reference - 1f, 1f);
            if (float.IsNaN(cap)) return 1f;
            return Math.Max(1f, Math.Min(scale, cap));
        }

        private static float GetBagScaleReferenceMass(float capacity)
        {
            return float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity >= float.MaxValue || capacity <= 0f
                ? Constants.Limits.MaxMass : capacity;
        }

        public static float GetMassScaleRatio(float value, float maxCapacity)
        {
            float denom = Mathf.Max(maxCapacity - 1f, 0.0001f);
            return (value - 1f) / denom;
        }
    }
}
