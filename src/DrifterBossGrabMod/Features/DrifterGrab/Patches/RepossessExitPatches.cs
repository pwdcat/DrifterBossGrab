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
    public static class RepossessExitPatches
    {
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RepossessExit Prefix] Recovered chosenTarget from deserialization: {recovered.name}");
                        }
                    }
                    else
                    {
                        Log.Warning($"[RepossessExit Prefix] chosenTarget is null from {__instance.GetType().Name}");
                        originalChosenTarget = null;
                        return true;
                    }
                }
                originalChosenTarget = chosenTarget;

                // Store per-instance for OnSerialize to use
                StoreOriginalTarget(__instance, chosenTarget);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    Log.Info($" RepossessExit Prefix: originalChosenTarget = {originalChosenTarget}.");
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
                var chosenTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
                if (chosenTarget == null && originalChosenTarget == null)
                {
                    Log.Warning($"[RepossessExit Postfix] chosenTarget is null from {__instance.GetType().Name}");
                    return;
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" RepossessExit Postfix: chosenTarget = {chosenTarget}, originalChosenTarget = {originalChosenTarget}.");
                }

                // If chosenTarget was rejected but it's grabbable, allow it
                if (chosenTarget == null && originalChosenTarget != null && PluginConfig.IsGrabbable(originalChosenTarget))
                {
                    _chosenTargetField?.SetValue(__instance, originalChosenTarget);
                    _activatedHitpauseField?.SetValue(__instance, true);
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
                            _chosenTargetField?.SetValue(__instance, originalChosenTarget);
                            _activatedHitpauseField?.SetValue(__instance, true);
                            chosenTarget = originalChosenTarget;
                        }
                    }
                }

                // Send network message to host when a grab occurs
                if (originalChosenTarget != null)
                {
                    // Only send if we're a client (not the host)
                    if (!NetworkServer.active && NetworkClient.active)
                    {
                        // Block grab if object is currently undergoing throw operation
                        if (ProjectileRecoveryPatches.IsUndergoingThrowOperation(originalChosenTarget))
                        {
                            Log.Warning($"[RepossessExit Postfix] Blocking grab request for {originalChosenTarget.name} - object is currently undergoing throw operation");
                            return;
                        }

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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RepossessExit OnSerialize] Restored chosenTarget for serialization: {stored.name}");
                        }
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[RepossessExit OnDeserialize] Received chosenTarget: {deserializedTarget.name}");
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

                var chosenTarget = _chosenTargetField?.GetValue(__instance) as GameObject;
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

                // Only refresh stock if it's 0 and the bag still has room for another grab
                if (utilitySkill.stock == 0 && BagCapacityCalculator.HasRoomForGrab(bagController))
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
                    var targetObject = _targetObjectField?.GetValue(__instance) as GameObject;
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
                    var targetObject = _targetObjectField?.GetValue(__instance) as GameObject;
                    if (targetObject == null) return;

                    // check suppression: if suppression is active, do not restore physics yet.
                    if (BaggedObjectPatches.IsObjectExitSuppressed(targetObject))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject.OnExit] Suppressing Rigidbody restoration for {targetObject.name} (AutoGrab/Transition)");
                        }
                        return;
                    }
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        var state = Patches.BagPatches.GetState(bagController);
                        bool isInMainSeat = bagController.vehicleSeat?.NetworkpassengerBodyObject == targetObject;
                        bool isInAdditionalSeat = state?.AdditionalSeats.ContainsKey(targetObject) == true;

                        if (isInMainSeat || isInAdditionalSeat)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [BaggedObject.OnExit] Skipping restoration for {targetObject.name} - still in bag (Main={isInMainSeat}, Additional={isInAdditionalSeat})");
                            }
                            return;
                        }
                    }

                    // Re-enable Rigidbody for released objects
                    var rb = targetObject.GetComponent<Rigidbody>();
                    if (rb)
                    {
                        var existingState = bagController != null ? BaggedObjectPatches.LoadObjectState(bagController, targetObject) : null;
                        if (existingState != null && existingState.hasCapturedRigidbodyState)
                        {
                            rb.isKinematic = existingState.originalIsKinematic;
                            rb.useGravity = existingState.originalUseGravity;
                            rb.mass = existingState.originalMass;
                            rb.drag = existingState.originalDrag;
                            rb.angularDrag = existingState.originalAngularDrag;
                            rb.detectCollisions = true;
                        }
                        else
                        {
                            rb.isKinematic = false;
                            rb.detectCollisions = true;
                        }

                    }

                    // Re-enable hurtboxes that were disabled during the grab.
                    var characterBody = targetObject.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.modelLocator != null)
                    {
                        var modelTransform = characterBody.modelLocator.modelTransform;
                        if (modelTransform != null)
                        {
                            var hurtBoxGroup = modelTransform.GetComponent<RoR2.HurtBoxGroup>();
                            if (hurtBoxGroup != null && hurtBoxGroup.hurtBoxesDeactivatorCounter > 0)
                            {
                                int oldCounter = hurtBoxGroup.hurtBoxesDeactivatorCounter;
                                hurtBoxGroup.hurtBoxesDeactivatorCounter = 0;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($"[BaggedObject.OnExit] Reset hurtBoxesDeactivatorCounter from {oldCounter} to 0 for {targetObject.name}");
                                }
                            }
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
