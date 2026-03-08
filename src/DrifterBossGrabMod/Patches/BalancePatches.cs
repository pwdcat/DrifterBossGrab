using System;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    // Patches for balance features (capacity scaling, elite bonus, overencumbrance)
    public static class BalancePatches
    {
        [HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.maxStock), MethodType.Setter)]
        public class GenericSkill_maxStock_Setter_Patch
        {
            static void Postfix(GenericSkill __instance)
            {
                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    Balance.CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            }
        }


        [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
        public class CharacterBody_RecalculateStats_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterBody __instance)
            {
                // Only apply overencumbrance when EnableBalance is true
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
                if (drifterBagController != null)
                {
                    // Update UI only if we have authority (local player)
                    if (drifterBagController.hasAuthority)
                    {
                        string slotFormula = PluginConfig.Instance.SlotScalingFormula.Value?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(slotFormula) && slotFormula != "0")
                        {
                            UIPatches.UpdateMassCapacityUIOnCapacityChange(drifterBagController);
                        }
                    }

                    // Apply overencumbrance to all players (host and clients)
                    // Each player's debuff is based on their own bag's state
                    if (PluginConfig.Instance.OverencumbranceMax.Value > 0)
                    {
                        Balance.OverencumbranceSystem.ApplyOverencumbrance(__instance, drifterBagController);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.OnDestroy))]
        public class CharacterBody_OnDestroy_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(CharacterBody __instance)
            {
                Balance.OverencumbranceSystem.CleanupCharacterBody(__instance);
            }
        }

        // Patch to cap throw speed from EmptyBag state
        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), "ModifyProjectile")]
        public class EmptyBag_ModifyProjectile_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance, ref FireProjectileInfo fireProjectileInfo)
            {
                // Apply max launch speed cap if configured
                string maxLaunchSpeedStr = PluginConfig.Instance.MaxLaunchSpeed.Value.Trim().ToUpper();
                if (maxLaunchSpeedStr != "INF" && maxLaunchSpeedStr != "INFINITY" && float.TryParse(maxLaunchSpeedStr, out float maxLaunchSpeed))
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }
            }
        }

        // Patch to cap launch speed for all projectiles (covers both throw and breakout)
        [HarmonyPatch(typeof(ProjectileManager), "FireProjectile", new Type[] { typeof(FireProjectileInfo) })]
        public class ProjectileManager_FireProjectile_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(ref FireProjectileInfo fireProjectileInfo)
            {
                // Apply max launch speed cap if configured
                string maxLaunchSpeedStr = PluginConfig.Instance.MaxLaunchSpeed.Value.Trim().ToUpper();
                if (maxLaunchSpeedStr != "INF" && maxLaunchSpeedStr != "INFINITY" && float.TryParse(maxLaunchSpeedStr, out float maxLaunchSpeed) && fireProjectileInfo.speedOverride > 0f)
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }
            }
        }
    }
}
