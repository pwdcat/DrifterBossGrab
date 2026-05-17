#nullable enable
using RoR2;
using UnityEngine;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Balance
{

    public static class CharacterFlagMassBonus
    {

        public static float ApplyFlagBonus(GameObject baggedObject, float baseMass)
        {
            if (baggedObject == null) return baseMass;

            var characterBody = baggedObject.GetComponent<CharacterBody>();
            if (characterBody == null) return baseMass;

            if (!PluginConfig.Instance.EnableBalance.Value)
                return baseMass;

            float highestMassBonusPercent = 1f;

            var localVars = new Dictionary<string, float>
            {
                { "B", baseMass }
            };

            void CheckFlag(bool condition, string flagMultiplierFormula)
            {
                if (condition)
                {

                    float flagMultiplier = FormulaParser.Evaluate(flagMultiplierFormula, characterBody, localVars);
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

            float allFlagMultiplier = FormulaParser.Evaluate(cfg.AllFlagMultiplier.Value, characterBody, localVars);

            if (highestMassBonusPercent != 1f || allFlagMultiplier != 1f)
            {

                totalMass *= allFlagMultiplier * highestMassBonusPercent;
            }

            return totalMass;
        }
    }
}
