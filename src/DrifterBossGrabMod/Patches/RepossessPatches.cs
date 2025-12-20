using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using EntityStates.Drifter;

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

        [HarmonyPatch(typeof(Repossess), MethodType.Constructor)]
        public class Repossess_Constructor_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Repossess __instance)
            {
                __instance.searchRange *= PluginConfig.SearchRangeMultiplier.Value;

                // Cache original values on first use
                if (originalForwardVelocity == null)
                {
                    originalForwardVelocity = (float)forwardVelocityField.GetValue(null);
                    originalUpwardVelocity = (float)upwardVelocityField.GetValue(null);
                }

                forwardVelocityField.SetValue(null, originalForwardVelocity.Value * PluginConfig.ForwardVelocityMultiplier.Value);
                upwardVelocityField.SetValue(null, originalUpwardVelocity.Value * PluginConfig.UpwardVelocityMultiplier.Value);
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "Awake")]
        public class DrifterBagController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                __instance.maxSmacks = PluginConfig.MaxSmacks.Value;
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "CalculateBaggedObjectMass")]
        public class DrifterBagController_CalculateBaggedObjectMass_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                float multiplier = 1.0f;
                if (float.TryParse(PluginConfig.MassMultiplier.Value, out float parsed))
                {
                    multiplier = parsed;
                }
                __result *= multiplier;
                __result = Mathf.Clamp(__result, 0f, DrifterBagController.maxMass);
            }
        }

        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_ExtendBreakoutTime
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                // Cache traverse object to avoid repeated creation
                var traverse = Traverse.Create(__instance);
                var targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} BaggedObject.OnEnter: targetObject = {targetObject}");
                    if (targetObject)
                    {
                        var body = targetObject.GetComponent<CharacterBody>();
                        if (body)
                        {
                            Log.Info($"{Constants.LogPrefix} Bagging {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, currentVehicle: {body.currentVehicle != null}");
                        }
                    }
                }
                var currentBreakoutTime = traverse.Field("breakoutTime").GetValue<float>();
                traverse.Field("breakoutTime").SetValue(currentBreakoutTime * PluginConfig.BreakoutTimeMultiplier.Value);
            }
        }

        [HarmonyPatch(typeof(SpecialObjectAttributes), "get_isTargetable")]
        public class SpecialObjectAttributes_get_isTargetable
        {
            [HarmonyPrefix]
            public static void Prefix(SpecialObjectAttributes __instance)
            {
                // Clean null entries from SpecialObjectAttributes lists to prevent NullReferenceException
                __instance.childSpecialObjectAttributes.RemoveAll(s => s == null);
                __instance.renderersToDisable.RemoveAll(r => r == null);
                __instance.behavioursToDisable.RemoveAll(b => b == null);
                __instance.childObjectsToDisable.RemoveAll(c => c == null);
                __instance.pickupDisplaysToDisable.RemoveAll(p => p == null);
                __instance.lightsToDisable.RemoveAll(l => l == null);
                __instance.objectsToDetach.RemoveAll(o => o == null);
                __instance.skillHighlightRenderers.RemoveAll(r => r == null);
            }

            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance, ref bool __result)
            {
                var body = __instance.gameObject.GetComponent<CharacterBody>();
                if (body)
                {
                    bool isBoss = body.isBoss;
                    bool isUngrabbable = body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                    bool canOverride = ((isBoss && PluginConfig.EnableBossGrabbing.Value) ||
                                        (isUngrabbable && PluginConfig.EnableNPCGrabbing.Value)) &&
                                       !PluginConfig.IsBlacklisted(__instance.gameObject.name);
                    if (canOverride)
                    {
                        __result = true;
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
                        // Allow targeting of any body (bosses, NPCs, regular enemies) as long as not blacklisted
                        allowTargeting = true;
                    }
                    if (allowTargeting && !PluginConfig.IsBlacklisted(body.name))
                    {
                        __result = true;
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Allowing targeting of boss/elite/NPC: {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, ungrabbable: {body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}, currentVehicle: {body.currentVehicle != null}");
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
                if (PluginConfig.IsBlacklisted(__instance.gameObject.name))
                {
                    return true; // Allow original behavior for blacklisted
                }
                var body = __instance.gameObject.GetComponent<CharacterBody>();
                if (body)
                {
                    bool isBoss = body.isBoss;
                    bool isUngrabbable = body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                    bool shouldPrevent = (isBoss && PluginConfig.EnableBossGrabbing.Value) ||
                                          (isUngrabbable && PluginConfig.EnableNPCGrabbing.Value);
                    return !shouldPrevent;
                }
                return true; // Allow if no body
            }
        }

        public static void OnForwardVelocityChanged(object sender, EventArgs args)
        {
            if (originalForwardVelocity.HasValue)
            {
                forwardVelocityField.SetValue(null, originalForwardVelocity.Value * PluginConfig.ForwardVelocityMultiplier.Value);
            }
        }

        public static void OnUpwardVelocityChanged(object sender, EventArgs args)
        {
            if (originalUpwardVelocity.HasValue)
            {
                upwardVelocityField.SetValue(null, originalUpwardVelocity.Value * PluginConfig.UpwardVelocityMultiplier.Value);
            }
        }
    }
}