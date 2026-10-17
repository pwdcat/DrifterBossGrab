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

    // ========================================================================================
    // BAGGED OBJECT STATE DATA
    // ========================================================================================
    public class BaggedObjectStateData
    {
        private static readonly FieldInfo _targetBodyField = ReflectionCache.BaggedObject.TargetBody;
        private static readonly FieldInfo _isBodyField = ReflectionCache.BaggedObject.IsBody;
        private static readonly FieldInfo _vehiclePassengerAttributesField = ReflectionCache.BaggedObject.VehiclePassengerAttributes;
        private static readonly FieldInfo _baggedMassField = ReflectionCache.BaggedObject.BaggedMass;
        private static readonly FieldInfo _bagScale01Field = ReflectionCache.BaggedObject.BagScale01;
        private static readonly FieldInfo _movespeedPenaltyField = ReflectionCache.BaggedObjectAdditional.MovespeedPenalty;
        private static readonly FieldInfo? _attackSpeedStatField = ReflectionCache.BaggedObjectAdditional.AttackSpeedStat;
        private static readonly FieldInfo? _damageStatField = ReflectionCache.BaggedObjectAdditional.DamageStat;
        private static readonly FieldInfo? _critStatField = ReflectionCache.BaggedObjectAdditional.CritStat;
        private static readonly FieldInfo? _moveSpeedStatField = ReflectionCache.BaggedObjectAdditional.MoveSpeedStat;
        private static readonly FieldInfo _drifterBagControllerField = ReflectionCache.BaggedObject.DrifterBagController;

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

        public float breakoutTime;
        public float breakoutAttempts = 0f;
        public float elapsedBreakoutTime = 0f;
        public int smacks = 0;

        public bool hasCapturedModelTransformState = false;
        public SpecialObjectAttributes? vehiclePassengerAttributes;

        public bool originalIsKinematic;
        public bool originalUseGravity;
        public float originalMass;
        public float originalDrag;
        public float originalAngularDrag;
        public bool hasCapturedRigidbodyState = false;

        // ========================================================================================
        // CAPTURE LOGIC
        // ========================================================================================
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

                if (ReflectionCache.BaggedObject.BreakoutTimer != null)
                {
                    elapsedBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTimer.GetValue(state);
                }

                var bagController = (DrifterBagController?)_drifterBagControllerField?.GetValue(state);
                if (bagController != null && ReflectionCache.DrifterBagController.Smacks != null)
                {
                    this.smacks = Convert.ToInt32(ReflectionCache.DrifterBagController.Smacks.GetValue(bagController));
                }

                Log.Debug($"[BaggedObjectStateData] Captured state for {(targetObject != null ? targetObject.name : "null")}: " +
                        $"mass={baggedMass}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                        $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}");
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error capturing from BaggedObject: {ex.Message}");
            }
        }

        public void CaptureBreakoutStateFromBaggedObject(BaggedObject state)
        {
            if (state == null || state.drifterBagController == null ||
                ReflectionCache.BaggedObject.BreakoutTime?.GetValue(state) is not float interval || interval <= 0f) return;

            try
            {
                if (ReflectionCache.BaggedObject.BreakoutTime != null) breakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(state);

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null) breakoutAttempts = (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(state);

                if (ReflectionCache.BaggedObject.BreakoutTimer != null)
                {
                    elapsedBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTimer.GetValue(state);
                }

                var breakoutBagController = (DrifterBagController?)_drifterBagControllerField?.GetValue(state);
                if (breakoutBagController != null && ReflectionCache.DrifterBagController.Smacks != null)
                {
                    this.smacks = (int)ReflectionCache.DrifterBagController.Smacks.GetValue(breakoutBagController);
                }

                Log.Debug($"[BaggedObjectStateData] Captured breakout state for {(targetObject != null ? targetObject.name : "null")}: " +
                        $"age={elapsedBreakoutTime}, breakoutTime={breakoutTime}");
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error capturing breakout state from BaggedObject: {ex.Message}");
            }
        }

        // ========================================================================================
        // APPLICATION LOGIC
        // ========================================================================================
        public void ApplyToBaggedObject(BaggedObject state)
        {
            if (state == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot apply to null BaggedObject");
                return;
            }

            if (this.targetObject == null && this.baggedMass == 0f)
            {
                Log.Debug("[BaggedObjectStateData] Skipping application of uninitialized 'stub' state (targetObject is null).");
                return;
            }

            if (this.targetObject == null || (this.targetObject is UnityEngine.Object uo && !uo))
            {
                Log.Debug("[BaggedObjectStateData] Skipping application - targetObject is null or destroyed.");
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

                if (ReflectionCache.BaggedObject.BreakoutTime != null && breakoutTime > 0f)
                {
                    ReflectionCache.BaggedObject.BreakoutTime.SetValue(state, breakoutTime);
                }

                if (ReflectionCache.BaggedObject.BreakoutAttempts != null && breakoutTime > 0f)
                {
                    ReflectionCache.BaggedObject.BreakoutAttempts.SetValue(state, breakoutAttempts);
                }

                if (ReflectionCache.BaggedObject.BreakoutTimer != null && breakoutTime > 0f)
                {
                    ReflectionCache.BaggedObject.BreakoutTimer.SetValue(state, elapsedBreakoutTime);
                }

                var applyBagController = (DrifterBagController?)_drifterBagControllerField?.GetValue(state);
                if (applyBagController != null && ReflectionCache.DrifterBagController.Smacks != null)
                {
                    ReflectionCache.DrifterBagController.Smacks.SetValue(applyBagController, this.smacks);
                }

                if (targetBody != null)
                {
                    ApplyToCharacterBody(targetBody);
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string targetName = this.targetObject != null ? this.targetObject.name : (state.targetObject != null ? state.targetObject.name : "null");
                    Log.Debug($"[BaggedObjectStateData] Applied state to {targetName}: " +
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

            if (baseMaxHealth <= 0)
            {
                Log.Warning($"[ApplyToCharacterBody] Skipped invalid baseMaxHealth={baseMaxHealth} for {body.name}. The saved state may be uninitialized.");
                return;
            }

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

                body.experience = experience;
                body.teamComponent.teamIndex = (TeamIndex)teamIndex;
                body.bodyFlags = bodyFlags;
                body.subtitleNameToken = subtitleNameToken ?? body.subtitleNameToken;
                body.skinIndex = skinIndex;

                body.RecalculateStats();

                Log.Debug($"[ApplyToCharacterBody] Captured stats: baseMaxHealth={baseMaxHealth}, baseRegen={baseRegen}, baseDamage={baseDamage}, level={level}");

                if (baseMaxHealth <= 0)
                {
                    Log.Error($"[ApplyToCharacterBody] Invalid saved baseMaxHealth={baseMaxHealth} for {body.name}. Health must be positive.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to CharacterBody: {ex.Message}");
            }
        }

        public void RestorePhysicsAndHurtboxes(GameObject target)
        {
            if (target == null || target.GetComponent<CharacterBody>() != null) return;

            var rb = target.GetComponent<Rigidbody>();
            if (rb)
            {
                if (hasCapturedRigidbodyState)
                {
                    rb.isKinematic = originalIsKinematic;
                    rb.useGravity = originalUseGravity;
                    rb.mass = originalMass;
                    rb.drag = originalDrag;
                    rb.angularDrag = originalAngularDrag;
                    rb.detectCollisions = true;
                }
                else
                {
                    rb.isKinematic = false;
                    rb.detectCollisions = true;
                }
            }
        }

        // ========================================================================================
        // CALCULATION LOGIC
        // ========================================================================================
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
                this.targetObject = targetObject;
                HealthComponent healthComponent = targetObject.GetComponent<HealthComponent>();
                targetBody = targetObject.GetComponent<CharacterBody>();
                isBody = healthComponent != null;
                vehiclePassengerAttributes = targetObject.GetComponent<SpecialObjectAttributes>();

                var rb = targetObject.GetComponent<Rigidbody>();
                if (rb != null && !hasCapturedRigidbodyState)
                {
                    originalIsKinematic = rb.isKinematic;
                    originalUseGravity = rb.useGravity;
                    originalMass = rb.mass;
                    originalDrag = rb.drag;
                    originalAngularDrag = rb.angularDrag;
                    hasCapturedRigidbodyState = true;
                }

                baggedMass = controller.CalculateBaggedObjectMass(targetObject);

                bagScale01 = StateCalculator.CalculateBagScale01(controller, baggedMass);

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

                if (targetBody != null)
                {
                    attackSpeedStat = targetBody.attackSpeed;
                    damageStat = targetBody.baseDamage;
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
                            Log.Error($"[CalculateFromObject] Captured invalid baseMaxHealth={baseMaxHealth} for {targetObject.name}. Health must be positive before restoration.");
                        }
                        else
                        {
                            Log.Debug($"[CalculateFromObject] Captured valid stats for {targetObject.name}: baseMaxHealth={baseMaxHealth}, level={level}");
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

                Log.Debug($"[BaggedObjectStateData] Calculated state for {targetObject.name}: " +
                        $"mass={baggedMass}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                        $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}");
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

            Log.Debug($"[BaggedObjectStateData] Captured timer state from AdditionalSeat: age={elapsedBreakoutTime}, attempts={breakoutAttempts}");
        }

        // ========================================================================================
        // UTILITIES
        // ========================================================================================
        private static int CalculateJunkSpawnCount(float mass)
        {
            return Mathf.Max(1, Mathf.CeilToInt(mass / 100f));
        }

        public void ResetBreakoutData()
        {
            this.breakoutTime = 0f;
            this.breakoutAttempts = 0f;
            this.elapsedBreakoutTime = 0f;
            this.smacks = 0;

            Log.Debug($"[BaggedObjectStateData] Reset breakout data for {(targetObject != null ? targetObject.name : "null")}");
        }
    }
}
