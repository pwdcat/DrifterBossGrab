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
    // Stores all BaggedObject state fields per GameObject for persistence across object cycling
    public class BaggedObjectStateData
    {
        // Cached FieldInfo instances to reduce reflection overhead - using centralized ReflectionCache
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
        
        // Target references
        public CharacterBody? targetBody;
        public GameObject? targetObject;
        public bool isBody;

        // Mass and scaling
        public float baggedMass;
        public float bagScale01;
        public float movespeedPenalty;

        // Stats
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
        public float slamDamageCoefficient; // Stores the damage coefficient calculated when object was bagged

        // Breakout timer tracking properties
        public float breakoutTime = 10f;
        public float breakoutAttempts = 0f;
        public float elapsedBreakoutTime = 0f;

        // Model restoration data
        public bool originalAutoUpdateModelTransform = true;
        public bool hasCapturedModelTransformState = false;

        // Additional
        public SpecialObjectAttributes? vehiclePassengerAttributes;

        // Extract all fields from a BaggedObject instance
        public void CaptureFromBaggedObject(BaggedObject state)
        {
            if (state == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot capture from null BaggedObject");
                return;
            }

            try
            {
                // Target references
                targetBody = (CharacterBody?)_targetBodyField?.GetValue(state);
                isBody = _isBodyField != null ? (bool)_isBodyField.GetValue(state) : false;
                vehiclePassengerAttributes = (SpecialObjectAttributes?)_vehiclePassengerAttributesField?.GetValue(state);
                targetObject = state.targetObject;
                
                // Mass and scaling
                baggedMass = _baggedMassField != null ? (float)_baggedMassField.GetValue(state) : 0f;
                bagScale01 = _bagScale01Field != null ? (float)_bagScale01Field.GetValue(state) : 0.5f;
                movespeedPenalty = _movespeedPenaltyField != null ? (float)_movespeedPenaltyField.GetValue(state) : 0f;
                
                // Stats
                attackSpeedStat = _attackSpeedStatField != null ? (float)_attackSpeedStatField.GetValue(state) : 1f;
                damageStat = _damageStatField != null ? (float)_damageStatField.GetValue(state) : 0f;
                critStat = _critStatField != null ? (float)_critStatField.GetValue(state) : 0f;
                moveSpeedStat = _moveSpeedStatField != null ? (float)_moveSpeedStatField.GetValue(state) : 0f;

                if (targetBody != null)
                {
                    armorStat = targetBody.armor;
                    regenStat = targetBody.regen;
                }
                
                // Breakout timer tracking
                if (ReflectionCache.BaggedObject.BreakoutTime != null) breakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(state);

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null) breakoutAttempts = (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(state);

                // Try to get EntityState's internal age
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
                Log.Error($"[BaggedObjectStateData] Error capturing from BaggedObject: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Extract only breakout timer fields from a BaggedObject instance
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
                Log.Error($"[BaggedObjectStateData] Error capturing breakout state from BaggedObject: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Restore all fields to a BaggedObject instance.
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
                // Target references
                _targetBodyField?.SetValue(state, targetBody);
                _isBodyField?.SetValue(state, isBody);
                _vehiclePassengerAttributesField?.SetValue(state, vehiclePassengerAttributes);
                state.targetObject = targetObject;
                
                // Mass and scaling
                _baggedMassField?.SetValue(state, baggedMass);
                _bagScale01Field?.SetValue(state, bagScale01);
                _movespeedPenaltyField?.SetValue(state, movespeedPenalty);
                
                // Stats
                _attackSpeedStatField?.SetValue(state, attackSpeedStat);
                _damageStatField?.SetValue(state, damageStat);
                _critStatField?.SetValue(state, critStat);
                _moveSpeedStatField?.SetValue(state, moveSpeedStat);
                
                // Breakout data
                // Set breakoutTime and breakoutAttempts directly on the state object
                if (ReflectionCache.BaggedObject.BreakoutTime != null) ReflectionCache.BaggedObject.BreakoutTime.SetValue(state, breakoutTime);

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null) ReflectionCache.BaggedObject.BreakoutAttempts.SetValue(state, breakoutAttempts);

                // Set EntityState's internal age
                if (ReflectionCache.EntityState.FixedAge != null)
                {
                    ReflectionCache.EntityState.FixedAge.SetValue(state, elapsedBreakoutTime);
                }

                // Apply stats to the body if it exists
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
                Log.Error($"[BaggedObjectStateData] Error applying to BaggedObject: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Apply captured stats and flags to a CharacterBody instance
        public void ApplyToCharacterBody(CharacterBody body)
        {
            if (body == null) return;

            try
            {
                body.baseMaxHealth = baseMaxHealth;
                body.baseRegen = baseRegen;
                body.baseMaxShield = baseMaxShield;
                body.baseMoveSpeed = baseMoveSpeed;
                body.baseDamage = baseDamage;
                body.baseAttackSpeed = baseAttackSpeed;
                body.baseArmor = baseArmor;
                body.baseCrit = baseCrit;
                body.level = level;
                body.experience = experience;
                body.teamComponent.teamIndex = (TeamIndex)teamIndex;
                body.bodyFlags = bodyFlags;
                body.subtitleNameToken = subtitleNameToken ?? body.subtitleNameToken;
                body.skinIndex = skinIndex;

                // Recalculate stats to apply changes
                body.RecalculateStats();
                
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectStateData] Restored CharacterBody stats for {body.name}: level={level}, hp={body.baseMaxHealth}, elite={isElite}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to CharacterBody: {ex.Message}");
            }
        }

        // Calculate fresh state for new objects from a GameObject and DrifterBagController.
        public void CalculateFromObject(GameObject targetObject, DrifterBagController controller)
        {
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
                // Target references
                this.targetObject = targetObject;
                HealthComponent healthComponent = targetObject.GetComponent<HealthComponent>();
                targetBody = healthComponent?.body;
                isBody = healthComponent != null;
                vehiclePassengerAttributes = targetObject.GetComponent<SpecialObjectAttributes>();

                // Mass and scaling
                baggedMass = controller.CalculateBaggedObjectMass(targetObject);

                // Calculate bagScale01 - only apply UncapBagScale when EnableBalance is true
                float massValue = baggedMass;
                float maxCapacity = controller != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(controller) : DrifterBagController.maxMass;
                
                if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.IsBagScaleCapInfinite)
                {
                    float maxScale = 1f;
                    if (float.TryParse(PluginConfig.Instance.BagScaleCap.Value, out float parsedBagScaleCap) && parsedBagScaleCap > 1f) {
                        maxScale = parsedBagScaleCap;
                    }
                    massValue = Mathf.Clamp(baggedMass, 1f, maxCapacity); // Scale itself handled natively if cap is exceeded
                }
                else
                {
                    massValue = Mathf.Max(baggedMass, 1f);
                }
                float t = (massValue - 1f) / (maxCapacity - 1f);
                bagScale01 = 0.5f + 0.5f * t;

                // Calculate movespeedPenalty using formula when EnableBalance is true
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

                // Stats - capture from CharacterBody if available
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
                    level = targetBody.level;
                    experience = targetBody.experience;
                    teamIndex = (uint)targetBody.teamComponent.teamIndex;
                    isElite = targetBody.isElite;
                    bodyFlags = targetBody.bodyFlags;
                    subtitleNameToken = targetBody.subtitleNameToken;
                    skinIndex = targetBody.skinIndex;
                }
                else
                {
                    // Default values for non-body objects
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

                // Breakout data - reset for new objects
                breakoutTime = 0f;
                breakoutAttempts = 0f;
                elapsedBreakoutTime = 0f;

                if (!hasCapturedModelTransformState)
                {
                    var modelLocator = targetObject.GetComponent<ModelLocator>();
                    if (modelLocator != null)
                    {
                        originalAutoUpdateModelTransform = modelLocator.autoUpdateModelTransform;
                        hasCapturedModelTransformState = true;
                    }
                }
                
                

                // Calculate junk spawn count
                junkSpawnCount = CalculateJunkSpawnCount(baggedMass);

                // Calculate and store slam damage coefficient based on this object's mass
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
                Log.Error($"[BaggedObjectStateData] Error calculating from object: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Capture properties explicitly from an additional seat timer
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

        // Calculate junk spawn count based on mass
        private static int CalculateJunkSpawnCount(float mass)
        {
            // Formula: 1 junk cube per 100 mass, minimum 1
            return Mathf.Max(1, Mathf.CeilToInt(mass / 100f));
        }
    }
}
