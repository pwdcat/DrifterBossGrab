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
    // Calculates predicted slam damage for the damage preview overlay
    // Uses the SuffocateSlam damage formula:
    // effectiveCoef = baseDamageCoef + (massScaling * baggedMass / maxCapacity)
    // damage = drifterBody.damage * effectiveCoef
    public static class SlamDamageCalculator
    {
        public const float DefaultBaseDamageCoef = Constants.Multipliers.SlamBaseDamageCoef;
        public const float DefaultMassScaling = Constants.Multipliers.SlamMassScaling;

        // Calculates predicted damage
        // Uses DryRunDamageCalculator
        // param: bagController - The DrifterBagController
        // param: target - The target bagged object
        // returns: Absolute damage value, or 0 if can't calculate
        public static float GetPredictedDamage(DrifterBagController? bagController, GameObject? target)
        {
            if (!bagController || !target) return 0f;

            var drifterBody = bagController.GetComponent<CharacterBody>();
            if (!drifterBody) return 0f;

            float effectiveCoef = GetEffectiveCoefficient(bagController);

            // Apply AoE distribution mode if enabled
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

            // Calculate base damage
            float baseDamage = drifterBody.damage * effectiveCoef;

            // Get target body for dry run calculation
            var targetBody = target.GetComponent<CharacterBody>();
            if (targetBody == null)
            {
                // Fallback for non-CharacterBody targets
                // Still apply item damage modifiers (e.g., Delicate Watch) even without a CharacterBody target
                float itemDamageMultiplier = GetItemDamageMultiplier(drifterBody);
                return baseDamage * itemDamageMultiplier;
            }

            // Use dry run calculation to get accurate damage including item modifiers
            return DryRunDamageCalculator.GetPredictedDamage(drifterBody, targetBody, baseDamage);
        }
        public static float GetPredictedDamageFraction(DrifterBagController bagController, GameObject target)
        {
            if (!bagController || !target) return 0f;

            // Priority 0: JunkCubeController (Custom Durability)
            // JunkCubeController rejects standard damage and uses ActivationCount instead.
            // We need to reflect _maxActivationCount since it's private.
            var junkController = target.GetComponent<JunkCubeController>();
            if (junkController)
            {
                if (ReflectionCache.JunkCubeController.MaxActivationCount != null)
                {
                    int maxCount = (int)ReflectionCache.JunkCubeController.MaxActivationCount.GetValue(junkController);
                    if (maxCount > 0) return 1f / maxCount;
                }
                // Fallback if reflection somehow fails
                return 0.334f;
            }

            // Priority 1: CharacterBody (Health)
            // Matches DrifterBagController.CmdDamageBaggedObject priority
            var body = target.GetComponent<CharacterBody>();
            if (body && body.healthComponent)
            {
                float totalHealth = body.healthComponent.fullCombinedHealth;
                if (totalHealth <= 0f) return 1f;

                float damage = GetPredictedDamage(bagController, target);
                return Mathf.Clamp01(damage / totalHealth);
            }

            // Priority 2: SpecialObjectAttributes (Durability)
            var attributes = target.GetComponent<SpecialObjectAttributes>();
            if (attributes && attributes.maxDurability > 0)
            {
                return 1f / attributes.maxDurability;
            }

            return 0f;
        }

        // Gets the effective damage coefficient including mass scaling.
        public static float GetEffectiveCoefficient(DrifterBagController bagController)
        {
            float baggedMass = bagController?.baggedMass ?? 0f;

            // When balance is off, use vanilla formula: 2.8 + (5.0 * baggedMass / 700)
            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                return DefaultBaseDamageCoef + (DefaultMassScaling * baggedMass / DrifterBagController.maxMass);
            }

            var body = bagController?.GetComponent<CharacterBody>();
            float maxCapacity = bagController ? CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;

            // Create local variables specific to slam damage
            var localVars = new Dictionary<string, float>
            {
                { "BASE_COEF", DefaultBaseDamageCoef },
                { "MASS_SCALING", DefaultMassScaling },
                { "BM", baggedMass },
                { "MC", maxCapacity }
            };

            string formula = PluginConfig.Instance.SlamDamageFormula.Value;
            float result = FormulaParser.Evaluate(formula, body, localVars);

            // Validate result
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[SlamDamageCalculator] Formula '{formula}' = {result:F2} (BM={baggedMass:F1}, MC={maxCapacity:F1})");
            }

            return result;
        }

        // Gets the item damage multiplier from the attacker's inventory
        // This is used for targets without a CharacterBody
        private static float GetItemDamageMultiplier(CharacterBody attackerBody)
        {
            if (attackerBody == null || attackerBody.inventory == null)
                return 1f;

            float itemDamageMultiplier = 1f;

            // Delicate Watch (FragileDamageBonus) - +20% per stack
            int fragileStacks = attackerBody.inventory.GetItemCountEffective(DLC1Content.Items.FragileDamageBonus);
            if (fragileStacks > 0)
            {
                itemDamageMultiplier *= 1f + fragileStacks * Constants.Multipliers.DelicateWatchDamageBonus;
            }

            // Nearby Damage Bonus - +20% per stack when within 13m
            // Note: Can't check distance without a target, so we assume it applies
            int nearbyDamageStacks = attackerBody.inventory.GetItemCountEffective(RoR2Content.Items.NearbyDamageBonus);
            if (nearbyDamageStacks > 0)
            {
                itemDamageMultiplier *= 1f + nearbyDamageStacks * Constants.Multipliers.NearbyDamageBonus;
            }

            return itemDamageMultiplier;
        }

        // Logs the detailed damage calculation for debugging.
        public static void LogDetails(DrifterBagController bagController, GameObject target)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value) return;

            float baseDamageCoef = DefaultBaseDamageCoef;
            float massScaling = DefaultMassScaling;
            bool foundState = false;

            if (bagController)
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

            // Calculation logic matching GetEffectiveCoefficient
            float mass = bagController ? bagController.baggedMass : 0f;
            float maxCapacity = bagController ? CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;
            float massFraction = bagController ? (bagController.baggedMass / maxCapacity) : 0f;
            float effectiveCoef = foundState ? baseDamageCoef : (baseDamageCoef + (massScaling * massFraction));

            var drifterBody = bagController ? bagController.GetComponent<CharacterBody>() : null;
            float damageStat = drifterBody ? drifterBody.damage : 0f;
            float baseDamage = damageStat * effectiveCoef;
            float armor = 0f;

            // Calculate armor reduction
            var targetBody = target.GetComponent<CharacterBody>();
            if (targetBody) armor = targetBody.armor;
            float armorFactor = armor >= 0 ? (100f / (100f + armor)) : (2f - (100f / (100f - armor)));
            float mitigatedDamage = baseDamage * armorFactor;

            float finalDamage = GetPredictedDamage(bagController, target);

            // Use DryRunDamageCalculator for accurate damage prediction
            var dryRunResult = default(DryRunDamageCalculator.DryRunResult);
            if (drifterBody && targetBody != null)
            {
                float dryRunBaseDamage = damageStat * effectiveCoef;
                dryRunResult = DryRunDamageCalculator.CalculateDamage(drifterBody, targetBody, dryRunBaseDamage);
            }

            // Priority 0: JunkCubeController
            var junkController = target.GetComponent<JunkCubeController>();
            var body = target.GetComponent<CharacterBody>(); // Move declaration up

            if (junkController)
            {
                var field = typeof(JunkCubeController).GetField("_maxActivationCount", BindingFlags.NonPublic | BindingFlags.Instance);
                int maxCount = field != null ? (int)field.GetValue(junkController) : 3;
                float frac = maxCount > 0 ? 1f / maxCount : 0f;
                Log.Info($"  FractionPath: JUNK_CUBE (ActivationCount logic: 1/{maxCount} = {frac:F3})");
            }
            // Priority 1: CharacterBody (Health)
            else if (body && body.healthComponent)
            {
                float totalHealth = body.healthComponent.fullCombinedHealth;
                float frac = totalHealth > 0f ? Mathf.Clamp01(finalDamage / totalHealth) : 1f;
                Log.Info($"  FractionPath: HEALTH (hp={body.healthComponent.combinedHealth:F1}/{totalHealth:F1}, previewFrac={frac:F3})");
            }
            // Priority 2: SpecialObjectAttributes (Durability)
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
