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

        public static void Cleanup()
        {
            // No cleanup needed for this patch
        }
        [HarmonyPatch(typeof(Repossess), MethodType.Constructor)]
        public class Repossess_Constructor_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Repossess __instance)
            {
                __instance.searchRange *= PluginConfig.Instance.SearchRangeMultiplier.Value;
                // Cache original values on first use
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
        [HarmonyPatch(typeof(DrifterBagController), "Awake")]
        public class DrifterBagController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                __instance.maxSmacks = PluginConfig.Instance.MaxSmacks.Value;
            }
        }
        [HarmonyPatch(typeof(DrifterBagController), "CalculateBaggedObjectMass")]
        public class DrifterBagController_CalculateBaggedObjectMass_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                float multiplier = 1.0f;
                if (float.TryParse(PluginConfig.Instance.MassMultiplier.Value, out float parsed))
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
                // If targetObject is null, log error and skip processing to prevent NRE
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
                // Synchronize persistence across clients in multiplayer
                if (UnityEngine.Networking.NetworkServer.active && PluginConfig.Instance.EnableObjectPersistence.Value)
                {
                    PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(new System.Collections.Generic.List<GameObject> { targetObject });
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Sent persistence message for bagged object {targetObject.name}");
                    }
                }
                // Spawn the object on network if server
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
                // Ensure UI overlay is refreshed for initial grabs (not done via AssignPassenger)
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
                if (!DrifterBossGrabPlugin.IsDrifterPresent) return true; // Skip if no Drifter
                // Clean null entries from SpecialObjectAttributes lists to prevent NullReferenceException
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
                if (!DrifterBossGrabPlugin.IsDrifterPresent) return; // Skip if no Drifter
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
                // Check for locked objects
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
                        // Check config settings for bosses and ungrabbable NPCs
                        if (body.isBoss && !PluginConfig.Instance.EnableBossGrabbing.Value)
                        {
                            return; // Don't allow targeting if boss grabbing is disabled
                        }
                        if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && !PluginConfig.Instance.EnableNPCGrabbing.Value)
                        {
                            return; // Don't allow targeting if NPC grabbing is disabled
                        }
                        // Allow targeting of regular enemies and enabled boss/NPC types as long as not blacklisted
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
                if (!DrifterBossGrabPlugin.IsDrifterPresent) return true; // Skip if no Drifter
                if (PluginConfig.IsBlacklisted(__instance.gameObject.name))
                {
                    return true; // Allow original behavior for blacklisted
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
                return true; // Allow if no body
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
    }
}
