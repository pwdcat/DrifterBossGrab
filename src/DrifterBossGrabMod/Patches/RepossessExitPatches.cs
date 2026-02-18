using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drifter;
using DrifterBossGrabMod.Networking;
namespace DrifterBossGrabMod.Patches
{
    public static class RepossessExitPatches
    {
        [HarmonyPatch(typeof(RepossessExit), "OnEnter")]
        public class RepossessExit_OnEnter_Patch
        {
            private static GameObject? originalChosenTarget;

            [HarmonyPrefix]
            public static bool Prefix(RepossessExit __instance)
            {
                var traverse = Traverse.Create(__instance);
                var chosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                originalChosenTarget = chosenTarget;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    Log.Info($" RepossessExit Prefix: originalChosenTarget = {originalChosenTarget}");
                    Log.Info($"[RepossessExit Prefix] EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}, hasAuthority={bagController?.hasAuthority}");
                }
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance)
            {
                // Only apply grabbing logic if any grabbing type is enabled
                if (!PluginConfig.Instance.EnableBossGrabbing.Value && !PluginConfig.Instance.EnableNPCGrabbing.Value)
                    return;
                var traverse = Traverse.Create(__instance);
                var chosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" RepossessExit Postfix: chosenTarget = {chosenTarget}, originalChosenTarget = {originalChosenTarget}");
                }
                // If chosenTarget was rejected but it's grabbable, allow it
                if (chosenTarget == null && originalChosenTarget != null && PluginConfig.IsGrabbable(originalChosenTarget))
                {
                    traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                    traverse.Field("activatedHitpause").SetValue(true);
                }
                else if (chosenTarget == null && originalChosenTarget != null)
                {
                    var component2 = originalChosenTarget.GetComponent<CharacterBody>();
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Checking body: {component2}, ungrabbable: {component2 && component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}");
                    }
                    if (component2)
                    {
                        bool isBoss = component2.isBoss || component2.isChampion;
                        bool isElite = component2.isElite;
                        bool isUngrabbable = component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                        bool canGrab = (PluginConfig.Instance.EnableBossGrabbing.Value && isBoss) ||
                                        (PluginConfig.Instance.EnableNPCGrabbing.Value && isUngrabbable);
                        bool isBlacklisted = PluginConfig.IsBlacklisted(component2.name);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Body {component2.name}: isBoss={isBoss}, isElite={isElite}, ungrabbable={isUngrabbable}, canGrab={canGrab}, isBlacklisted={isBlacklisted}");
                        }
                        if (canGrab && !isBlacklisted)
                        {
                            traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                            traverse.Field("activatedHitpause").SetValue(true);
                        }
                    }
                }

                // Send network message to host when balance is enabled and a grab occurs
                // This ensures the host also calls AssignPassenger, triggering the Harmony patch
                if (PluginConfig.Instance.EnableBalance.Value && originalChosenTarget != null)
                {
                    // Only send if we're a client (not the host)
                    if (!NetworkServer.active && NetworkClient.active)
                    {
                        var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                        if (bagController != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[RepossessExit Postfix] Sending grab request to host for {originalChosenTarget.name}");
                            }
                            CycleNetworkHandler.SendGrabObjectRequest(bagController, originalChosenTarget);
                        }
                    }
                }
            }
        }
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                try
                {
                    var traverse = Traverse.Create(__instance);
                    GameObject targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                    if (targetObject == null) return;
                }
                catch (Exception ex)
                {
                    Log.Error($" Error in BaggedObject.OnEnter debug logging: {ex.Message}");
                }
            }
        }
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnExit")]
        public class BaggedObject_OnExit_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                try
                {
                    var traverse = Traverse.Create(__instance);
                    GameObject targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                    if (targetObject == null) return;

                    // CHECK SUPPRESSION: If suppression is active, do NOT restore physics yet.
                    if (BaggedObjectPatches.IsObjectExitSuppressed(targetObject))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject.OnExit] Suppressing Rigidbody restoration for {targetObject.name} (AutoGrab/Transition)");
                        }
                        return;
                    }

                    // Re-enable Rigidbody for released objects
                    var rb = targetObject.GetComponent<Rigidbody>();
                    if (rb)
                    {
                        rb.isKinematic = false;
                        rb.detectCollisions = true;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Restored Rigidbody on {targetObject.name} (bag exit)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($" Error in BaggedObject.OnExit restoration: {ex.Message}");
                }
            }
        }
    }
}
