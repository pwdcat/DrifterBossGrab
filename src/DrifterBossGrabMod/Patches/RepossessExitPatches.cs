using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drifter;
using EntityStates.Drifter.Bag;
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
                if (chosenTarget == null)
                {
                    Log.Warning($"[RepossessExit Prefix] chosenTarget is null from {__instance.GetType().Name}");
                    originalChosenTarget = null;
                    return true;
                }
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
                if (chosenTarget == null && originalChosenTarget == null)
                {
                    Log.Warning($"[RepossessExit Postfix] chosenTarget is null from {__instance.GetType().Name}");
                    return;
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" RepossessExit Postfix: chosenTarget = {chosenTarget}, originalChosenTarget = {originalChosenTarget}");
                }
                
                // If chosenTarget was rejected but it's grabbable, allow it
                if (chosenTarget == null && originalChosenTarget != null && PluginConfig.IsGrabbable(originalChosenTarget))
                {
                    traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                    traverse.Field("activatedHitpause").SetValue(true);
                    chosenTarget = originalChosenTarget;
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
                        
                        // Vanilla rejects targets missing a Rigidbody or ModelLocator.
                        // If it's a standard NPC that vanilla rejected, allow it if NPC grabbing is enabled.
                        bool isStandardNPCRejectedByVanilla = !isBoss && !isUngrabbable && PluginConfig.Instance.EnableNPCGrabbing.Value;

                        bool canGrab = (PluginConfig.Instance.EnableBossGrabbing.Value && isBoss) ||
                                        (PluginConfig.Instance.EnableNPCGrabbing.Value && isUngrabbable) ||
                                        isStandardNPCRejectedByVanilla ||
                                        PluginConfig.Instance.EnableLockedObjectGrabbing.Value;

                        bool isBlacklisted = PluginConfig.IsBlacklisted(component2.name);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Body {component2.name}: isBoss={isBoss}, isElite={isElite}, ungrabbable={isUngrabbable}, isStandardRejected={isStandardNPCRejectedByVanilla}, canGrab={canGrab}, isBlacklisted={isBlacklisted}");
                        }
                        if (canGrab && !isBlacklisted)
                        {
                            traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                            traverse.Field("activatedHitpause").SetValue(true);
                            chosenTarget = originalChosenTarget;
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
        [HarmonyPatch(typeof(RepossessExit), "OnExit")]
        public class RepossessExit_OnExit_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance)
            {
                if (!PluginConfig.Instance.EnableSuccessiveGrabStockRefresh.Value)
                {
                    return;
                }

                var traverse = Traverse.Create(__instance);
                var chosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (chosenTarget == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[SuccessiveGrab] Skipping stock refresh - chosenTarget is null (grab unsuccessful)");
                    return;
                }

                // Get bag controller
                var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                if (bagController == null) return;

                // Get character body and skill locator
                var body = bagController.GetComponent<CharacterBody>();
                if (body == null || body.skillLocator == null) return;

                var utilitySkill = body.skillLocator.utility;
                if (utilitySkill == null) return;

                // Only refresh stock if it's 0
                if (utilitySkill.stock == 0)
                {
                    // When PrioritizeMainSeat is enabled, the skill is overridden with the bagged object's skill
                    // We need to temporarily remove the override, refresh the stock, and reapply it
                    if (PluginConfig.Instance.PrioritizeMainSeat.Value)
                    {
                        // Find the BaggedObject state machine
                        var stateMachines = bagController.GetComponents<EntityStateMachine>();
                        BaggedObject? baggedObject = null;
                        foreach (var esm in stateMachines)
                        {
                            if (esm.customName == "Bag" && esm.state is BaggedObject bo)
                            {
                                baggedObject = bo;
                                break;
                            }
                        }

                        if (baggedObject != null)
                        {
                            // Get the override fields
                            var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                            var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");

                            var overriddenUtility = overriddenUtilityField?.GetValue(baggedObject) as GenericSkill;
                            var utilityOverride = utilityOverrideField?.GetValue(baggedObject) as RoR2.Skills.SkillDef;

                            // Temporarily remove the override
                            if (overriddenUtility != null && utilityOverride != null && overriddenUtilityField != null)
                            {
                                overriddenUtility.UnsetSkillOverride(baggedObject, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                                overriddenUtilityField.SetValue(baggedObject, null);

                                // Refresh the stock
                                utilitySkill.stock = 1;

                                // Reapply the override
                                baggedObject.TryOverrideUtility(utilitySkill);

                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Debug($"[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (with PrioritizeMainSeat - override temporarily removed)");
                            }
                            else
                            {
                                // No override found, just refresh the stock
                                utilitySkill.stock = 1;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Debug($"[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (PrioritizeMainSeat enabled but no override found)");
                            }
                        }
                        else
                        {
                            // No BaggedObject state found, just refresh the stock
                            utilitySkill.stock = 1;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Debug($"[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (PrioritizeMainSeat enabled but no BaggedObject state found)");
                        }
                    }
                    else
                    {
                        // PrioritizeMainSeat is disabled, just refresh the stock normally
                        utilitySkill.stock = 1;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (PrioritizeMainSeat disabled)");
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[SuccessiveGrab] Skipping stock refresh - stock is {utilitySkill.stock} (not 0)");
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
