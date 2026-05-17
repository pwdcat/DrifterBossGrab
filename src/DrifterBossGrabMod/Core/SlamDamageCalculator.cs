#nullable enable
using RoR2;
using UnityEngine;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;

using System.Reflection;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Core
{

    public static class SlamDamageCalculator
    {
        public const float DefaultBaseDamageCoef = Constants.Multipliers.SlamBaseDamageCoef;
        public const float DefaultMassScaling = Constants.Multipliers.SlamMassScaling;

        public static float GetPredictedDamage(DrifterBagController? bagController, GameObject? target)
        {
            if (bagController == null || target == null) return 0f;

            var drifterBody = bagController!.GetComponent<CharacterBody>();
            if (!drifterBody) return 0f;

            float effectiveCoef = GetEffectiveCoefficient(bagController);

            if (PluginConfig.Instance.EnableBalance.Value &&
                PluginConfig.Instance.AoEDamageDistribution.Value != DrifterBossGrabMod.AoEDamageMode.None &&
                PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.All &&
                PluginConfig.Instance.AoEDamageDistribution.Value == AoEDamageMode.Split)
            {
                var bagState = BagPatches.GetState(bagController);
                int count = bagState.BaggedObjects?.Count ?? 1;
                if (count > 1)
                    effectiveCoef /= count;
            }

            float baseDamage = drifterBody.damage * effectiveCoef;

            float itemDamageMultiplier = GetItemDamageMultiplier(drifterBody);
            float damage = baseDamage * itemDamageMultiplier;

            var targetBody = target!.GetComponent<CharacterBody>();
            if (targetBody != null)
            {
                float armor = targetBody.armor;
                float armorFactor = armor >= 0 ? (100f / (100f + armor)) : (2f - (100f / (100f - armor)));
                damage = Mathf.Max(1f, damage * armorFactor);
            }

            return damage;
        }
        public static float GetPredictedDamageFraction(DrifterBagController? bagController, GameObject? target)
        {
            if (bagController == null || target == null) return 0f;

            var junkController = target.GetComponent<JunkCubeController>();
            if (junkController)
            {
                if (ReflectionCache.JunkCubeController.MaxActivationCount != null)
                {
                    int maxCount = (int)ReflectionCache.JunkCubeController.MaxActivationCount.GetValue(junkController);
                    if (maxCount > 0) return 1f / maxCount;
                }

                return 0.334f;
            }

            var body = target.GetComponent<CharacterBody>();
            if (body && body.healthComponent)
            {
                float totalHealth = body.healthComponent.fullCombinedHealth;
                if (totalHealth <= 0f) return 1f;

                float damage = GetPredictedDamage(bagController, target);
                return Mathf.Clamp01(damage / totalHealth);
            }

            var attributes = target.GetComponent<SpecialObjectAttributes>();
            if (attributes && attributes.maxDurability > 0)
            {
                return 1f / attributes.maxDurability;
            }

            return 0f;
        }

        public static float GetEffectiveCoefficient(DrifterBagController? bagController)
        {
            float baggedMass = bagController?.baggedMass ?? 0f;

            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                return DefaultBaseDamageCoef + (DefaultMassScaling * baggedMass / DrifterBagController.maxMass);
            }

            var body = bagController?.GetComponent<CharacterBody>();
            float maxCapacity = bagController != null ? CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;

            var localVars = new Dictionary<string, float>
            {
                { "BASE_COEF", DefaultBaseDamageCoef },
                { "MASS_SCALING", DefaultMassScaling },
                { "BM", baggedMass },
                { "MC", maxCapacity }
            };

            string formula = PluginConfig.Instance.SlamDamageFormula.Value;
            float result = FormulaParser.Evaluate(formula, body, localVars);

            if (float.IsNaN(result))
            {
                Log.Warning($"[SlamDamageCalculator] Formula '{formula}' returned NaN. Using default calculation.");
                result = DefaultBaseDamageCoef + (DefaultMassScaling * baggedMass / maxCapacity);
            }
            else if (float.IsInfinity(result))
            {
                Log.Warning($"[SlamDamageCalculator] Formula '{formula}' returned Infinity. Using default calculation.");
                result = DefaultBaseDamageCoef + (DefaultMassScaling * baggedMass / maxCapacity);
            }
            return result;
        }

        private static float GetItemDamageMultiplier(CharacterBody attackerBody)
        {
            if (attackerBody == null || attackerBody.inventory == null)
                return 1f;

            float itemDamageMultiplier = 1f;

            int fragileStacks = attackerBody.inventory.GetItemCountEffective(DLC1Content.Items.FragileDamageBonus);
            if (fragileStacks > 0)
            {
                itemDamageMultiplier *= 1f + fragileStacks * Constants.Multipliers.DelicateWatchDamageBonus;
            }

            int nearbyDamageStacks = attackerBody.inventory.GetItemCountEffective(RoR2Content.Items.NearbyDamageBonus);
            if (nearbyDamageStacks > 0)
            {
                itemDamageMultiplier *= 1f + nearbyDamageStacks * Constants.Multipliers.NearbyDamageBonus;
            }

            return itemDamageMultiplier;
        }

        public static void LogDetails(DrifterBagController? bagController, GameObject? target)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value) return;

            float baseDamageCoef = DefaultBaseDamageCoef;
            float massScaling = DefaultMassScaling;
            bool foundState = false;

            if (bagController != null)
            {
                var stateMachines = bagController.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.state is EntityStates.Drifter.SuffocateSlam slamState)
                    {
                        baseDamageCoef = slamState.damageCoefficient;
                        massScaling = slamState.damageCoefficientIncreaseWithMass;
                        foundState = true;
                        break;
                    }
                }
            }

            float mass = bagController != null ? bagController.baggedMass : 0f;
            float maxCapacity = bagController != null ? CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;
            float massFraction = (bagController != null && maxCapacity > 0) ? (bagController.baggedMass / maxCapacity) : 0f;
            float effectiveCoef = foundState ? baseDamageCoef : (baseDamageCoef + (massScaling * massFraction));

            var drifterBody = bagController ? bagController!.GetComponent<CharacterBody>() : null;
            float damageStat = drifterBody ? drifterBody!.damage : 0f;
            float baseDamage = damageStat * effectiveCoef;

            float finalDamage = GetPredictedDamage(bagController, target);

            if (target == null) return;
            var junkController = target.GetComponent<JunkCubeController>();
            var body = target.GetComponent<CharacterBody>();

            if (junkController)
            {
                var field = typeof(JunkCubeController).GetField("_maxActivationCount", BindingFlags.NonPublic | BindingFlags.Instance);
                int maxCount = field != null ? (int)field.GetValue(junkController) : 3;
                float frac = maxCount > 0 ? 1f / maxCount : 0f;
                Log.Info($"  FractionPath: JUNK_CUBE (ActivationCount logic: 1/{maxCount} = {frac:F3})");
            }

            else if (body && body.healthComponent)
            {
                float totalHealth = body.healthComponent.fullCombinedHealth;
                float frac = totalHealth > 0f ? Mathf.Clamp01(finalDamage / totalHealth) : 1f;
                Log.Info($"  FractionPath: HEALTH (hp={body.healthComponent.combinedHealth:F1}/{totalHealth:F1}, previewFrac={frac:F3})");
            }

            else
            {
                var attributes = target.GetComponent<SpecialObjectAttributes>();
                if (attributes && attributes.maxDurability > 0)
                {
                    Log.Info($"  FractionPath: DURABILITY (durability={attributes.durability}/{attributes.maxDurability}, previewFrac={1f / attributes.maxDurability:F3})");
                }
                else
                {
                    Log.Info($"  FractionPath: NONE (hasAttributes={attributes != null}, hasBody={body != null}, hasHC={body?.healthComponent != null})");
                }
            }
        }
    }
}
