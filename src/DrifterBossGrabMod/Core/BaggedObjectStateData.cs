#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Balance;
using EntityStates.Drifter.Bag;
using EntityStates;

namespace DrifterBossGrabMod.Core
{
    public class BaggedObjectStateData
    {
        private static readonly FieldInfo _targetBodyField = ReflectionCache.BaggedObject.TargetBody;
        private static readonly FieldInfo _isBodyField = ReflectionCache.BaggedObject.IsBody;
        private static readonly FieldInfo _vehiclePassengerAttributesField = ReflectionCache.BaggedObject.VehiclePassengerAttributes;
        private static readonly FieldInfo _baggedMassField = ReflectionCache.BaggedObject.BaggedMass;
        private static readonly FieldInfo _bagScale01Field = ReflectionCache.BaggedObject.BagScale01;
        private static readonly FieldInfo _movespeedPenaltyField = ReflectionCache.BaggedObjectAdditional.MovespeedPenalty;
        private static readonly FieldInfo _attackSpeedStatField = ReflectionCache.BaggedObjectAdditional.AttackSpeedStat!;
        private static readonly FieldInfo _damageStatField = ReflectionCache.BaggedObjectAdditional.DamageStat!;
        private static readonly FieldInfo _critStatField = ReflectionCache.BaggedObjectAdditional.CritStat!;
        private static readonly FieldInfo _moveSpeedStatField = ReflectionCache.BaggedObjectAdditional.MoveSpeedStat!;

        public CharacterBody? targetBody;
        public GameObject? targetObject;
        public bool isBody;

        public float baggedMass;
        public float bagScale01;
        public float movespeedPenalty;

        public float attackSpeedStat;
        public float damageStat;
        public float critStat;
        public float moveSpeedStat;
        public float armorStat;
        public float regenStat;
        public float baseMaxHealth;
        public float baseRegen;
        public float baseMaxShield;
        public float baseMoveSpeed;
        public float baseDamage;
        public float baseAttackSpeed;
        public float baseArmor;
        public float baseCrit;
        public float level;
        public float experience;
        public uint teamIndex;
        public bool isElite;
        public CharacterBody.BodyFlags bodyFlags;
        public string? subtitleNameToken;
        public uint skinIndex;

        public int junkSpawnCount;
        public float slamDamageCoefficient;

        public float breakoutTime = 10f;
        public float breakoutAttempts = 0f;
        public float elapsedBreakoutTime = 0f;

        public bool hasCapturedModelTransformState = false;
        public SpecialObjectAttributes? vehiclePassengerAttributes;

        public void CaptureFromBaggedObject(BaggedObject state)
        {
            if (state == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot capture from null BaggedObject");
                return;
            }

            try
            {
                targetBody = (CharacterBody?)_targetBodyField?.GetValue(state);
                isBody = _isBodyField != null ? (bool)_isBodyField.GetValue(state) : false;
                vehiclePassengerAttributes = (SpecialObjectAttributes?)_vehiclePassengerAttributesField?.GetValue(state);
                targetObject = state.targetObject;

                baggedMass = _baggedMassField != null ? (float)_baggedMassField.GetValue(state) : 0f;
                bagScale01 = _bagScale01Field != null ? (float)_bagScale01Field.GetValue(state) : 0.5f;
                movespeedPenalty = _movespeedPenaltyField != null ? (float)_movespeedPenaltyField.GetValue(state) : 0f;

                attackSpeedStat = _attackSpeedStatField != null ? (float)_attackSpeedStatField.GetValue(state) : 1f;
                damageStat = _damageStatField != null ? (float)_damageStatField.GetValue(state) : 0f;
                critStat = _critStatField != null ? (float)_critStatField.GetValue(state) : 0f;
                moveSpeedStat = _moveSpeedStatField != null ? (float)_moveSpeedStatField.GetValue(state) : 0f;

                if (targetBody != null)
                {
                    armorStat = targetBody.armor;
                    regenStat = targetBody.regen;

                    baseMaxHealth = targetBody.baseMaxHealth;
                    baseRegen = targetBody.baseRegen;
                    baseMaxShield = targetBody.baseMaxShield;
                    baseMoveSpeed = targetBody.baseMoveSpeed;
                    baseDamage = targetBody.baseDamage;
                    baseAttackSpeed = targetBody.baseAttackSpeed;
                    baseArmor = targetBody.baseArmor;
                    baseCrit = targetBody.baseCrit;
                    level = 0f;
                    experience = targetBody.experience;
                    teamIndex = (uint)targetBody.teamComponent.teamIndex;
                    isElite = targetBody.isElite;
                    bodyFlags = targetBody.bodyFlags;
                    subtitleNameToken = targetBody.subtitleNameToken;
                    skinIndex = targetBody.skinIndex;
                }

                if (ReflectionCache.BaggedObject.BreakoutTime != null) breakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(state);

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null) breakoutAttempts = (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(state);

                if (ReflectionCache.EntityState.FixedAge != null)
                {
                    elapsedBreakoutTime = (float)ReflectionCache.EntityState.FixedAge.GetValue(state);
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectStateData] Captured state for {targetObject?.name ?? "null"}: " +
                            $"mass={baggedMass}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                            $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error capturing from BaggedObject: {ex.Message}");
            }
        }

