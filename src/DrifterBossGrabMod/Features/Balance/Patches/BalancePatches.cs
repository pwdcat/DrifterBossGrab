#nullable enable
using System;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.API;

namespace DrifterBossGrabMod.Patches
{

    // ========================================================================================
    // BALANCE PATCHES
    // ========================================================================================
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

                if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed))
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }
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
                            originalSpeed = 60f;
                        }
                    }

                    float originalMaxDistance = 60f;
                    if (Mathf.Abs(num) > 0.0001f)
                    {
                        originalMaxDistance = __instance.maxDistance / num;
                    }

                    float finalSpeed = originalSpeed * clampedNum;
                    float finalMaxDistance = originalMaxDistance * clampedNum;

                    ReflectionCache.EmptyBag.ProjectileBaseSpeed.SetValue(__instance, finalSpeed);
                    __instance.maxDistance = finalMaxDistance;
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

                    float drifterMass = 100f;
                    if (__instance.characterBody != null && __instance.characterBody.characterMotor != null)
                    {
                        drifterMass = __instance.characterBody.characterMotor.mass;
                    }
                    if (drifterMass <= 0f) drifterMass = 100f;

                    float normalRecoilSpeed = Mathf.Abs(_originalAirKnockbackForce * bagObjectMass / (maxMass * drifterMass));

                    if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed))
                    {
                        if (bagObjectMass > 0f && normalRecoilSpeed > maxLaunchSpeed)
                        {
                            float sign = Mathf.Sign(_originalAirKnockbackForce);
                            __instance.airKnockbackForce = sign * maxLaunchSpeed * maxMass * drifterMass / bagObjectMass;
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
                if (!PluginConfig.Instance.EnableBalance.Value || fireProjectileInfo.owner == null ||
                    fireProjectileInfo.owner.GetComponent<DrifterBagController>() == null ||
                    fireProjectileInfo.projectilePrefab == null ||
                    fireProjectileInfo.projectilePrefab.GetComponent<ThrownObjectProjectileController>() == null) return;

                if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite && float.TryParse(PluginConfig.Instance.MaxLaunchSpeed.Value, out float maxLaunchSpeed) && fireProjectileInfo.speedOverride > 0f)
                {
                    fireProjectileInfo.speedOverride = Mathf.Min(fireProjectileInfo.speedOverride, maxLaunchSpeed);
                }
            }
        }

        [HarmonyPatch(typeof(CharacterBody), "UpdateFireTrail")]
        public class BaggedBlazingTrailPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(CharacterBody __instance, ref DamageTrail? ___fireTrail)
            {
                if (!PluginConfig.Instance.EnableBalance.Value || EliteThrowEffectsAPI.IsGrabEffectEnabled("Blazing") ||
                    !__instance.HasBuff(RoR2Content.Buffs.AffixRed)) return true;
                var seat = __instance.currentVehicle;
                if (!seat || seat.NetworkpassengerBodyObject != __instance.gameObject ||
                    !seat.GetComponentInParent<DrifterBagController>()) return true;
                if (___fireTrail)
                {
                    ___fireTrail!.damagePerSecond = 0f;
                    ___fireTrail.enabled = false;
                    UnityEngine.Object.Destroy(___fireTrail.gameObject);
                    ___fireTrail = null;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), nameof(DrifterBagController.AssignPassenger))]
        public class EliteGrabEffectPatches
        {
            internal static void OnServerDamageDealt(DamageReport report)
            {
                if (!NetworkServer.active || !PluginConfig.Instance.EnableBalance.Value || report.damageDealt <= 0f ||
                    report.damageInfo.rejected || report.damageInfo.dotIndex != DotController.DotIndex.None ||
                    report.damageInfo.delayedDamageSecondHalf || !report.attacker || !report.victimBody ||
                    report.attacker == report.victimBody.gameObject || !report.victim || !report.victim.alive) return;
                var bag = report.victimBody.GetComponent<DrifterBagController>();
                if (bag) EliteThrowEffectsAPI.ApplyGrabHitEffects(bag, report.victimBody);
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static void Prefix(DrifterBagController __instance, GameObject passengerObject, out bool __state)
            {
                __state = false;
                if (!NetworkServer.active || !PluginConfig.Instance.EnableBalance.Value || !passengerObject ||
                    DrifterBossGrabPlugin.IsSwappingPassengers || PersistenceSceneHandler.IsRestoringFromSceneChange() ||
                    ProjectileRecoveryPatches.IsInProjectileState(passengerObject)) return;
                var body = passengerObject.GetComponent<CharacterBody>();
                __state = body && !body.currentVehicle && !DrifterBagAPI.IsObjectInBag(__instance, passengerObject);
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(DrifterBagController __instance, GameObject passengerObject, bool __state)
            {
                if (!__state || !NetworkServer.active || !PluginConfig.Instance.EnableBalance.Value || !passengerObject) return;
                var passenger = passengerObject.GetComponent<CharacterBody>();
                var grabber = __instance.GetComponent<CharacterBody>();
                if (!passenger || !grabber || passenger.isPlayerControlled || !passenger.healthComponent ||
                    !passenger.healthComponent.alive || !grabber.healthComponent || !grabber.healthComponent.alive ||
                    !passenger.teamComponent || !grabber.teamComponent ||
                    passenger.teamComponent.teamIndex == grabber.teamComponent.teamIndex) return;
                var seat = passenger.currentVehicle;
                if (!seat || seat.NetworkpassengerBodyObject != passengerObject) return;
                if (seat != __instance.vehicleSeat && seat.GetComponentInParent<DrifterBagController>() != __instance &&
                    (!BagPatches.GetState(__instance).AdditionalSeats.TryGetValue(passengerObject, out var additionalSeat) || seat != additionalSeat)) return;
                EliteThrowEffectsAPI.ApplyGrabEffects(passenger, grabber);
            }
        }

        [HarmonyPatch(typeof(ProjectileManager), "InitializeProjectile")]
        public class EliteThrowEffectPatches
        {
            [HarmonyPrefix]
            private static void Prefix(ProjectileController projectileController, ref FireProjectileInfo fireProjectileInfo)
            {
                if (!NetworkServer.active || !PluginConfig.Instance.EnableBalance.Value) return;
                var info = fireProjectileInfo;
                if (!info.owner || !info.projectilePrefab || info.damage <= 0f ||
                    info.projectilePrefab.name != "ThrownObjectProjectile" ||
                    !info.damageTypeOverride.HasValue || info.damageTypeOverride.Value.damageSource != DamageSource.Utility)
                    return;
                var bag = info.owner.GetComponent<DrifterBagController>();
                var prefab = info.projectilePrefab.GetComponent<ThrownObjectProjectileController>();
                if (!bag || !prefab) return;
                var passenger = info.passenger ? info.passenger : bag.baggedObject;
                var body = passenger ? passenger.GetComponent<CharacterBody>() : null;
                var ownerBody = info.owner.GetComponent<CharacterBody>();
                if (!body || body!.isPlayerControlled || !body.teamComponent || !ownerBody ||
                    !ownerBody.teamComponent || body.teamComponent.teamIndex == ownerBody.teamComponent.teamIndex)
                    return;

                var additions = EliteThrowEffectsAPI.Resolve(body, prefab);
                if (additions.damageType == DamageType.Generic && additions.damageTypeExtended == DamageTypeExtended.Generic)
                    return;
                var original = info.damageTypeOverride.Value;
                if (original.damageType == DamageType.Generic && original.damageTypeExtended == DamageTypeExtended.Generic)
                {
                    var damage = info.projectilePrefab.GetComponent<ProjectileDamage>();
                    if (damage) original = EliteThrowEffectsAPI.MergeFlags(original, damage.damageType);
                }
                fireProjectileInfo.damageTypeOverride = EliteThrowEffectsAPI.MergeFlags(original, additions);
                var passengerHealth = body.healthComponent;
                var explosion = projectileController.GetComponent<ProjectileExplosion>();
                if (passengerHealth && explosion)
                {
                    var addedOnly = EliteThrowEffectsAPI.AddedFlags(original, additions);
                    explosion.OnModifyBlastAttack += blast =>
                    {
                        blast.modifyOutgoingBlastInfoCallback += (ref BlastAttack.BlastAttackDamageInfo hit, BlastAttack.HitPoint point) =>
                        {
                            if (hit.hitHealthComponent == passengerHealth)
                                hit.damageType = EliteThrowEffectsAPI.RemoveFlags(hit.damageType, addedOnly);
                        };
                    };
                }
            }
        }
    }
}
