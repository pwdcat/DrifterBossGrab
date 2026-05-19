#nullable enable
using System;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{

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

                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
                if (drifterBagController != null)
                {

                    if (drifterBagController.hasAuthority)
                    {
                        string slotFormula = PluginConfig.Instance.SlotScalingFormula.Value?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(slotFormula) && slotFormula != "0")
                        {
                            UIPatches.UpdateMassCapacityUIOnCapacityChange(drifterBagController);
                        }
                    }

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

        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), "ModifyProjectile")]
        public class EmptyBag_ModifyProjectile_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance, ref FireProjectileInfo fireProjectileInfo)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                float incomingSpeed = fireProjectileInfo.speedOverride;
                if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed))
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }

                Log.Debug($"[EmptyBag_ModifyProjectile_Patch] Incoming speedOverride={incomingSpeed:F1} -> Capped={fireProjectileInfo.speedOverride:F1}");
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), nameof(EntityStates.Drifter.EmptyBag.OnEnter))]
        public class EmptyBag_OnEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance)
            {
                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    float bagObjectMass = bagController.baggedMass;
                    float num = 1f - bagObjectMass * 0.000714f;
                    float clampedNum = Mathf.Max(num, 0.15f);

                    float originalSpeed = 0f;
                    if (Mathf.Abs(num) > 0.0001f)
                    {
                        float baseSpeed = (float)ReflectionCache.EmptyBag.ProjectileBaseSpeed.GetValue(__instance);
                        originalSpeed = baseSpeed / num;
                    }
                    else
                    {
                        var component = __instance.projectilePrefab != null ? __instance.projectilePrefab.GetComponent<ProjectileSimple>() : null;
                        if (component != null)
                        {
                            originalSpeed = component.desiredForwardSpeed;
                            if (originalSpeed == 0f)
                            {
#pragma warning disable CS0618
                                originalSpeed = component.velocity;
#pragma warning restore CS0618
                            }
                        }
                        if (originalSpeed == 0f)
                        {
                            originalSpeed = 60f; // Default fallback
                        }
                    }

                    float originalMaxDistance = 60f; // Default for EmptyBag in RoR2
                    if (Mathf.Abs(num) > 0.0001f)
                    {
                        originalMaxDistance = __instance.maxDistance / num;
                    }

                    float finalSpeed = originalSpeed * clampedNum;
                    float finalMaxDistance = originalMaxDistance * clampedNum;

                    ReflectionCache.EmptyBag.ProjectileBaseSpeed.SetValue(__instance, finalSpeed);
                    __instance.maxDistance = finalMaxDistance;

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[EmptyBag_OnEnter_Patch] Mass={bagObjectMass:F1}, num={num:F4}, clampedNum={clampedNum:F4}");
                        Log.Debug($"[EmptyBag_OnEnter_Patch] Speed: Original={originalSpeed:F1} -> Target={finalSpeed:F1}");
                        Log.Debug($"[EmptyBag_OnEnter_Patch] Distance: Original={originalMaxDistance:F1} -> Target={finalMaxDistance:F1}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), "FireProjectile")]
        public class EmptyBag_FireProjectile_Patch
        {
            private static float _originalAirKnockbackForce;

            [HarmonyPrefix]
            public static void Prefix(EntityStates.Drifter.EmptyBag __instance)
            {
                _originalAirKnockbackForce = __instance.airKnockbackForce;

                if (!PluginConfig.Instance.EnableBalance.Value) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    float bagObjectMass = bagController.baggedMass;
                    float maxMass = RoR2.DrifterBagController.maxMass;

                    float drifterMass = 100f; // Default player mass fallback
                    if (__instance.characterBody != null && __instance.characterBody.characterMotor != null)
                    {
                        drifterMass = __instance.characterBody.characterMotor.mass;
                    }
                    if (drifterMass <= 0f) drifterMass = 100f;

                    // Note: Recoil Velocity = (airKnockbackForce * bagObjectMass) / (maxMass * drifterMass) due to motor division by body mass
                    float normalRecoilSpeed = Mathf.Abs(_originalAirKnockbackForce * bagObjectMass / (maxMass * drifterMass));

                    bool isGrounded = __instance.characterBody != null && __instance.characterBody.characterMotor != null && __instance.characterBody.characterMotor.isGrounded;

                    Log.Debug($"[EmptyBag_FireProjectile_Patch] Prefix: Grounded={isGrounded}, Original airKnockbackForce={_originalAirKnockbackForce:F1}, Target Mass={bagObjectMass:F1}, Drifter Mass={drifterMass:F1}, Predicted Recoil Speed={normalRecoilSpeed:F2} m/s");

                    if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed))
                    {
                        if (bagObjectMass > 0f && normalRecoilSpeed > maxLaunchSpeed)
                        {
                            float sign = Mathf.Sign(_originalAirKnockbackForce);
                            __instance.airKnockbackForce = sign * maxLaunchSpeed * maxMass * drifterMass / bagObjectMass;

                            Log.Debug($"[EmptyBag_FireProjectile_Patch] CLAMPED airKnockbackForce to {__instance.airKnockbackForce:F1} (Recoil speed capped to MaxLaunchSpeed={maxLaunchSpeed:F1} m/s)");
                        }
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance)
            {
                __instance.airKnockbackForce = _originalAirKnockbackForce;
            }
        }

        [HarmonyPatch(typeof(ProjectileManager), "FireProjectile", new Type[] { typeof(FireProjectileInfo) })]
        public class ProjectileManager_FireProjectile_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(ref FireProjectileInfo fireProjectileInfo)
            {
                if (!PluginConfig.Instance.EnableBalance.Value) return;

                float incomingSpeed = fireProjectileInfo.speedOverride;
                if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed) && fireProjectileInfo.speedOverride > 0f)
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }

                Log.Debug($"[ProjectileManager_FireProjectile_Patch] Incoming speedOverride={incomingSpeed:F1} -> Capped={fireProjectileInfo.speedOverride:F1}");
            }
        }
    }
}
