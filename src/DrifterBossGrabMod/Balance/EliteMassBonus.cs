using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Balance
{
    // System for applying elite mass bonus to bagged objects
    public static class EliteMassBonus
    {
        // Applies elite mass bonus to an object's mass
        public static float ApplyEliteBonus(GameObject baggedObject, float baseMass)
        {
            if (baggedObject == null) return baseMass;

            var characterBody = baggedObject.GetComponent<CharacterBody>();
            if (characterBody == null || !characterBody.isElite)
            {
                return baseMass; // Not an elite
            }

            // Only apply elite mass bonus when EnableBalance is true
            float bonusPercent = PluginConfig.Instance.EnableBalance.Value
                ? PluginConfig.Instance.EliteMassBonusPercent.Value
                : 0f;
            float eliteMass = baseMass * (1f + (bonusPercent / 100f));

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[EliteMassBonus] Applied elite bonus: Base={baseMass}, Bonus%={bonusPercent}, Result={eliteMass}");
            }

            return eliteMass;
        }

        // Checks if an object is an elite
        public static bool IsElite(GameObject obj)
        {
            if (obj == null) return false;

            var characterBody = obj.GetComponent<CharacterBody>();
            return characterBody != null && characterBody.isElite;
        }
    }
}
