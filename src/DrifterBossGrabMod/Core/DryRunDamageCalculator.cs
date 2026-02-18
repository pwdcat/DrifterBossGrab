using RoR2;
using UnityEngine;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Core
{
    // Predicts damage (Delicate Watch, Crowbar, Boss Damage Bonus, etc.)
    public static class DryRunDamageCalculator
    {
        // Result of a dry run damage calculation
        public struct DryRunResult
        {
            public float baseDamage;           // Original damage from DamageInfo
            public float modifiedDamage;       // After attacker-side modifiers (items, crit, etc.)
            public float mitigatedDamage;      // After armor/defense
            public float finalDamage;          // Final damage that would be dealt

            public bool wouldCrit;
            public float critMultiplier;

            public List<string> activeModifiers;

            public float itemDamageMultiplier;
        }
        public static DryRunResult CalculateDamage(
            CharacterBody attackerBody,
            CharacterBody targetBody,
            float baseDamage,
            DamageType damageType = DamageType.Generic)
        {
            var result = new DryRunResult
            {
                baseDamage = baseDamage,
                modifiedDamage = baseDamage,
                mitigatedDamage = baseDamage,
                finalDamage = baseDamage,
                wouldCrit = false,
                critMultiplier = 1f,
                activeModifiers = new List<string>(),
                itemDamageMultiplier = 1f
            };

            if (attackerBody == null || targetBody == null || baseDamage <= 0f)
            {
                return result;
            }

            float damage = baseDamage;
            var targetHealthComponent = targetBody.healthComponent;

            // ========================================
            // ATTACKER-SIDE DAMAGE MODIFIERS
            // HealthComponent.TakeDamageProcess
            // ========================================

            // --- Crit Check ---
            // Crit multiplier
            result.wouldCrit = attackerBody.RollCrit();
            if (result.wouldCrit)
            {
                result.critMultiplier = attackerBody.critMultiplier;
                result.activeModifiers.Add($"Crit(x{result.critMultiplier:F2})");
            }

            // --- Item Modifiers (from attacker's inventory) ---
            if (attackerBody.inventory != null)
            {
                // Lines 874-881: Crowbar - +75% per stack when target >= 90% health
                if (targetHealthComponent != null)
                {
                    float targetHealthFraction = targetHealthComponent.combinedHealth / targetHealthComponent.fullCombinedHealth;
                    if (targetHealthFraction >= 0.9f)
                    {
                        int crowbarStacks = attackerBody.inventory.GetItemCountEffective(RoR2Content.Items.Crowbar);
                        if (crowbarStacks > 0)
                        {
                            float crowbarBonus = 1f + 0.75f * crowbarStacks;
                            result.itemDamageMultiplier *= crowbarBonus;
                            result.activeModifiers.Add($"Crowbar(x{crowbarStacks}, +{(crowbarBonus - 1f) * 100:F0}%)");
                        }
                    }
                }

                // Nearby Damage Bonus
                int nearbyDamageStacks = attackerBody.inventory.GetItemCountEffective(RoR2Content.Items.NearbyDamageBonus);
                if (nearbyDamageStacks > 0)
                {
                    float distance = Vector3.Distance(attackerBody.corePosition, targetBody.corePosition);
                    if (distance <= 13f) // 13^2 = 169, matching game code
                    {
                        float nearbyBonus = 1f + nearbyDamageStacks * 0.2f;
                        result.itemDamageMultiplier *= nearbyBonus;
                        result.activeModifiers.Add($"NearbyDamage(x{nearbyDamageStacks}, dist={distance:F1})");
                    }
                }

                // FragileDamageBonus (Delicate Watch)
                int fragileStacks = attackerBody.inventory.GetItemCountEffective(DLC1Content.Items.FragileDamageBonus);
                if (fragileStacks > 0)
                {
                    float fragileBonus = 1f + fragileStacks * 0.2f;
                    result.itemDamageMultiplier *= fragileBonus;
                    result.activeModifiers.Add($"DelicateWatch(x{fragileStacks}, +{(fragileBonus - 1f) * 100:F0}%)");
                }

                // Boss Damage Bonus
                if (targetBody.isBoss)
                {
                    int bossDamageStacks = attackerBody.inventory.GetItemCountEffective(RoR2Content.Items.BossDamageBonus);
                    if (bossDamageStacks > 0)
                    {
                        float bossBonus = 1f + 0.2f * bossDamageStacks;
                        result.itemDamageMultiplier *= bossBonus;
                        result.activeModifiers.Add($"BossDamage(x{bossDamageStacks})");
                    }
                }
            }

            // Apply item damage multiplier
            damage *= result.itemDamageMultiplier;

            // --- Apply Crit Multiplier ---
            if (result.wouldCrit)
            {
                damage *= result.critMultiplier;
            }

            // --- Target Debuffs ---
            // Death Mark
            if (targetBody.HasBuff(RoR2Content.Buffs.DeathMark))
            {
                damage *= 1.5f;
                result.activeModifiers.Add("DeathMark(+50%)");
            }

            result.modifiedDamage = damage;

            // ========================================
            // TARGET-SIDE DAMAGE MITIGATION
            // HealthComponent.TakeDamageProcess
            // ========================================

            // --- Armor Mitigation ---
            bool bypassArmor = (damageType & DamageType.BypassArmor) > 0;
            if (!bypassArmor)
            {
                float armor = targetBody.armor;

                // Add adaptive armor if applicable
                // float adaptiveArmor = targetHealthComponent?.adaptiveArmorValue ?? 0f;
                // armor += adaptiveArmor;

                // Check for AOE resistance
                bool isAOE = (damageType & DamageType.AOE) > 0;
                if ((targetBody.bodyFlags & CharacterBody.BodyFlags.ResistantToAOE) > 0 && isAOE)
                {
                    armor += 300f;
                }

                // Armor formula: reduction = 1 - armor/(armor + 100)
                float armorFactor;
                if (armor >= 0f)
                {
                    armorFactor = 1f - armor / (armor + 100f);
                }
                else
                {
                    // Negative armor increases damage
                    armorFactor = 2f - 100f / (100f - armor);
                }

                damage = Mathf.Max(1f, damage * armorFactor);
                result.activeModifiers.Add($"Armor({armor:F0}, factor={armorFactor:F2})");
            }

            // --- Armor Plate ---
            // -5 damage per stack, minimum 1
            if (targetBody.inventory != null)
            {
                int armorPlateStacks = targetBody.inventory.GetItemCountEffective(RoR2Content.Items.ArmorPlate);
                if (armorPlateStacks > 0)
                {
                    float reduction = 5f * armorPlateStacks;
                    damage = Mathf.Max(1f, damage - reduction);
                    result.activeModifiers.Add($"ArmorPlate(-{reduction:F0})");
                }
            }
            // --- Bonus to Low Health ---
            if ((damageType & DamageType.BonusToLowHealth) > 0)
            {
                float lowHealthBonus = Mathf.Lerp(3f, 1f, targetHealthComponent?.combinedHealthFraction ?? 1f);
                damage *= lowHealthBonus;
                result.activeModifiers.Add($"LowHealthBonus(x{lowHealthBonus:F2})");
            }

            // --- Lunar Shell ---
            if (targetBody.HasBuff(RoR2Content.Buffs.LunarShell))
            {
                float maxDamage = targetBody.maxHealth * 0.1f;
                if (damage > maxDamage)
                {
                    damage = maxDamage;
                    result.activeModifiers.Add("LunarShell(capped)");
                }
            }

            result.mitigatedDamage = damage;
            result.finalDamage = damage;

            return result;
        }

        // Simplified method that returns just the final predicted damage
        public static float GetPredictedDamage(
            CharacterBody attackerBody,
            CharacterBody targetBody,
            float baseDamage,
            DamageType damageType = DamageType.Generic)
        {
            return CalculateDamage(attackerBody, targetBody, baseDamage, damageType).finalDamage;
        }
    }
}
