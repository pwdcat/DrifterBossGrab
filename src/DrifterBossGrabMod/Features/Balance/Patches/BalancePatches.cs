#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    // Patches for balance features (capacity scaling, elite bonus, overencumbrance)
    public static class BalancePatches
    {
        private static readonly FieldInfo _bagObjectMassField = AccessTools.Field(typeof(EntityStates.Drifter.EmptyBag), "bagObjectMass");
        private static readonly FieldInfo _projectileBaseSpeedField = AccessTools.Field(typeof(EntityStates.Drifter.EmptyBag), "projectileBaseSpeed");
        private static readonly FieldInfo _maxDistanceField = AccessTools.Field(typeof(EntityStates.Drifter.EmptyBag), "maxDistance");
        private static readonly FieldInfo _airKnockbackForceField = AccessTools.Field(typeof(EntityStates.Drifter.EmptyBag), "airKnockbackForce");

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
                if (PluginConfig.Instance.EnableBalance.Value && !PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed))
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), "OnEnter")]
        public class EmptyBag_OnEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                float projectileBaseSpeed = (float)_projectileBaseSpeedField.GetValue(__instance);
                float maxSpeedCap = PluginConfig.Instance.GetMaxLaunchSpeed();

                float floorReference = PluginConfig.Instance.IsMaxLaunchSpeedInfinite ? 30f : maxSpeedCap;
                float speedFloor = floorReference * 0.2f; // ~6.0

                if (projectileBaseSpeed < speedFloor)
                {
                    float baggedMass = (float)_bagObjectMassField.GetValue(__instance);
                    float distanceFloor = speedFloor * 0.66f; // ~4.0

                    _projectileBaseSpeedField.SetValue(__instance, speedFloor);
                    _maxDistanceField.SetValue(__instance, distanceFloor);
                }
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), "FireProjectile")]
        public class EmptyBag_FireProjectile_Patch
        {
            private static float _originalMass = 0f;

            [HarmonyPrefix]
            public static void Prefix(EntityStates.Drifter.EmptyBag __instance)
            {
                if (!PluginConfig.Instance.EnableBalance.Value || PluginConfig.Instance.IsMaxLaunchSpeedInfinite) return;

                float bagObjectMass = (float)_bagObjectMassField.GetValue(__instance);
                float airKnockbackForce = (float)_airKnockbackForceField.GetValue(__instance);
                float maxSpeedCap = PluginConfig.Instance.GetMaxLaunchSpeed();
                float vanillaMaxMass = DrifterBagController.maxMass; // Usually 700

                // Proportionalize recoil to the speed cap.
                // Ratio: 4500 force / 30 speed = 150
                float maxRecoilMagnitude = maxSpeedCap * 150f;
                float currentRecoilMagnitude = Mathf.Abs(airKnockbackForce) * (bagObjectMass / vanillaMaxMass);

                if (currentRecoilMagnitude > maxRecoilMagnitude)
                {
                    _originalMass = bagObjectMass;
                    float clampedMass = (maxRecoilMagnitude / Mathf.Abs(airKnockbackForce)) * vanillaMaxMass;

                    _bagObjectMassField.SetValue(__instance, clampedMass);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance)
            {
                if (PluginConfig.Instance.EnableBalance.Value && _originalMass > 0f)
                {
                    _bagObjectMassField.SetValue(__instance, _originalMass);
                    _originalMass = 0f;
                }
            }
        }

        // Patch to cap manual player throw force
        [HarmonyPatch(typeof(DrifterBagController), "HandlePlayerThrowVelocity")]
        public class DrifterBagController_HandlePlayerThrowVelocity_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(DrifterBagController __instance)
            {
                if (PluginConfig.Instance.EnableBalance.Value && !PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed))
                {
                    if (__instance.playerThrowForce > maxLaunchSpeed)
                    {
                        __instance.playerThrowForce = maxLaunchSpeed;
                    }
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
                if (PluginConfig.Instance.EnableBalance.Value && !PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed) && fireProjectileInfo.speedOverride > 0f)
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }
            }
        }
    }
}
