using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Core
{
    // Stores all BaggedObject state fields per GameObject for persistence across object cycling
    public class BaggedObjectStateData
    {
        // Cached FieldInfo instances to reduce reflection overhead
        private static readonly FieldInfo _targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
        private static readonly FieldInfo _isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
        private static readonly FieldInfo _vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
        private static readonly FieldInfo _baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
        private static readonly FieldInfo _bagScale01Field = AccessTools.Field(typeof(BaggedObject), "bagScale01");
        private static readonly FieldInfo _movespeedPenaltyField = AccessTools.Field(typeof(BaggedObject), "movespeedPenalty");
        private static readonly FieldInfo _attackSpeedStatField = AccessTools.Field(typeof(BaggedObject), "attackSpeedStat");
        private static readonly FieldInfo _damageStatField = AccessTools.Field(typeof(BaggedObject), "damageStat");
        private static readonly FieldInfo _critStatField = AccessTools.Field(typeof(BaggedObject), "critStat");
        private static readonly FieldInfo _moveSpeedStatField = AccessTools.Field(typeof(BaggedObject), "moveSpeedStat");
        private static readonly FieldInfo _breakoutTimeField = AccessTools.Field(typeof(BaggedObject), "breakoutTime");
        private static readonly FieldInfo _breakoutAttemptsField = AccessTools.Field(typeof(BaggedObject), "breakoutAttempts");

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

        // Breakout data
        public float breakoutTime;
        public float breakoutAttempts;

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
                
                // Breakout data
                breakoutTime = _breakoutTimeField != null ? (float)_breakoutTimeField.GetValue(state) : 0f;
                breakoutAttempts = _breakoutAttemptsField != null ? (float)_breakoutAttemptsField.GetValue(state) : 0f;

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

        // Restore all fields to a BaggedObject instance.
        public void ApplyToBaggedObject(BaggedObject state)
        {
            if (state == null)
            {
                Log.Error("[BaggedObjectStateData] Cannot apply to null BaggedObject");
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
                _breakoutTimeField?.SetValue(state, breakoutTime);
                _breakoutAttemptsField?.SetValue(state, breakoutAttempts);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectStateData] Applied state to {targetObject?.name ?? "null"}: " +
                            $"mass={baggedMass}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                            $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error applying to BaggedObject: {ex.Message}\n{ex.StackTrace}");
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
                
                if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.UncapBagScale.Value)
                {
                    massValue = Mathf.Clamp(baggedMass, 1f, maxCapacity);
                }
                else
                {
                    massValue = Mathf.Max(baggedMass, 1f);
                }
                float t = (massValue - 1f) / (maxCapacity - 1f);
                bagScale01 = 0.5f + 0.5f * t;

                // Calculate movespeedPenalty
                // Only apply balance penalty settings when EnableBalance is true
                var minPenalty = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.MinMovespeedPenalty.Value : 0f;
                var maxPenalty = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.MaxMovespeedPenalty.Value : 0f;
                var finalLimit = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.FinalMovespeedPenaltyLimit.Value : 0f;
                float massRatio = Mathf.Clamp01(baggedMass / maxCapacity);
                movespeedPenalty = Mathf.Lerp(minPenalty, maxPenalty, massRatio);
                movespeedPenalty = Mathf.Min(movespeedPenalty, finalLimit);

                // Stats - capture from CharacterBody if available
                if (targetBody != null)
                {
                    attackSpeedStat = targetBody.attackSpeed;
                    damageStat = targetBody.baseDamage;
                    critStat = targetBody.crit;
                    moveSpeedStat = targetBody.moveSpeed;
                }
                else
                {
                    // Default values for non-body objects
                    attackSpeedStat = 1f;
                    damageStat = 0f;
                    critStat = 0f;
                    moveSpeedStat = 0f;
                }

                // Breakout data - reset for new objects
                breakoutTime = 0f;
                breakoutAttempts = 0;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectStateData] Calculated state for {targetObject.name}: " +
                            $"mass={baggedMass}, scale={bagScale01}, penalty={movespeedPenalty}, " +
                            $"damage={damageStat}, attackSpeed={attackSpeedStat}, crit={critStat}, moveSpeed={moveSpeedStat}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BaggedObjectStateData] Error calculating from object: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