        public void CaptureBreakoutStateFromBaggedObject(BaggedObject state)
        {
            if (state == null) return;

            try
            {
                if (ReflectionCache.BaggedObject.BreakoutTime != null) breakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(state);

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null) breakoutAttempts = (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(state);

                if (ReflectionCache.EntityState.FixedAge != null)
                {
                    elapsedBreakoutTime = (float)ReflectionCache.EntityState.FixedAge.GetValue(state);
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectStateData] Captured breakout state for {targetObject?.name ?? "null"}: " +
                            $"age={elapsedBreakoutTime}, breakoutTime={breakoutTime}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error capturing breakout state from BaggedObject: {ex.Message}");
            }
        }

        public void ApplyToBaggedObject(BaggedObject state)
        {
            if (state == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot apply to null BaggedObject");
                return;
            }

            // Detect and prevent applying uninitialized "stub" states which would zero out a functional object
            if (this.targetObject == null && this.baggedMass == 0f)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info("[BaggedObjectStateData] Skipping application of uninitialized 'stub' state (targetObject is null).");
                return;
            }

            try
            {
                _targetBodyField?.SetValue(state, targetBody);
                _isBodyField?.SetValue(state, isBody);
                _vehiclePassengerAttributesField?.SetValue(state, vehiclePassengerAttributes);
                state.targetObject = targetObject;

                _baggedMassField?.SetValue(state, baggedMass);
                _bagScale01Field?.SetValue(state, bagScale01);
                _movespeedPenaltyField?.SetValue(state, movespeedPenalty);

                _attackSpeedStatField?.SetValue(state, attackSpeedStat);
                _damageStatField?.SetValue(state, damageStat);
                _critStatField?.SetValue(state, critStat);
                _moveSpeedStatField?.SetValue(state, moveSpeedStat);

                if (ReflectionCache.BaggedObject.BreakoutTime != null) ReflectionCache.BaggedObject.BreakoutTime.SetValue(state, breakoutTime);

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null) ReflectionCache.BaggedObject.BreakoutAttempts.SetValue(state, breakoutAttempts);

                if (ReflectionCache.EntityState.FixedAge != null)
                {
                    ReflectionCache.EntityState.FixedAge.SetValue(state, elapsedBreakoutTime);
                }

                if (targetBody != null)
                {
                    ApplyToCharacterBody(targetBody);
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string targetName = this.targetObject != null ? this.targetObject.name : (state.targetObject != null ? state.targetObject.name : "null");
                    Log.Info($"[BaggedObjectStateData] Applied state to {targetName}: " +
                            $"mass={baggedMass}, age={elapsedBreakoutTime}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                            $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}, " +
                            $"level={level}, isElite={isElite}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to BaggedObject: {ex.Message}");
            }
        }

        public void ApplyToCharacterBody(CharacterBody body)
        {
            if (body == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[ApplyToCharacterBody] ENTRY: body.name={body.name}, body.baseMaxHealth={body.baseMaxHealth}, state.baseMaxHealth={baseMaxHealth}");

            if (baseMaxHealth <= 0)
            {
                Log.Warning($"[ApplyToCharacterBody] ABORTED: Attempting to apply INVALID baseMaxHealth={baseMaxHealth} to {body.name}. This would have killed the object. State state is likely uninitialized.");
                return; // CRITICAL SAFETY: Do not apply zero/negative health to a living body
            }

            try
            {
                body.baseMaxHealth = baseMaxHealth;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ApplyToCharacterBody] AFTER SET: body.baseMaxHealth={body.baseMaxHealth}");
                body.baseRegen = baseRegen;
                body.baseMaxShield = baseMaxShield;
                body.baseMoveSpeed = baseMoveSpeed;
                body.baseDamage = baseDamage;
                body.baseAttackSpeed = baseAttackSpeed;
                body.baseArmor = baseArmor;
                body.baseCrit = baseCrit;
                // Don't override level - let's game's level system manage it naturally
                // body.level = level;
                body.experience = experience;
                body.teamComponent.teamIndex = (TeamIndex)teamIndex;
                body.bodyFlags = bodyFlags;
                body.subtitleNameToken = subtitleNameToken ?? body.subtitleNameToken;
                body.skinIndex = skinIndex;

                body.RecalculateStats();

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ApplyToCharacterBody] Captured stats: baseMaxHealth={baseMaxHealth}, baseRegen={baseRegen}, baseDamage={baseDamage}, level={level}");
                }

                if (baseMaxHealth <= 0)
                {
                    Log.Error($"[ApplyToCharacterBody] CRITICAL: Attempting to apply INVALID baseMaxHealth={baseMaxHealth} to {body.name}! This will kill the object!");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to CharacterBody: {ex.Message}");
            }
        }

        public void CalculateFromObject(GameObject targetObject, DrifterBagController controller)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                var stackTrace = new System.Diagnostics.StackTrace();
                var callerMethod = stackTrace.GetFrame(1).GetMethod();
                Log.Info($"[CalculateFromObject] ENTRY: targetObject={targetObject?.name ?? "null"}, caller={callerMethod?.DeclaringType?.Name}.{callerMethod?.Name}");

                // Log partial stack trace to identify high-level triggers
                string traceSnippet = "";
                for (int i = 1; i < Math.Min(stackTrace.FrameCount, 5); i++)
                {
                    var frame = stackTrace.GetFrame(i).GetMethod();
                    traceSnippet += $" -> {frame.DeclaringType?.Name}.{frame.Name}";
                }
                Log.Info($"[CalculateFromObject] CALL STACK: {traceSnippet}");
            }

            if (targetObject == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot calculate from null targetObject");
                return;
            }

            if (controller == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot calculate from null controller");
                return;
            }

            try
            {
                this.targetObject = targetObject;
                HealthComponent healthComponent = targetObject.GetComponent<HealthComponent>();
                targetBody = targetObject.GetComponent<CharacterBody>();
                isBody = healthComponent != null;
                vehiclePassengerAttributes = targetObject.GetComponent<SpecialObjectAttributes>();

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CalculateFromObject] targetObject={targetObject.name}, targetBody={(targetBody != null ? targetBody.name : "null")}, healthComponent={(healthComponent != null ? "exists" : "null")}");
                    if (healthComponent != null && healthComponent.body != null)
                        Log.Info($"[CalculateFromObject] healthComponent.body={healthComponent.body.name}, healthComponent.body.baseMaxHealth={healthComponent.body.baseMaxHealth}");
                    if (targetBody != null)
                        Log.Info($"[CalculateFromObject] targetBody.baseMaxHealth={targetBody.baseMaxHealth}");
                }

                baggedMass = controller.CalculateBaggedObjectMass(targetObject);

                float massValue = baggedMass;
                float maxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;

                if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.IsBagScaleCapInfinite)
                {
                    float maxScale = 1f;
                    if (float.TryParse(PluginConfig.Instance.BagScaleCap.Value, out float parsedBagScaleCap) && parsedBagScaleCap > 1f)
                    {
                        maxScale = parsedBagScaleCap;
                    }
                    massValue = Mathf.Clamp(baggedMass, 1f, maxCapacity);
                }
                else
                {
                    massValue = Mathf.Max(baggedMass, 1f);
                }
                float t = (massValue - 1f) / (maxCapacity - 1f);
                bagScale01 = 0.5f + 0.5f * t;

                float penalty = 0f;
                if (PluginConfig.Instance.EnableBalance.Value && controller != null)
                {
                    var body = controller.GetComponent<CharacterBody>();
                    float health = body != null ? body.maxHealth : 0f;
                    float level = body != null ? body.level : 1f;
                    float stocks = body != null && body.skillLocator != null && body.skillLocator.utility != null
                        ? body.skillLocator.utility.maxStock : 1f;
                    float massCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;
                    float totalCapacity = controller != null ? Balance.CapacityScalingSystem.GetTotalCapacity(controller) : 1f;

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

                    var penaltyVars = new Dictionary<string, float>
                    {
                        ["T"] = baggedMass,
                        ["M"] = massCapacity,
                        ["C"] = totalCapacity,
                        ["H"] = health,
                        ["L"] = level,
                        ["MC"] = massCap
                    };

                    penalty = FormulaParser.Evaluate(PluginConfig.Instance.MovespeedPenaltyFormula.Value, penaltyVars);
                }
                movespeedPenalty = penalty;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[CalculateFromObject] About to capture stats. targetBody={(targetBody != null ? "NOT NULL" : "NULL")}");

                if (targetBody != null)
                {
                    attackSpeedStat = targetBody.attackSpeed;
                    damageStat = targetBody.baseDamage; // baseDamage is safe default if damage is obscured
                    critStat = targetBody.crit;
                    moveSpeedStat = targetBody.moveSpeed;
                    armorStat = targetBody.armor;
                    regenStat = targetBody.regen;

                    baseMaxHealth = targetBody.baseMaxHealth;
                    baseRegen = targetBody.baseRegen;
                    baseMaxShield = targetBody.baseMaxShield;
                    baseMoveSpeed = targetBody.baseMoveSpeed;
                    baseDamage = targetBody.baseDamage;
                    baseAttackSpeed = targetBody.baseAttackSpeed;
                    baseArmor = targetBody.baseArmor;
                    baseCrit = targetBody.baseCrit;
                    level = 0f;
                    experience = targetBody.experience;
                    teamIndex = (uint)targetBody.teamComponent.teamIndex;
                    isElite = targetBody.isElite;
                    bodyFlags = targetBody.bodyFlags;
                    subtitleNameToken = targetBody.subtitleNameToken;
                    skinIndex = targetBody.skinIndex;

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        if (baseMaxHealth <= 0)
                        {
                            Log.Error($"[CalculateFromObject] CRITICAL: Captured INVALID baseMaxHealth={baseMaxHealth} for {targetObject.name}! This will cause instant death on restoration.");
                        }
                        else
                        {
                            Log.Info($"[CalculateFromObject] Captured valid stats for {targetObject.name}: baseMaxHealth={baseMaxHealth}, level={level}");
                        }
                    }
                }
                else
                {
                    attackSpeedStat = 1f;
                    damageStat = 0f;
                    critStat = 0f;
                    moveSpeedStat = 0f;
                    armorStat = 0f;
                    regenStat = 0f;

                    baseMaxHealth = 0f;
                    baseRegen = 0f;
                    baseMaxShield = 0f;
                    baseMoveSpeed = 0f;
                    baseDamage = 0f;
                    baseAttackSpeed = 0f;
                    baseArmor = 0f;
                    baseCrit = 0f;
                    level = 1f;
                    experience = 0f;
                    teamIndex = unchecked((uint)TeamIndex.None);
                    isElite = false;
                    bodyFlags = CharacterBody.BodyFlags.None;
                    subtitleNameToken = null;
                    skinIndex = 0;
                }

                breakoutTime = 0f;
                breakoutAttempts = 0f;
                elapsedBreakoutTime = 0f;

                if (!hasCapturedModelTransformState)
                {
                    var modelLocator = targetObject.GetComponent<ModelLocator>();
                    if (modelLocator != null)
                    {
                        hasCapturedModelTransformState = true;
                    }
                }

                junkSpawnCount = CalculateJunkSpawnCount(baggedMass);

                float slamMaxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : RoR2.DrifterBagController.maxMass;
                float massFraction = baggedMass / slamMaxCapacity;
                slamDamageCoefficient = 2.8f + (5.0f * massFraction);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectStateData] Calculated state for {targetObject.name}: " +
                            $"mass={baggedMass}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                            $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}, " +
                            $"slamDamageCoef={slamDamageCoefficient:F2}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error calculating from object: {ex.Message}");
            }
        }

        public void CaptureFromAdditionalTimer(Patches.AdditionalSeatBreakoutTimer timer)
        {
            if (timer == null) return;

            this.breakoutTime = timer.breakoutTime;
            this.breakoutAttempts = timer.breakoutAttempts;
            this.elapsedBreakoutTime = timer.GetElapsedBreakoutTime();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BaggedObjectStateData] Captured timer state from AdditionalSeat: age={elapsedBreakoutTime}, attempts={breakoutAttempts}");
            }
        }

        private static int CalculateJunkSpawnCount(float mass)
        {
            return Mathf.Max(1, Mathf.CeilToInt(mass / 100f));
        }

        public void ResetBreakoutData()
        {
            this.breakoutTime = 0f;
            this.breakoutAttempts = 0f;
            this.elapsedBreakoutTime = 0f;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BaggedObjectStateData] Reset breakout data for {targetObject?.name ?? "null"}");
            }
        }
    }
}
