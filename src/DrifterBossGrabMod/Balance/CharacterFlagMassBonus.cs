using RoR2;
using UnityEngine;

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
            float highestMassBonusPercent = 0f;
            float highestHealthMultiplier = 0f;
            float highestLevelMultiplier = 0f;

            void CheckFlag(bool condition, float massBonus, float healthMult, float levelMult)
            {
                if (condition)
                {
                    highestMassBonusPercent = Mathf.Max(highestMassBonusPercent, massBonus);
                    highestHealthMultiplier = Mathf.Max(highestHealthMultiplier, healthMult);
                    highestLevelMultiplier = Mathf.Max(highestLevelMultiplier, levelMult);
                }
            }

            var cfg = PluginConfig.Instance;

            CheckFlag(characterBody.isElite, 
                cfg.EliteMassBonusPercent.Value, cfg.EliteBaseHealthMassMultiplier.Value, cfg.EliteLevelMassMultiplier.Value);

            CheckFlag(characterBody.isBoss, 
                cfg.BossMassBonusPercent.Value, cfg.BossBaseHealthMassMultiplier.Value, cfg.BossLevelMassMultiplier.Value);

            CheckFlag(characterBody.isChampion, 
                cfg.ChampionMassBonusPercent.Value, cfg.ChampionBaseHealthMassMultiplier.Value, cfg.ChampionLevelMassMultiplier.Value);

            CheckFlag(characterBody.isPlayerControlled, 
                cfg.PlayerMassBonusPercent.Value, cfg.PlayerBaseHealthMassMultiplier.Value, cfg.PlayerLevelMassMultiplier.Value);

            CheckFlag(characterBody.master != null && characterBody.master.minionOwnership != null, 
                cfg.MinionMassBonusPercent.Value, cfg.MinionBaseHealthMassMultiplier.Value, cfg.MinionLevelMassMultiplier.Value);

            CheckFlag((characterBody.bodyFlags & CharacterBody.BodyFlags.Drone) != 0, 
                cfg.DroneMassBonusPercent.Value, cfg.DroneBaseHealthMassMultiplier.Value, cfg.DroneLevelMassMultiplier.Value);

            CheckFlag((characterBody.bodyFlags & CharacterBody.BodyFlags.Mechanical) != 0, 
                cfg.MechanicalMassBonusPercent.Value, cfg.MechanicalBaseHealthMassMultiplier.Value, cfg.MechanicalLevelMassMultiplier.Value);

            CheckFlag((characterBody.bodyFlags & CharacterBody.BodyFlags.Void) != 0, 
                cfg.VoidMassBonusPercent.Value, cfg.VoidBaseHealthMassMultiplier.Value, cfg.VoidLevelMassMultiplier.Value);

            float totalMass = baseMass;

            // Apply Base Health Mass (additive)
            if (highestHealthMultiplier > 0f)
            {
                float baseHealth = characterBody.baseMaxHealth;
                totalMass += baseHealth * highestHealthMultiplier;
            }

            // Apply flag multiplier directly
            if (highestMassBonusPercent != 0f)
            {
                totalMass *= highestMassBonusPercent;
            }

            // Apply Level Multiplier
            if (highestLevelMultiplier > 0f)
            {
                // RoR2 considers base stats to be level 1, so additional levels are level - 1
                float levelScaling = Mathf.Max(0, characterBody.level - 1f);
                totalMass *= (1f + levelScaling * highestLevelMultiplier);
            }

            return totalMass;
        }
    }
}
