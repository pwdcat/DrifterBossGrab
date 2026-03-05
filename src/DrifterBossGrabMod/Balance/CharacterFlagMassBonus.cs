using RoR2;
using UnityEngine;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Balance
{
    // Handles mass bonus calculations based on character flags.
    // Applies the highest multiplier among all enabled flags.
    public static class CharacterFlagMassBonus
    {
        // Applies flag-based mass bonus to a bagged object.
        public static float ApplyFlagBonus(GameObject baggedObject, float baseMass)
        {
            if (baggedObject == null) return baseMass;

            var characterBody = baggedObject.GetComponent<CharacterBody>();
            if (characterBody == null) return baseMass;

            if (!PluginConfig.Instance.EnableBalance.Value)
                return baseMass;

            // Check each flag and collect multipliers
            float highestMassBonusPercent = 1f;

            // Prepare variables for formula parsing
            var formulaVars = new Dictionary<string, float>
            {
                { "H", characterBody.maxHealth },
                { "BH", characterBody.baseMaxHealth },
                { "L", characterBody.level },
                { "B", baseMass },
                { "S", RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1 }
            };

            void CheckFlag(bool condition, string flagMultiplierFormula)
            {
                if (condition)
                {
                    // Parse and evaluate the formula-based flag multiplier
                    float flagMultiplier = FormulaParser.Evaluate(flagMultiplierFormula, formulaVars);
                    if (flagMultiplier > 0f)
                    {
                        highestMassBonusPercent = Mathf.Max(highestMassBonusPercent, flagMultiplier);
                    }
                }
            }

            var cfg = PluginConfig.Instance;

            CheckFlag(characterBody.isElite, cfg.EliteFlagMultiplier.Value);
            CheckFlag(characterBody.isBoss, cfg.BossFlagMultiplier.Value);
            CheckFlag(characterBody.isChampion, cfg.ChampionFlagMultiplier.Value);
            CheckFlag(characterBody.isPlayerControlled, cfg.PlayerFlagMultiplier.Value);
            CheckFlag(characterBody.master != null && characterBody.master.minionOwnership != null, cfg.MinionFlagMultiplier.Value);
            CheckFlag((characterBody.bodyFlags & CharacterBody.BodyFlags.Drone) != 0, cfg.DroneFlagMultiplier.Value);
            CheckFlag((characterBody.bodyFlags & CharacterBody.BodyFlags.Mechanical) != 0, cfg.MechanicalFlagMultiplier.Value);
            CheckFlag((characterBody.bodyFlags & CharacterBody.BodyFlags.Void) != 0, cfg.VoidFlagMultiplier.Value);

            float totalMass = baseMass;

            // Apply ALL flag multiplier (universal multiplier that applies to ALL enemies)
            float allFlagMultiplier = FormulaParser.Evaluate(cfg.AllFlagMultiplier.Value, formulaVars);

            // Apply flag multiplier directly (multiplicative stacking with ALL flag)
            if (highestMassBonusPercent != 1f || allFlagMultiplier != 1f)
            {
                // The ALL flag stacks multiplicatively with the highest specific flag
                totalMass *= allFlagMultiplier * highestMassBonusPercent;
            }

            return totalMass;
        }
    }
}
