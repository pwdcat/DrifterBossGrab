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

        public bool isTeleporter;
        public int teleporterShrineStacks;

        public const float DefaultBreakoutTime = 10f;
        public const float DefaultBagScale = 0.5f;
        public const float DefaultMassCap = 700f;
        public const float JunkSpawnMassDivisor = 100f;

        public float breakoutTime = DefaultBreakoutTime;
        public float breakoutAttempts = 0f;
        public float elapsedBreakoutTime = 0f;

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

                if (ReflectionCache.EntityState.FixedAge != null)
                {
                    elapsedBreakoutTime = (float)ReflectionCache.EntityState.FixedAge.GetValue(state);
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.DebugIfEnabled("[BaggedObjectStateData] Captured state for {0}: mass={1}, scale={2}, penalty={3}, damage={4}, attackSpeed={5}, crit={6}, moveSpeed={7}",
                            targetObject?.name ?? "null", baggedMass, bagScale01, movespeedPenalty, damageStat, attackSpeedStat, critStat, moveSpeedStat);
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
                    Log.DebugIfEnabled("[BaggedObjectStateData] Captured breakout state for {0}: age={1}, breakoutTime={2}",
                            targetObject?.name ?? "null", elapsedBreakoutTime, breakoutTime);
                }
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

            // Detect and prevent applying uninitialized "stub" states which would zero out a functional object
            if (this.targetObject == null && this.baggedMass == 0f)
            {
                Log.DebugIfEnabled("[BaggedObjectStateData] Skipping application of uninitialized 'stub' state (targetObject is null).");
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
                    Log.DebugIfEnabled("[BaggedObjectStateData] Applied state to {0}: mass={1}, age={2}, scale={3}, penalty={4}, damage={5}, attackSpeed={6}, crit={7}, moveSpeed={8}, level={9}, isElite={10}",
                            targetObject?.name ?? "null", baggedMass, elapsedBreakoutTime, bagScale01, movespeedPenalty, damageStat, attackSpeedStat, critStat, moveSpeedStat, level, isElite);
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

            Log.DebugIfEnabled("[ApplyToCharacterBody] body.name={0}, body.baseMaxHealth={1}, state.baseMaxHealth={2}", body.name, body.baseMaxHealth, baseMaxHealth);

            if (baseMaxHealth <= 0)
            {
                Log.DebugIfEnabled($"[ApplyToCharacterBody] ABORTED: Attempting to apply INVALID baseMaxHealth={baseMaxHealth} to {body.name}. This would have killed the object. State state is likely uninitialized.");
                return; // Critical safety: Do not apply zero/negative health to a living body
            }

            try
            {
                body.baseMaxHealth = baseMaxHealth;

                Log.DebugIfEnabled("[ApplyToCharacterBody] body.baseMaxHealth={0}", body.baseMaxHealth);
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

                Log.DebugIfEnabled("[ApplyToCharacterBody] Captured stats: baseMaxHealth={0}, baseRegen={1}, baseDamage={2}, level={3}", baseMaxHealth, baseRegen, baseDamage, level);

                if (baseMaxHealth <= 0)
                {
                    Log.Error($"[ApplyToCharacterBody] Attempting to apply INVALID baseMaxHealth={baseMaxHealth} to {body.name}! This will kill the object!");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to CharacterBody: {ex.Message}");
            }
        }

        public void ApplyToTeleporter(TeleporterInteraction teleporter)
        {
            if (teleporter == null || !isTeleporter) return;

            try
            {
                teleporter.Network_shrineBonusStacks = teleporterShrineStacks;
                if (teleporter.bossGroup != null)
                {
                    teleporter.bossGroup.bonusRewardCount = teleporterShrineStacks;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to Teleporter: {ex.Message}");
            }
        }

        // ========================================================================================
        // CALCULATION LOGIC
        // ========================================================================================

        public void CalculateFromObject(GameObject targetObject, DrifterBagController controller)
        {
            Log.DebugIfEnabled("[CalculateFromObject] targetObject={0}", targetObject?.name ?? "null");

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

                Log.DebugIfEnabled("[CalculateFromObject] targetObject={0}, targetBody={1}, healthComponent={2}",
                    targetObject.name, targetBody != null ? targetBody.name : "null", healthComponent != null ? "exists" : "null");
                if (healthComponent != null && healthComponent.body != null)
                    Log.DebugIfEnabled("[CalculateFromObject] healthComponent.body={0}, healthComponent.body.baseMaxHealth={1}", healthComponent.body.name, healthComponent.body.baseMaxHealth);
                if (targetBody != null)
                    Log.DebugIfEnabled("[CalculateFromObject] targetBody.baseMaxHealth={0}", targetBody.baseMaxHealth);

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
                bagScale01 = DefaultBagScale + DefaultBagScale * t;

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

                    float massCap = DefaultMassCap;
                    string massCapStr = PluginConfig.Instance.MassCap.Value;
                    if (string.Equals(massCapStr, "INF", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(massCapStr, "Infinity", StringComparison.OrdinalIgnoreCase))
                    {
                        massCap = float.MaxValue;
                    }
                    else if (!float.TryParse(massCapStr, out massCap))
                    {
                        massCap = DefaultMassCap;
                    }

                    StateCalculator.UpdatePenaltyVarsBuffer(baggedMass, massCapacity, totalCapacity, health, level, massCap);
                    penalty = FormulaParser.Evaluate(PluginConfig.Instance.MovespeedPenaltyFormula.Value, StateCalculator.PenaltyVarsBuffer);
                }
                movespeedPenalty = penalty;

                Log.DebugIfEnabled("[CalculateFromObject] About to capture stats. targetBody={0}", (targetBody != null ? "NOT NULL" : "NULL"));

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
                            Log.Error($"[CalculateFromObject] Captured INVALID baseMaxHealth={baseMaxHealth} for {targetObject.name}! This will cause instant death on restoration.");
                        }
                        else
                        {
                            Log.DebugIfEnabled("[CalculateFromObject] Captured valid stats for {0}: baseMaxHealth={1}, level={2}", targetObject.name, baseMaxHealth, level);
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

                    // Teleporter specific capture
                    var teleporter = targetObject.GetComponent<TeleporterInteraction>();
                    if (teleporter != null)
                    {
                        isTeleporter = true;
                        teleporterShrineStacks = teleporter.shrineBonusStacks;

                        Log.DebugIfEnabled("[CalculateFromObject] Captured teleporter state: shrineStacks={0}", teleporterShrineStacks);
                    }
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

                // Set initial breakout timer values
                float finalTime = Mathf.Max(DefaultBreakoutTime - 0.005f * baggedMass, 1f) * (PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.BreakoutTimeMultiplier.Value : 1f);
                if (targetBody != null && targetBody.isElite) finalTime *= 0.8f;

                breakoutTime = finalTime;
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

                Log.DebugIfEnabled("[BaggedObjectStateData] Calculated state for {0}: mass={1}, scale={2}, penalty={3}, damage={4}, attackSpeed={5}, crit={6}, moveSpeed={7}",
                    targetObject.name, baggedMass, bagScale01, movespeedPenalty, damageStat, attackSpeedStat, critStat, moveSpeedStat);
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

            Log.DebugIfEnabled("[BaggedObjectStateData] Captured timer state from AdditionalSeat: age={0}, attempts={1}", elapsedBreakoutTime, breakoutAttempts);
        }

        // ========================================================================================
        // UTILITIES
        // ========================================================================================

        private static int CalculateJunkSpawnCount(float mass)
        {
            return Mathf.Max(1, Mathf.CeilToInt(mass / JunkSpawnMassDivisor));
        }

        public void ResetBreakoutData()
        {
            this.breakoutTime = 0f;
            this.breakoutAttempts = 0f;
            this.elapsedBreakoutTime = 0f;

            Log.DebugIfEnabled("[BaggedObjectStateData] Reset breakout data for {0}", targetObject?.name ?? "null");
        }

        public void CopyTo(BaggedObjectStateData other)
        {
            if (other == null) return;

            other.targetBody = this.targetBody;
            other.targetObject = this.targetObject;
            other.isBody = this.isBody;

            other.baggedMass = this.baggedMass;
            other.bagScale01 = this.bagScale01;
            other.movespeedPenalty = this.movespeedPenalty;

            other.attackSpeedStat = this.attackSpeedStat;
            other.damageStat = this.damageStat;
            other.critStat = this.critStat;
            other.moveSpeedStat = this.moveSpeedStat;
            other.armorStat = this.armorStat;
            other.regenStat = this.regenStat;
            other.baseMaxHealth = this.baseMaxHealth;
            other.baseRegen = this.baseRegen;
            other.baseMaxShield = this.baseMaxShield;
            other.baseMoveSpeed = this.baseMoveSpeed;
            other.baseDamage = this.baseDamage;
            other.baseAttackSpeed = this.baseAttackSpeed;
            other.baseArmor = this.baseArmor;
            other.baseCrit = this.baseCrit;
            other.level = this.level;
            other.experience = this.experience;
            other.teamIndex = this.teamIndex;
            other.isElite = this.isElite;
            other.bodyFlags = this.bodyFlags;
            other.subtitleNameToken = this.subtitleNameToken;
            other.skinIndex = this.skinIndex;

            other.junkSpawnCount = this.junkSpawnCount;

            other.isTeleporter = this.isTeleporter;
            other.teleporterShrineStacks = this.teleporterShrineStacks;

            other.breakoutTime = this.breakoutTime;
            other.breakoutAttempts = this.breakoutAttempts;
            other.elapsedBreakoutTime = this.elapsedBreakoutTime;

            other.hasCapturedModelTransformState = this.hasCapturedModelTransformState;
            other.vehiclePassengerAttributes = this.vehiclePassengerAttributes;

            other.originalIsKinematic = this.originalIsKinematic;
            other.originalUseGravity = this.originalUseGravity;
            other.originalMass = this.originalMass;
            other.originalDrag = this.originalDrag;
            other.originalAngularDrag = this.originalAngularDrag;
            other.hasCapturedRigidbodyState = this.hasCapturedRigidbodyState;
        }
    }
}
