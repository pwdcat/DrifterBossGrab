using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using EntityStates.Drifter;
using EntityStates.Drifter.Bag;
namespace DrifterBossGrabMod.Patches
{
    public static class RepossessPatches
    {
        private static FieldInfo? forwardVelocityField;
        private static FieldInfo? upwardVelocityField;
        private static float? originalForwardVelocity;
        private static float? originalUpwardVelocity;
        public static void Initialize()
        {
            forwardVelocityField = AccessTools.Field(typeof(Repossess), "forwardVelocity");
            upwardVelocityField = AccessTools.Field(typeof(Repossess), "upwardVelocity");
        }

        public static void Cleanup()
        {
        }
        [HarmonyPatch(typeof(Repossess), MethodType.Constructor)]
        public class Repossess_Constructor_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Repossess __instance)
            {
                __instance.searchRange *= PluginConfig.Instance.SearchRangeMultiplier.Value;
                if (originalForwardVelocity == null && forwardVelocityField != null && upwardVelocityField != null)
                {
                    originalForwardVelocity = (float)forwardVelocityField.GetValue(null);
                    originalUpwardVelocity = (float)upwardVelocityField.GetValue(null);
                }
                if (forwardVelocityField != null && originalForwardVelocity.HasValue)
                {
                    forwardVelocityField.SetValue(null, originalForwardVelocity.Value * PluginConfig.Instance.ForwardVelocityMultiplier.Value);
                }
                if (upwardVelocityField != null && originalUpwardVelocity.HasValue)
                {
                    upwardVelocityField.SetValue(null, originalUpwardVelocity.Value * PluginConfig.Instance.UpwardVelocityMultiplier.Value);
                }
            }
        }
        [HarmonyPatch(typeof(DrifterBagController), "CalculateBaggedObjectMass")]
        public class DrifterBagController_CalculateBaggedObjectMass_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(GameObject targetObject, ref float __result)
            {
                if (!targetObject)
                {
                    __result = 0f;
                    return false;
                 }

                  float mass = Constants.Multipliers.DefaultMassMultiplier;
                  if (targetObject.TryGetComponent<IPhysMotor>(out var physMotor))
                {
                    mass = physMotor.mass;
                }
                else if (targetObject.TryGetComponent<SpecialObjectAttributes>(out var specialObjectAttributes))
                {
                     mass = specialObjectAttributes.massOverride;
                 }

                  float multiplier = Constants.Multipliers.DefaultMassMultiplier;
                  if (float.TryParse(PluginConfig.Instance.MassMultiplier.Value, out float parsed))
                  {
                      multiplier = parsed;
                  }
                  mass *= multiplier;

                  __result = Mathf.Clamp(mass, 0f, DrifterBagController.maxMass);

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
                        if (gameObject != null && !OtherPatches.IsInProjectileState(gameObject))
                        {
                            totalMass += __instance.CalculateBaggedObjectMass(gameObject);
                        }
                     }
                 }

                  totalMass = Mathf.Clamp(totalMass, 0f, Constants.Limits.MaxMass);

                  Traverse.Create(__instance).Field("baggedMass").SetValue(totalMass);

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
            }
        }
        [HarmonyPatch(typeof(DrifterBagController), "Awake")]
        public class DrifterBagController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                __instance.maxSmacks = PluginConfig.Instance.MaxSmacks.Value;

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
                var traverse = Traverse.Create(__instance);
                var targetObject = traverse.Field("targetObject").GetValue<GameObject>();

                if (targetObject == null)
                {

                    return;
                }
                var currentBreakoutTime = traverse.Field("breakoutTime").GetValue<float>();
                traverse.Field("breakoutTime").SetValue(currentBreakoutTime * PluginConfig.Instance.BreakoutTimeMultiplier.Value);
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

                var bagController = __instance.outer.GetComponent<DrifterBagController>();
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
        public static void OnForwardVelocityChanged(object sender, EventArgs args)
        {
            if (forwardVelocityField != null && originalForwardVelocity.HasValue)
            {
                forwardVelocityField.SetValue(null, originalForwardVelocity.Value * PluginConfig.Instance.ForwardVelocityMultiplier.Value);
            }
        }
        public static void OnUpwardVelocityChanged(object sender, EventArgs args)
        {
            if (upwardVelocityField != null && originalUpwardVelocity.HasValue)
            {
                upwardVelocityField.SetValue(null, originalUpwardVelocity.Value * PluginConfig.Instance.UpwardVelocityMultiplier.Value);
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.Repossess), "OnExit")]
        public class Repossess_OnExit_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(EntityStates.Drifter.Repossess __instance)
            {

            }
        }

        public static bool HasPassengerOverride(VehicleSeat seat)
        {
            if (seat == null) return false;

            if (!PluginConfig.Instance.BottomlessBagEnabled.Value) return seat.hasPassenger;

            var bagController = seat.GetComponentInParent<DrifterBagController>();
            if (bagController)
            {
                int count = BagCapacityCalculator.GetBaggedObjectCount(bagController);

                int maxCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);

                if (count >= maxCapacity)
                {

                    return true;
                }

                if (seat.hasPassenger)
                {

                    return false;
                }
            }

            return seat.hasPassenger;
        }
    }
}
