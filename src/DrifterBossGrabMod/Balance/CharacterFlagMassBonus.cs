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
            float highestMultiplier = 0f;

            // Boolean properties
            if (characterBody.isElite)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.EliteMassBonusPercent.Value);
            }

            if (characterBody.isBoss)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.BossMassBonusPercent.Value);
            }

            if (characterBody.isChampion)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.ChampionMassBonusPercent.Value);
            }

            if (characterBody.isPlayerControlled)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.PlayerMassBonusPercent.Value);
            }

            // Minion check
            if (characterBody.master != null && characterBody.master.minionOwnership != null)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.MinionMassBonusPercent.Value);
            }

            // BodyFlags
            if ((characterBody.bodyFlags & CharacterBody.BodyFlags.Drone) != 0)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.DroneMassBonusPercent.Value);
            }

            if ((characterBody.bodyFlags & CharacterBody.BodyFlags.Mechanical) != 0)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.MechanicalMassBonusPercent.Value);
            }

            if ((characterBody.bodyFlags & CharacterBody.BodyFlags.Void) != 0)
            {
                highestMultiplier = Mathf.Max(highestMultiplier, PluginConfig.Instance.VoidMassBonusPercent.Value);
            }

            // Apply the highest multiplier
            if (highestMultiplier != 0f)
            {
                return baseMass * (1f + (highestMultiplier / 100f));
            }

            return baseMass;
        }
    }
}
