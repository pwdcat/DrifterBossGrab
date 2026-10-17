#nullable enable
using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using EntityStates.Drifter;
using EntityStates.Drifter.Bag;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.Patches
{

    // ========================================================================================
    // REPOSSESS PATCHES
    // ========================================================================================
    public static class RepossessPatches
    {

        public static float GetBaseObjectMass(GameObject targetObject)
        {
            float mass = 0f;
            if (targetObject.TryGetComponent<TrialGongInteraction>(out _))
            {
                mass = targetObject.TryGetComponent<SpecialObjectAttributes>(out var gongAttributes) && gongAttributes.massOverride > 0f
                    ? gongAttributes.massOverride : Constants.Limits.RitualGongMass;
            }
            else if (targetObject.TryGetComponent<CharacterBody>(out var body))
            {
                GameObject? prefab = null;
#if DEBUG
                try
                {
                    prefab = BodyCatalog.FindBodyPrefab(body.baseNameToken);
                }
                catch (Exception)
                {
                    prefab = null;
                }
#else
                prefab = BodyCatalog.FindBodyPrefab(body.baseNameToken);
#endif
                if (prefab == null)
                {
                    prefab = targetObject;
                }
                if (prefab.TryGetComponent<IPhysMotor>(out var prePM))
                {
                    mass = prePM.mass;
                }
            }

            if (mass <= 0f && targetObject.TryGetComponent<IPhysMotor>(out var physMotor))
            {
                mass = physMotor.mass;
            }

            if (mass <= 0f && targetObject.TryGetComponent<SpecialObjectAttributes>(out var specialObjectAttributes))
            {
                mass = specialObjectAttributes.massOverride;
            }

            if (mass <= 0f) mass = Constants.Multipliers.DefaultMassMultiplier;
            return mass;
        }

        [HarmonyPatch(typeof(DrifterBagController), "CalculateBaggedObjectMass")]
        public class DrifterBagController_CalculateBaggedObjectMass_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance, GameObject targetObject, ref float __result)
            {
                if (!targetObject)
                {
                    __result = 0f;
                    return false;
                }

                float mass = GetBaseObjectMass(targetObject);

                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    mass = Balance.CharacterFlagMassBonus.ApplyFlagBonus(targetObject, mass);
                }

                bool isInf = PluginConfig.Instance.IsMassCapInfinite;
                if (!isInf)
                {
                    float massCapValue = float.MaxValue;
                    if (float.TryParse(PluginConfig.Instance.MassCap.Value, out float parsedMassCap))
                    {
                        massCapValue = parsedMassCap;
                    }

                    if (PluginConfig.Instance.EnableBalance.Value)
                    {
                        float computedCap = Balance.CapacityScalingSystem.CalculateMassCapacity(__instance);

                        if (computedCap != float.MaxValue)
                            massCapValue = computedCap > massCapValue ? computedCap : massCapValue;
                    }

                    __result = Mathf.Clamp(mass, 0f, massCapValue);
                }
                else
                {

                    __result = Mathf.Max(mass, 0f);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "RecalculateBaggedObjectMass")]
        public class DrifterBagController_RecalculateBaggedObjectMass_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance)
            {
                float totalMass = 0f;
                var list = BagPatches.GetState(__instance).BaggedObjects;
                if (list != null)
                {
                    foreach (GameObject gameObject in list)
                    {
                        if (gameObject != null && !ProjectileRecoveryPatches.IsInProjectileState(gameObject))
                        {
                            totalMass += __instance.CalculateBaggedObjectMass(gameObject);
                        }
                    }
                }

                totalMass = Mathf.Max(totalMass, 0f);

                ReflectionCache.DrifterBagController.BaggedMass.SetValue(__instance, totalMass);

                var stateMachines = __instance.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag" && esm.state is BaggedObject baggedObject)
                    {
                        BaggedObjectPatches.UpdateBagScale(baggedObject, totalMass);
                        break;
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "OnSyncBaggedObject")]
        public class DrifterBagController_OnSyncBaggedObject_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                BagPassengerManager.ForceRecalculateMass(__instance);

                if (!UnityEngine.Networking.NetworkServer.active && __instance.baggedObject != null)
                {
                    var esms = __instance.GetComponents<EntityStateMachine>();
                    foreach (var esm in esms)
                    {
                        if (esm.customName == "Bag" && esm.state is BaggedObject baggedObject)
                        {
                            if (baggedObject.targetObject == null)
                            {
                                baggedObject.targetObject = __instance.baggedObject;
                                BaggedObjectPatches.UpdateTargetFields(baggedObject);
                            }
                            break;
                        }
                    }
                }
            }
        }
        [HarmonyPatch(typeof(DrifterBagController), "Awake")]
        public class DrifterBagController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    __instance.maxSmacks = Core.StateCalculator.CalculateMaxSmacks(__instance);
                }

                if (__instance.GetComponent<Networking.BottomlessBagNetworkController>() == null)
                {
                    __instance.gameObject.AddComponent<Networking.BottomlessBagNetworkController>();
                }
            }
        }
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_ExtendBreakoutTime
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                var targetObject = ReflectionCache.BaggedObject.TargetObject != null ? (GameObject)ReflectionCache.BaggedObject.TargetObject.GetValue(__instance) : null;

                if (targetObject == null)
                {

                    return;
                }
                if (PluginConfig.Instance.EnableBalance.Value && ReflectionCache.BaggedObject.BreakoutTime != null)
                {
                    var controller = __instance.outer?.GetComponent<DrifterBagController>();
                    var storedState = controller != null ? BaggedObjectPatches.LoadObjectState(controller, targetObject) : null;
                    var currentBreakoutTime = (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(__instance);
                    float interval = storedState != null && storedState.breakoutTime > 0f
                        ? storedState.breakoutTime
                        : currentBreakoutTime * Core.StateCalculator.CalculateBreakoutTimeMultiplier(controller, targetObject, currentBreakoutTime);
                    ReflectionCache.BaggedObject.BreakoutTime.SetValue(__instance, interval);
                }
                if (targetObject != null && UnityEngine.Networking.NetworkServer.active)
                {
                    var networkIdentity = targetObject.GetComponent<UnityEngine.Networking.NetworkIdentity>();
                    if (networkIdentity != null)
                    {
                        UnityEngine.Networking.NetworkServer.Spawn(targetObject);
                    }
                }
                bool alreadyPersisted = PersistenceObjectManager.IsObjectPersisted(targetObject!);
                if (UnityEngine.Networking.NetworkServer.active && PluginConfig.Instance.EnableObjectPersistence.Value && !alreadyPersisted)
                {
                    PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(new System.Collections.Generic.List<GameObject> { targetObject! });
                }

                var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                if (bagController != null && targetObject != null)
                {
                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                }
            }
        }

        [HarmonyPatch(typeof(SpecialObjectAttributes), "isTargetable", MethodType.Getter)]
        public class SpecialObjectAttributes_get_isTargetable
        {
            [HarmonyPrefix]
            public static bool Prefix(SpecialObjectAttributes __instance)
            {
                if (!DrifterBossGrabPlugin.IsDrifterPresent) return true;
                __instance.childSpecialObjectAttributes.RemoveAll(s => s == null);
                __instance.renderersToDisable.RemoveAll(r => r == null);
                __instance.behavioursToDisable.RemoveAll(b => b == null);
                __instance.childObjectsToDisable.RemoveAll(c => c == null);
                __instance.pickupDisplaysToDisable.RemoveAll(p => p == null);
                __instance.lightsToDisable.RemoveAll(l => l == null);
                __instance.objectsToDetach.RemoveAll(o => o == null);
                __instance.skillHighlightRenderers.RemoveAll(r => r == null);
                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance, ref bool __result)
            {
                if (!DrifterBossGrabPlugin.IsDrifterPresent) return;
                var body = __instance.gameObject.GetComponent<CharacterBody>();
                if (body)
                {
                    bool isBoss = body.isBoss;
                    bool isUngrabbable = body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                    bool canOverride = ((isBoss && PluginConfig.Instance.EnableBossGrabbing.Value) ||
                                        (isUngrabbable && PluginConfig.Instance.EnableNPCGrabbing.Value)) &&
                                       !PluginConfig.IsBlacklisted(__instance.gameObject.name);
                    if (canOverride)
                    {
                        __result = true;
                    }
                }
                if (PluginConfig.Instance.EnableLockedObjectGrabbing.Value && __instance.locked)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(RepossessBullseyeSearch), "HurtBoxPassesRequirements")]
        public class RepossessBullseyeSearch_HurtBoxPassesRequirements
        {
            [HarmonyPostfix]
            public static void Postfix(ref bool __result, HurtBox hurtBox)
            {
                __result = false;
                if (hurtBox && hurtBox.healthComponent)
                {
                    if (!hurtBox.healthComponent.alive)
                    {
                        return;
                    }

                    var body = hurtBox.healthComponent.body;
                    bool allowTargeting = false;
                    if (body)
                    {
                        if (body.isBoss && !PluginConfig.Instance.EnableBossGrabbing.Value)
                        {
                            return;
                        }
                        if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && !body.isBoss && !PluginConfig.Instance.EnableNPCGrabbing.Value)
                        {
                            return;
                        }
                        allowTargeting = true;
                    }
                    if (allowTargeting && !PluginConfig.IsBlacklisted(body!.name))
                    {
                        __result = true;
                    }
                }
            }
        }
        [HarmonyPatch(typeof(SpecialObjectAttributes), "AvoidCapture")]
        public class SpecialObjectAttributes_AvoidCapture
        {
            [HarmonyPrefix]
            public static bool Prefix(SpecialObjectAttributes __instance)
            {
                if (!DrifterBossGrabPlugin.IsDrifterPresent) return true;
                if (PluginConfig.IsBlacklisted(__instance.gameObject.name))
                {
                    return true;
                }
                var body = __instance.gameObject.GetComponent<CharacterBody>();
                if (body)
                {
                    bool isBoss = body.isBoss;
                    bool isUngrabbable = body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                    bool shouldPrevent = (isBoss && PluginConfig.Instance.EnableBossGrabbing.Value) ||
                                        (isUngrabbable && PluginConfig.Instance.EnableNPCGrabbing.Value);
                    return !shouldPrevent;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.AimRepossess), "OnEnter")]
        public class AimRepossess_OnEnter_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(EntityStates.Drifter.AimRepossess __instance)
            {

                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    __instance.searchRange *= Core.StateCalculator.CalculateGrabRangeMultiplier(__instance.characterBody);
                }
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.Repossess), "OnEnter")]
        public class Repossess_OnEnter_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(EntityStates.Drifter.Repossess __instance)
            {

                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    __instance.searchRange *= Core.StateCalculator.CalculateGrabRangeMultiplier(__instance.characterBody);
                }
            }
        }
    }

    // ========================================================================================
    // SELF GRAB PATCHES
    // ========================================================================================
    public static class SelfGrabPatches
    {
        private const float DoubleTapWindow = 0.3f;
        private static readonly float MinimumAimY = Mathf.Sin(68f * Mathf.Deg2Rad);
        private static readonly ConditionalWeakTable<CharacterBody, TapState> Taps = new();
        private static readonly ConditionalWeakTable<CharacterBody, AnimationRecovery> Animations = new();

        private sealed class AnimationRecovery
        {
            public float Speed = 1f;
            public bool Pending;
        }

        private static void RestoreAnimation(CharacterBody body)
        {
            if (!Animations.TryGetValue(body, out var recovery) || !recovery.Pending
                || body.currentVehicle != null || body.healthComponent == null
                || !body.healthComponent.alive) return;
            var model = body.modelLocator != null ? body.modelLocator.modelTransform : null;
            var animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator == null || !animator.isActiveAndEnabled) return;

            animator.speed = recovery.Speed;
            animator.SetBool("sideways", false);
            foreach (string layerName in new[] { "FullBody, Override", "MostBody, Override" })
            {
                int layer = animator.GetLayerIndex(layerName);
                int empty = Animator.StringToHash("BufferEmpty");
                if (layer >= 0 && animator.HasState(layer, empty))
                    animator.Play(empty, layer, 0f);
            }
            Animations.Remove(body);
        }

        [HarmonyPatch(typeof(BaggedObject), nameof(BaggedObject.OnEnter))]
        public class BaggedObject_OnEnter
        {
            [HarmonyPrefix]
            public static void Prefix(BaggedObject __instance)
            {
                var body = __instance.outer != null ? __instance.outer.GetComponent<CharacterBody>() : null;
                if (body == null || __instance.targetObject != body.gameObject) return;
                var model = body.modelLocator != null ? body.modelLocator.modelTransform : null;
                var animator = model != null ? model.GetComponent<Animator>() : null;
                Animations.GetValue(body, _ => new AnimationRecovery
                {
                    Speed = animator != null && animator.speed > 0f ? animator.speed : 1f
                });
            }
        }

        [HarmonyPatch(typeof(BaggedObject), nameof(BaggedObject.OnExit))]
        public class BaggedObject_OnExit
        {
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                var body = __instance.outer != null ? __instance.outer.GetComponent<CharacterBody>() : null;
                if (body == null || __instance.targetObject != body.gameObject
                    || !Animations.TryGetValue(body, out var recovery)) return;
                recovery.Pending = true;
                RestoreAnimation(body);
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), "OnPassengerExit")]
        public class VehicleSeat_OnPassengerExit
        {
            [HarmonyPostfix]
            public static void Postfix(GameObject passenger)
            {
                var body = passenger != null ? passenger.GetComponent<CharacterBody>() : null;
                if (body == null || !Animations.TryGetValue(body, out var recovery)) return;
                recovery.Pending = true;
                RestoreAnimation(body);
            }
        }

        private sealed class TapState
        {
            public bool WasDown;
            public float LastTap = float.NegativeInfinity;
        }

        internal static bool CanSelfGrab(CharacterBody body)
        {
            return PluginConfig.Instance.EnableSelfGrabbing.Value
                && body.healthComponent != null && body.healthComponent.alive
                && body.currentVehicle == null
                && !PluginConfig.IsBlacklisted(body.gameObject.name);
        }

        [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.FixedUpdate))]
        public class CharacterBody_FixedUpdate
        {
            [HarmonyPrefix]
            public static void Prefix(CharacterBody __instance)
            {
                RestoreAnimation(__instance);
                if (!__instance.hasEffectiveAuthority || __instance.inputBank == null
                    || !__instance.TryGetComponent<DrifterBagController>(out var controller)) return;

                var taps = Taps.GetValue(__instance, _ => new TapState());
                bool down = __instance.inputBank.skill3.down;
                bool pressed = down && !taps.WasDown;
                taps.WasDown = down;
                if (!CanSelfGrab(__instance) || __instance.inputBank.aimDirection.y < MinimumAimY)
                {
                    taps.LastTap = float.NegativeInfinity;
                    return;
                }
                if (!pressed) return;

                float previousTap = taps.LastTap;
                taps.LastTap = Time.fixedTime;
                if (Time.fixedTime - previousTap > DoubleTapWindow) return;
                taps.LastTap = float.NegativeInfinity;

                EntityStateMachine? aimMachine = null;
                foreach (var machine in __instance.GetComponents<EntityStateMachine>())
                    if (machine.state is AimRepossess) { aimMachine = machine; break; }
                var bodyMachine = EntityStateMachine.FindByCustomName(__instance.gameObject, "Body");
                var bagMachine = EntityStateMachine.FindByCustomName(__instance.gameObject, "Bag");
                if (bodyMachine == null || bagMachine == null) return;
                bool aiming = aimMachine != null && aimMachine.state is AimRepossess;
                bool grabbing = bodyMachine.state is Repossess;
                if (!aiming && !grabbing) return;

                if (aiming)
                {
                    AccessTools.Field(typeof(AimRepossess), "canceled").SetValue(aimMachine!.state, false);
                    aimMachine.SetNextStateToMain();
                }
                if (grabbing) bodyMachine.SetNextStateToMain();
                bagMachine.SetNextState(new BaggedObject { targetObject = __instance.gameObject });
                if (!NetworkServer.active && NetworkClient.active)
                    Networking.CycleNetworkHandler.SendGrabObjectRequest(controller, __instance.gameObject);
            }
        }
    }
}
