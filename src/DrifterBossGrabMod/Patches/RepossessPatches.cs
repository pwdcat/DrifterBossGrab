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

                 float mass =1f;
                 if (targetObject.TryGetComponent<IPhysMotor>(out var physMotor))
                {
                    mass = physMotor.mass;
                }
                else if (targetObject.TryGetComponent<SpecialObjectAttributes>(out var specialObjectAttributes))
                {
                     mass = specialObjectAttributes.massOverride;
                 }

                 float multiplier = 1.0f;
                if (float.TryParse(PluginConfig.Instance.MassMultiplier.Value, out float parsed))
                {
                    multiplier = parsed;
                }
                float rawMass = mass;
                mass *= multiplier;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                     Log.Info($" [CalculateBaggedObjectMass] Object: {targetObject.name}, RawMass: {rawMass}, Multiplier: {multiplier}, FinalMass: {mass}, Uncapped: {PluginConfig.Instance.UncapBagScale.Value}");
                 }

                 if (!PluginConfig.Instance.UncapBagScale.Value)
                {
                    __result = Mathf.Clamp(mass, 0f, DrifterBagController.maxMass);
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
                if (BagPatches.baggedObjectsDict.TryGetValue(__instance, out var list))
                {
                    foreach (var obj in list)
                    {
                        if (obj != null && !OtherPatches.IsInProjectileState(obj))
                        {
                            totalMass += __instance.CalculateBaggedObjectMass(obj);
                        }
                     }
                 }

                 if (!PluginConfig.Instance.UncapBagScale.Value)
                {
                    totalMass = Mathf.Clamp(totalMass, 0f, 700f);
                }
                else
                {
                    totalMass = Mathf.Max(totalMass, 0f);
                }
                
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                     Log.Info($" [RecalculateBaggedObjectMass] Setting total baggedMass to {totalMass} (Objects: {(list?.Count ?? 0)})");
                 }

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
                BagPatches.ForceRecalculateMass(__instance);
            }
        }
        [HarmonyPatch(typeof(DrifterBagController), "Awake")]
        public class DrifterBagController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                __instance.maxSmacks = PluginConfig.Instance.MaxSmacks.Value;
                BagPatches.ScanAllSceneComponents();

                if (__instance.GetComponent<Networking.BottomlessBagNetworkController>() == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[DrifterBagController_Awake] Adding BottomlessBagNetworkController to {__instance.name}");
                    }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" BaggedObject.OnEnter: targetObject = {targetObject}");
                    if (targetObject)
                    {
                        var body = targetObject.GetComponent<CharacterBody>();
                        if (body)
                        {
                            Log.Info($" Bagging {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, currentVehicle: {body.currentVehicle != null}");
                        }
                    }
                }
                if (targetObject == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" BaggedObject.OnEnter: targetObject is null, skipping breakout time modification");
                    }
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Spawned bagged object {targetObject.name} on network");
                        }
                    }
                }
                bool alreadyPersisted = PersistenceObjectManager.IsObjectPersisted(targetObject);
                if (UnityEngine.Networking.NetworkServer.active && PluginConfig.Instance.EnableObjectPersistence.Value && !alreadyPersisted)
                {
                    PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(new System.Collections.Generic.List<GameObject> { targetObject });
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Sent persistence message for bagged object {targetObject.name}");
                    }
                }
                else if (UnityEngine.Networking.NetworkServer.active && PluginConfig.Instance.EnableObjectPersistence.Value && alreadyPersisted && PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Skipped duplicate persistence message for {targetObject.name} (already persisted via auto-grab)");
                }
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController != null && targetObject != null)
                {
                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter] Called RefreshUIOverlayForMainSeat for initial grab of {targetObject.name}");
                    }
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Allowing targeting of locked object: {__instance.gameObject.name}");
                    }
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Allowing targeting of boss/elite/NPC: {body!.name}, isBoss: {body!.isBoss}, isElite: {body!.isElite}, ungrabbable: {body!.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}, currentVehicle: {body!.currentVehicle != null}");
                        }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [Repossess.OnExit] Exiting Repossess state");
                }
            }
        }
        
        public static bool HasPassengerOverride(VehicleSeat seat)
        {
            if (seat == null) return false;

            if (!PluginConfig.Instance.BottomlessBagEnabled.Value) return seat.hasPassenger;

            var bagController = seat.GetComponentInParent<DrifterBagController>();
            if (bagController)
            {
                int count = BagPatches.GetBaggedObjectCount(bagController);

                int maxCapacity = BagPatches.GetUtilityMaxStock(bagController);

                if (count >= maxCapacity)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($" [HasPassengerOverride] Bag is FULL ({count}/{maxCapacity}). Blocking grab.");
                    return true;
                }

                if (seat.hasPassenger)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($" [HasPassengerOverride] Bag has {count}/{maxCapacity} objects. Tricking Repossess state to allow grab.");
                    return false;
                }
            }

            return seat.hasPassenger;
        }
    }
}
