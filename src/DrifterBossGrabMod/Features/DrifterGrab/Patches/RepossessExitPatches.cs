#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates;
using EntityStates.Drifter;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Networking;
using DrifterBossGrabMod.Core;
namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // REPOSSESS EXIT PATCHES
    // ========================================================================================

    public static class RepossessExitPatches
    {
        // ========================================================================================
        // INSTANCE TRACKING
        // ========================================================================================

        private static readonly FieldInfo _chosenTargetField = ReflectionCache.RepossessExit.ChosenTarget;
        private static readonly FieldInfo _activatedHitpauseField = ReflectionCache.RepossessExit.ActivatedHitpause;
        private static readonly FieldInfo _targetObjectField = ReflectionCache.BaggedObject.TargetObject;

        // Per-instance storage using ConditionalWeakTable
        private static readonly ConditionalWeakTable<RepossessExit, System.Runtime.CompilerServices.StrongBox<GameObject?>> _originalTargets
            = new ConditionalWeakTable<RepossessExit, System.Runtime.CompilerServices.StrongBox<GameObject?>>();

        public static void StoreOriginalTarget(RepossessExit instance, GameObject? target)
        {
            if (_originalTargets.TryGetValue(instance, out var box))
                box.Value = target;
            else
                _originalTargets.Add(instance, new System.Runtime.CompilerServices.StrongBox<GameObject?>(target));
        }

        public static GameObject? GetOriginalTarget(RepossessExit instance)
        {
            if (_originalTargets.TryGetValue(instance, out var box))
                return box.Value;
            return null;
        }

        // ========================================================================================
        // GRAB LOGIC
        // ========================================================================================

        [HarmonyPatch(typeof(RepossessExit), "OnEnter")]
        public class RepossessExit_OnEnter_Patch
        {
            private static GameObject? originalChosenTarget;

            [HarmonyPrefix]
            public static bool Prefix(RepossessExit __instance)
            {
                var chosenTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
                if (chosenTarget == null)
                {
                    // On client, try to recover from deserialized original target
                    var recovered = GetOriginalTarget(__instance);
                    if (recovered != null)
                    {
                        chosenTarget = recovered;
                        _chosenTargetField?.SetValue(__instance, chosenTarget);
                        Log.DebugIfEnabled("[RepossessExit Prefix] Recovered chosenTarget from deserialization: {0}", recovered.name);
                    }
                    else
                    {
                        Log.DebugIfEnabled($"[RepossessExit Prefix] chosenTarget is null from {__instance.GetType().Name}");
                        originalChosenTarget = null;
                        return true;
                    }
                }
                originalChosenTarget = chosenTarget;

                // Store per-instance for OnSerialize to use
                StoreOriginalTarget(__instance, chosenTarget);

                var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                Log.DebugIfEnabled("[RepossessExit Prefix] originalChosenTarget = {0}, EnableBalance={1}, NetworkServer.active={2}, hasAuthority={3}",
                    originalChosenTarget, PluginConfig.Instance.EnableBalance.Value, NetworkServer.active, bagController?.hasAuthority);
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance)
            {
                // Only apply grabbing logic if any grabbing type is enabled
                if (!PluginConfig.Instance.EnableBossGrabbing.Value && !PluginConfig.Instance.EnableNPCGrabbing.Value)
                    return;
                var chosenTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
                if (chosenTarget == null && originalChosenTarget == null)
                {
                    Log.DebugIfEnabled($"[RepossessExit Postfix] chosenTarget is null from {__instance.GetType().Name}");
                    return;
                }
                Log.DebugIfEnabled("[RepossessExit Postfix] chosenTarget = {0}, originalChosenTarget = {1}.", chosenTarget, originalChosenTarget);

                // If chosenTarget was rejected but it's grabbable, allow it (subject to capacity)
                if (chosenTarget == null && originalChosenTarget != null && PluginConfig.IsGrabbable(originalChosenTarget))
                {
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null && !ProjectileRecoveryPatches.IsInProjectileState(originalChosenTarget) && BagCapacityCalculator.HasRoomForGrab(bagController, originalChosenTarget))
                    {
                        _chosenTargetField?.SetValue(__instance, originalChosenTarget);
                        _activatedHitpauseField?.SetValue(__instance, true);
                        chosenTarget = originalChosenTarget;
                    }
                }
                // If vanilla ALREADY chose a target, we still need to enforce our mod's capacity limits
                else if (chosenTarget != null)
                {
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null && !BagCapacityCalculator.HasRoomForGrab(bagController, chosenTarget))
                    {
                        Log.DebugIfEnabled("[RepossessExit Postfix] Capacity reached");
                        _chosenTargetField?.SetValue(__instance, null);
                        chosenTarget = null;
                    }
                }
                else if (chosenTarget == null && originalChosenTarget != null)
                {
                    var component2 = originalChosenTarget.GetComponent<CharacterBody>();
                    Log.DebugIfEnabled("[RepossessExit Postfix] Checking body: {0}, ungrabbable: {1}",
                        component2, (component2 && component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)));

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
                        Log.DebugIfEnabled("[RepossessExit Postfix] Body {0}: isBoss={1}, isElite={2}, ungrabbable={3}, isStandardRejected={4}, canGrab={5}, isBlacklisted={6}",
                            component2.name, isBoss, isElite, isUngrabbable, isStandardNPCRejectedByVanilla, canGrab, isBlacklisted);

                        if (canGrab && !isBlacklisted)
                        {
                            var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                            if (bagController != null && !ProjectileRecoveryPatches.IsInProjectileState(originalChosenTarget) && BagCapacityCalculator.HasRoomForGrab(bagController, originalChosenTarget))
                            {
                                _chosenTargetField?.SetValue(__instance, originalChosenTarget);
                                _activatedHitpauseField?.SetValue(__instance, true);
                                chosenTarget = originalChosenTarget;
                            }
                        }
                    }
                }

                if (chosenTarget == null && originalChosenTarget != null)
                {
                    StoreOriginalTarget(__instance, null);
                }

                // Send network message to host when a grab occurs
                if (chosenTarget != null)
                {
                    // Only send if we're a client (not the host)
                    if (!NetworkServer.active && NetworkClient.active)
                    {

                        var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                        if (bagController != null && originalChosenTarget != null)
                        {
                            Log.DebugIfEnabled("[RepossessExit Postfix] Sending grab request to host for {0}", originalChosenTarget.name);
                            CycleNetworkHandler.SendGrabObjectRequest(bagController, originalChosenTarget);
                        }
                    }
                }
            }
        }

        // ========================================================================================
        // NETWORKING
        // ========================================================================================

        [HarmonyPatch(typeof(RepossessExit), "OnSerialize")]
        public class RepossessExit_OnSerialize_Patch
        {
            private static GameObject? _savedTarget;

            [HarmonyPrefix]
            public static void Prefix(RepossessExit __instance)
            {
                _savedTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
                if (_savedTarget == null)
                {
                    var stored = GetOriginalTarget(__instance);
                    if (stored != null)
                    {
                        _chosenTargetField?.SetValue(__instance, stored);
                        Log.DebugIfEnabled("[RepossessExit OnSerialize] Restored chosenTarget for serialization: {0}", stored.name);
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance)
            {
                // Restore original value after serialization
                if (_savedTarget == null)
                {
                    _chosenTargetField?.SetValue(__instance, null);
                }
                _savedTarget = null;
            }
        }

        [HarmonyPatch(typeof(RepossessExit), "OnDeserialize")]
        public class RepossessExit_OnDeserialize_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance, NetworkReader reader)
            {
                var deserializedTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
                if (deserializedTarget != null)
                {
                    StoreOriginalTarget(__instance, deserializedTarget);
                    Log.DebugIfEnabled("[RepossessExit OnDeserialize] Received chosenTarget: {0}", deserializedTarget.name);
                }
            }
        }
        // ========================================================================================
        // STOCK REFRESH
        // ========================================================================================

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

                var chosenTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
                if (chosenTarget == null)
                {
                    Log.DebugIfEnabled("[SuccessiveGrab] Skipping stock refresh - chosenTarget is null (grab unsuccessful)");
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

                // Only refresh stock if it's 0 and the bag still has room for another grab
                // Only refresh stock if it's 0 and the bag still has room for another grab
                if (utilitySkill.stock == 0 && BagCapacityCalculator.HasRoomForGrab(bagController, null))
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
                            // Get override fields
                            var overriddenUtility = ReflectionCache.BaggedObject.OverriddenUtility.GetValue(baggedObject) as GenericSkill;
                            var utilityOverride = ReflectionCache.BaggedObject.UtilityOverride.GetValue(baggedObject) as RoR2.Skills.SkillDef;

                            // Temporarily remove the override
                            if (overriddenUtility != null && utilityOverride != null)
                            {
                                overriddenUtility.UnsetSkillOverride(baggedObject, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                                ReflectionCache.BaggedObject.OverriddenUtility.SetValue(baggedObject, null);

                                // Refresh the stock
                                utilitySkill.stock = 1;

                                // Reapply the override
                                baggedObject.TryOverrideUtility(utilitySkill);

                                Log.DebugIfEnabled("[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (with PrioritizeMainSeat - override temporarily removed)");
                            }
                            else
                            {
                                // No override found, just refresh the stock
                                utilitySkill.stock = 1;
                                Log.DebugIfEnabled("[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (PrioritizeMainSeat enabled but no override found)");
                            }
                        }
                        else
                        {
                            // No BaggedObject state found, just refresh the stock
                            utilitySkill.stock = 1;
                            Log.DebugIfEnabled("[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (PrioritizeMainSeat enabled but no BaggedObject state found)");
                        }
                    }
                    else
                    {
                        // PrioritizeMainSeat is disabled, just refresh the stock normally
                        utilitySkill.stock = 1;
                        Log.DebugIfEnabled("[SuccessiveGrab] Refreshed stock from 0 to 1 after successful grab (PrioritizeMainSeat disabled)");
                    }
                }
                else
                {
                    Log.DebugIfEnabled("[SuccessiveGrab] Skipping stock refresh - stock is {0} (not 0)", utilitySkill.stock);
                }
            }
        }

    }
}

