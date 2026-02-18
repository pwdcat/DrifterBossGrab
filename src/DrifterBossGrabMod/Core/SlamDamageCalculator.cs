using RoR2;
using UnityEngine;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Patches;

using System.Reflection;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Core
{
    // Calculates predicted slam damage for the damage preview overlay
    // Uses the SuffocateSlam damage formula:
    //   effectiveCoef = baseDamageCoef + (massScaling * baggedMass / maxMass)
    //   damage = drifterBody.damage * effectiveCoef
    public static class SlamDamageCalculator
    {
        public const float DefaultBaseDamageCoef = 2.8f;
        public const float DefaultMassScaling = 5.0f;

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
                PluginConfig.Instance.EnableAoESlamDamage.Value &&
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
                return baseDamage;
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
                var field = typeof(JunkCubeController).GetField("_maxActivationCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    int maxCount = (int)field.GetValue(junkController);
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
            float baseDamageCoef = DefaultBaseDamageCoef;
            float massScaling = DefaultMassScaling;
            bool foundState = false;

            // Try to read from the actual entity state if available
            if (bagController)
            {
                var stateMachines = bagController.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.state is EntityStates.Drifter.SuffocateSlam slamState)
                    {
                        // SuffocateSlam.OnEnter modifies damageCoefficient in-place!
                        // So if we find the state, the coefficient is ALREADY scaled.
                        baseDamageCoef = slamState.damageCoefficient;
                        foundState = true;
                        break;
                    }
                }
            }

            // Only apply mass scaling if we didn't read the already-scaled value from the state
            if (!foundState)
            {
                float massFraction = bagController ? (bagController.baggedMass / DrifterBagController.maxMass) : 0f;
                return baseDamageCoef + (massScaling * massFraction);
            }

            return baseDamageCoef;
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
            float massFraction = bagController ? (bagController.baggedMass / DrifterBagController.maxMass) : 0f;
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

            Log.Info($"[SlamCalcs] Target: {target.name}");
            Log.Info($"  Components: Coef={baseDamageCoef} (StateFound={foundState}) MassScaling={massScaling} (Only applied if !StateFound)");
            Log.Info($"  Mass: {mass}/{DrifterBagController.maxMass} (Frac={massFraction:F2})");
            Log.Info($"  EffectiveCoef: {effectiveCoef:F2}");
            Log.Info($"  Damage: {damageStat} * {effectiveCoef:F2} = {baseDamage:F1}");
            Log.Info($"  Mitigation: Armor={armor} -> Factor={armorFactor:F2} -> Mitigated={mitigatedDamage:F1}");

            if (dryRunResult.activeModifiers != null && dryRunResult.activeModifiers.Count > 0)
            {
                Log.Info($"  DryRun Modifiers: {string.Join(", ", dryRunResult.activeModifiers)}");
                Log.Info($"  DryRun Result: Base={dryRunResult.baseDamage:F1} -> Modified={dryRunResult.modifiedDamage:F1} -> Mitigated={dryRunResult.mitigatedDamage:F1} -> Final={dryRunResult.finalDamage:F1}");
                Log.Info($"  ItemMultiplier: {dryRunResult.itemDamageMultiplier:F2}");
                Log.Info($"  Crit: {(dryRunResult.wouldCrit ? $"YES (x{dryRunResult.critMultiplier:F2})" : "NO")}");
            }
            Log.Info($"  FinalDamage (preview): {finalDamage:F1}");

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
