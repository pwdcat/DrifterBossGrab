using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using RoR2.HudOverlay;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates;
using EntityStates.Drifter.Bag;
using EntityStateMachine = RoR2.EntityStateMachine;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Features;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod.Patches
{
    // Harmony patches for managing bagged object state lifecycle.
    // Handles OnEnter, OnExit, FixedUpdate, and state machine transitions.
    public static class BaggedObjectStatePatches
    {
        private static string GetSafeName(UnityEngine.Object? obj) => obj ? obj.name : "null";
        // Reflection Cache
        private static readonly FieldInfo _targetObjectField = AccessTools.Field(typeof(BaggedObject), "targetObject");
        private static readonly FieldInfo _targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
        private static readonly FieldInfo _isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
        private static readonly MethodInfo _holdsDeadBodyMethod = AccessTools.Method(typeof(BaggedObject), "HoldsDeadBody");
        private static readonly FieldInfo _vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
        private static readonly FieldInfo _baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
        private static readonly FieldInfo _uiOverlayControllerField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
        private static readonly FieldInfo _drifterBagControllerField = AccessTools.Field(typeof(BaggedObject), "drifterBagController");
        private static readonly FieldInfo _overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
        private static readonly FieldInfo _overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
        private static readonly FieldInfo _utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
        private static readonly FieldInfo _primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");

        // Track last processed object to prevent infinite re-entry during sync issues
        private static GameObject? _lastProcessedObject;
        private static float _lastProcessTime;

        // Harmony patch for BaggedObject.OnEnter.
        // Handles initialization and state management when entering the bagged state.
        [HarmonyPatch(typeof(BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter
        {
            // Flag to signal that we are currently initializing a BaggedObject on the client
            // This is used by VehicleSeat.AssignPassenger patch to block assignment
            public static GameObject? InitializingPassenger;

            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                // Guard against infinite re-entry during sync issues
                if (__instance == null) return false;
                if (__instance.targetObject != null)
                {
                    var currentTime = Time.time;
                    if (ReferenceEquals(__instance.targetObject, _lastProcessedObject) &&
                        (currentTime - _lastProcessTime) < 0.5f)
                    {
                        // Same object processed very recently - likely a re-entry loop
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[BaggedObject_OnEnter.Prefix] Blocking re-entry for {__instance.targetObject.name} (processed {(currentTime - _lastProcessTime):F3}s ago)");
                        return false;
                    }
                }

                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
                if (bagController == null)
                {

                    return true;
                }
                var targetObject = __instance?.targetObject;
                if (targetObject == null)
                {

                    return false; // Skip the original OnEnter to prevent NRE
                }
                // Check if targetObject is in additional seat
                var seatDict = BagPatches.GetState(bagController).AdditionalSeats;
                if (seatDict != null && seatDict.TryGetValue(targetObject, out var additionalSeat))
                {

                    // Assign to additional seat instead of main
                    additionalSeat.AssignPassenger(targetObject);
                    // Don't call the original OnEnter logic
                    return false;
                }

                // Client with capacity > 1: allow OnEnter to run for initialization, but block seat assignment
                // We use InitializingPassenger flag to tell VehicleSeat.AssignPassenger to skip
                if (!NetworkServer.active && bagController.hasAuthority)
                {
                    int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                    if (effectiveCapacity > 1)
                    {
                        var list = BagPatches.GetState(bagController).BaggedObjects;
                        bool isAlreadyTracked = list.Contains(targetObject);

                        if (isAlreadyTracked)
                        {
                            if (!isAlreadyTracked)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for NEW GRAB of {targetObject.name} (capacity={effectiveCapacity}) but FLAGGING to block seat assignment");

                                InitializingPassenger = targetObject;

                                list.Add(targetObject);
                                BagHelpers.AddTracker(bagController, targetObject);
                                BagCarouselUpdater.UpdateCarousel(bagController);
                                BagCarouselUpdater.UpdateNetworkBagState(bagController);
                                BagPassengerManager.ForceRecalculateMass(bagController);
                            }
                            else
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for CYCLING of {targetObject.name} (capacity={effectiveCapacity})");
                            }
                        }
                        else
                        {
                             // Not tracked
                        }

                        return true;
                    }
                }

                // Otherwise, proceed normally
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                // Clear the flag
                InitializingPassenger = null;

                // Suppress vanilla's walk speed modifier
                BagPassengerManager.SuppressVanillaWalkSpeedModifier(__instance);

                // Update tracking to prevent infinite re-entry
                if (__instance?.targetObject != null)
                {
                    _lastProcessedObject = __instance.targetObject;
                    _lastProcessTime = Time.time;
                }
                else
                {
                    // Clear tracking when transitioning to null state
                    _lastProcessedObject = null;
                    _lastProcessTime = Time.time;
                }

                // Check if the main seat has the targetObject as passenger
                // If not, remove the UI overlay to prevent incorrect display
                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();

                if (bagController == null) return;
                var targetObject = __instance?.targetObject;

                if (targetObject == null) return;

                // Check if object is in an additional seat - this is used in multiple places
                bool isInAdditionalSeat = BagHelpers.GetAdditionalSeat(bagController, targetObject) != null;

                // Only populate if the network controller hasn't synced a null state (selectedIndex=-1)
                if (bagController.hasAuthority && !NetworkServer.active)
                {
                    // Don't populate main seat on client for new grabs when capacity > 1
                    // But do allow it during cycling
                    int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                    bool isAlreadyTracked = BagPatches.GetState(bagController).BaggedObjects.Contains(targetObject);
                    if (effectiveCapacity > 1 && !isAlreadyTracked)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[BaggedObject_OnEnter.Postfix] Client skipping main seat population for NEW GRAB of {targetObject.name} (capacity={effectiveCapacity})");
                        // Skip main seat population - server will handle seat assignment via DoSync
                    }
                    else
                    {
                    // Check if network controller has synced state
                    var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                    bool shouldPopulateMainSeat = true;

                    if (netController != null && netController.selectedIndex < 0 && netController.GetBaggedObjects().Count > 0)
                    {
                        shouldPopulateMainSeat = false;

                    }

                    if (shouldPopulateMainSeat && BagPatches.GetMainSeatObject(bagController) == null && !isInAdditionalSeat)
                    {

                        BagPatches.SetMainSeatObject(bagController, targetObject);
                    }
                }

            // Also ensure it's in BaggedObjects list (always do this, regardless of main seat state)
            var list = BagPatches.GetState(bagController).BaggedObjects;
            if (list != null && !list.Contains(targetObject))
            {
                list.Add(targetObject);
            }
                }

                var outerMainSeat = bagController.vehicleSeat;

                bool seatHasTarget = outerMainSeat != null && outerMainSeat.hasPassenger && ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject);
                var tracked = BagPatches.GetMainSeatObject(bagController);
                bool trackedHasTarget = tracked != null && ReferenceEquals(tracked, targetObject);

                if (bagController.hasAuthority)
                {
                    // Do nothing
                }
                else if (!seatHasTarget && !trackedHasTarget)
                {
                    // Neither seat nor tracked has targetObject, remove the UI
                    if (!isInAdditionalSeat)
                    {
                        var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                        var uiOverlayController = (OverlayController)uiOverlayField.GetValue(__instance);
                        if (uiOverlayController != null)
                        {
                            HudOverlayManager.RemoveOverlay(uiOverlayController);
                            uiOverlayField.SetValue(__instance, null);
                        }
                    }
                }
                // Add to BaggedObjects list for skill grabs
                if (bagController != null && targetObject != null)
                {
                    var list = BagPatches.GetState(bagController).BaggedObjects;
                    if (!list.Contains(targetObject))
                    {
                        list.Add(targetObject);

                    }
                    BagCarouselUpdater.UpdateCarousel(bagController);
                    // Sync to network so server knows about client grabs
                    BagCarouselUpdater.UpdateNetworkBagState(bagController);
                }
                else
                {
                    // Ensure UI is created/refreshed for main seat objects
                    if (bagController != null && targetObject != null && BagHelpers.GetAdditionalSeat(bagController, targetObject) == null)
                    {
                        BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    }
                }
                // Remove the overlay to use carousel instead
                if (PluginConfig.Instance.EnableCarouselHUD.Value)
                {
                    var uiOverlayField2 = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                    var uiOverlayController2 = (OverlayController)uiOverlayField2.GetValue(__instance);
                    if (uiOverlayController2 != null)
                    {
                        HudOverlayManager.RemoveOverlay(uiOverlayController2);
                        uiOverlayField2.SetValue(__instance, null);
                    }
                }

                if (!isInAdditionalSeat)
                {
                    BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                }

                bool isStashed = isInAdditionalSeat;
                bool isInMain = (bagController != null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger && ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject));

                // Check if object is tracked as main seat occupant (for capacity=1 scenarios with timing issues)
                var trackedObj = (bagController != null) ? BagPatches.GetMainSeatObject(bagController) : null;
                bool isTrackedAsMain = trackedObj != null && ReferenceEquals(trackedObj, targetObject);

                if (isStashed && !isInMain && !isTrackedAsMain)
                {

                    if (__instance != null && __instance.outer != null) __instance.outer.SetNextStateToMain();
                }
                else if (isStashed && !isInMain && isTrackedAsMain)
                {
                    // Don't exit if tracked as main seat

                }

                // Uncap Bag Scale logic - only apply when EnableBalance is true
                if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapBagScale.Value)
                {
                    try
                    {
                        float baggedMass = bagController != null ? bagController.baggedMass : (float)AccessTools.Field(typeof(BaggedObject), "baggedMass").GetValue(__instance);
                        if (__instance != null) BaggedObjectPatches.UpdateBagScale(__instance, baggedMass);

                    }
                    catch (Exception ex)
                    {
                        Log.Error($" [BaggedObject_OnEnter_Postfix] Error uncapping bag scale: {ex}");
                    }
                }
            }
        }

        // Harmony patch for BaggedObject.OnExit.
        // Handles cleanup and state management when exiting the bagged state.
        [HarmonyPatch(typeof(BaggedObject), "OnExit")]
        public class BaggedObject_OnExit
        {
            private static readonly HashSet<GameObject> _suppressedExitObjects = new HashSet<GameObject>();

            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (__instance == null) return true;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnExit] Prefix start for targetObject: {GetSafeName(__instance.targetObject)}");
                }

                // Check if we should keep the overrides (i.e. object is still being held/tracked)
                var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                bool shouldKeepOverrides = false;

                if (bagController != null && __instance.targetObject != null)
                {
                    // Check if object is still tracked as main seat
                    var tracked = BagPatches.GetMainSeatObject(bagController);
                    bool isTrackedAsMain = tracked != null && ReferenceEquals(__instance.targetObject, tracked);

                    // Check if object is physically in seat
                    bool isPhysicallyInSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                              ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, __instance.targetObject);

                    // We keep overrides if it's tracked or physically present, AND not dead/destroyed
                    bool isDeadCheck = false;
                    try { isDeadCheck = __instance.targetObject.GetComponent<HealthComponent>()?.alive == false; } catch { isDeadCheck = true; }

                    if ((isTrackedAsMain || isPhysicallyInSeat) && !isDeadCheck && __instance.targetObject.activeInHierarchy)
                    {
                        shouldKeepOverrides = true;
                    }
                }

                if (shouldKeepOverrides)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                         Log.Info($" [BaggedObject_OnExit] Skipping UnsetAllOverrides - object {GetSafeName(__instance.targetObject)} is still tracked or in seat.");
                    }
                }
                else
                {
                    // This ensures that even if we skip the original OnExit or it fails, the overrides are gone.
                    UnsetAllOverrides(__instance);
                }

                bool isSuppressed = false;
                if (__instance.targetObject)
                {
                    lock (_suppressedExitObjects)
                    {
                        if (_suppressedExitObjects.Contains(__instance.targetObject))
                        {
                            isSuppressed = true;
                            _suppressedExitObjects.Remove(__instance.targetObject);
                        }
                    }
                }

                if (isSuppressed)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] Suppressed OnExit (Persistence Auto-Grab Prevention)");
                    }
                    return false;
                }

                if (!__instance.targetObject)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] targetObject is null/destroyed, skipping original OnExit to prevent NRE (cleanup already attempted).");
                    }

                    // Manually trigger junk spawning since we're skipping vanilla OnExit
                    // Vanilla OnExit would call ExecuteBody() when HoldsDeadBody() is true
                    TrySpawnJunkForSkippedOnExit(__instance, "null/destroyed targetObject");
                    RemoveWalkSpeedPenalty(__instance);
                    return false;
                }

                bool isDead = false;
                try
                {
                    var hc = __instance.targetObject.GetComponent<HealthComponent>();
                    isDead = hc != null && !hc.alive;
                }
                catch
                {
                    isDead = true;
                }

                if (isDead)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] targetObject is dead/dying ({GetSafeName(__instance.targetObject)}), skipping original OnExit to avoid crashes (cleanup already attempted).");
                    }
                    // Also need to spawn junk for dead bodies since we're skipping vanilla OnExit
                    TrySpawnJunkForSkippedOnExit(__instance, $"dead/dying {GetSafeName(__instance.targetObject)}");
                    RemoveWalkSpeedPenalty(__instance);
                    return false;
                }

                return true;
            }

            private static void UnsetAllOverrides(BaggedObject instance)
            {
                try
                {

                    // Method 1: Field-based cleanup (the standard way)
                    // Unset Utility
                    var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                    var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");

                    if (overriddenUtilityField != null && utilityOverrideField != null)
                    {
                        var overriddenUtility = (GenericSkill)overriddenUtilityField.GetValue(instance);
                        var utilityOverride = (SkillDef)utilityOverrideField.GetValue(instance);

                        if (overriddenUtility != null)
                        {

                            overriddenUtility.UnsetSkillOverride(instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                            overriddenUtilityField.SetValue(instance, null);
                        }
                    }

                    // Unset Primary
                    var overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
                    var primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");

                    if (overriddenPrimaryField != null && primaryOverrideField != null)
                    {
                        var overriddenPrimary = (GenericSkill)overriddenPrimaryField.GetValue(instance);
                        var primaryOverride = (SkillDef)primaryOverrideField.GetValue(instance);

                        if (overriddenPrimary != null)
                        {

                            overriddenPrimary.UnsetSkillOverride(instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                            overriddenPrimaryField.SetValue(instance, null);
                        }
                    }

                    // Method 2: Nuclear Option - Scan the character's skills directly for any override sourced by this instance
                    var body = instance.outer?.GetComponent<CharacterBody>();
                    if (body && body.skillLocator)
                    {
                        CleanupSkillFromLocator(instance, body.skillLocator.primary);
                        CleanupSkillFromLocator(instance, body.skillLocator.secondary);
                        CleanupSkillFromLocator(instance, body.skillLocator.utility);
                        CleanupSkillFromLocator(instance, body.skillLocator.special);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in UnsetAllOverrides: {ex.Message}\n{ex.StackTrace}");
                }
            }

            private static void RemoveWalkSpeedPenalty(BaggedObject instance)
            {
                if (instance == null || instance.outer == null) return;
                try
                {
                    var motor = instance.outer.gameObject.GetComponent<CharacterMotor>();
                    if (motor == null) return;

                    var modifierField = AccessTools.Field(typeof(BaggedObject), "walkSpeedModifier");
                    if (modifierField != null)
                    {
                        var modifier = modifierField.GetValue(instance) as CharacterMotor.WalkSpeedPenaltyModifier;
                        if (modifier != null)
                        {

                            motor.RemoveWalkSpeedPenalty(modifier);
                            modifierField.SetValue(instance, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in RemoveWalkSpeedPenalty: {ex.Message}");
                }
            }

            // When we skip vanilla OnExit (targetObject is null/destroyed or dead),
            // manually trigger junk spawning since vanilla OnExit.ExecuteBody() won't run.
            private static void TrySpawnJunkForSkippedOnExit(BaggedObject instance, string reason)
            {
                try
                {
                    DrifterBagController? drifterBagController = null;

                    // Method 1: Try Traverse to get the private field
                    try
                    {
                        drifterBagController = Traverse.Create(instance).Field("drifterBagController").GetValue<DrifterBagController>();
                    }
                    catch (Exception ex)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($" [TrySpawnJunk] Traverse failed: {ex.Message}");
                    }

                    // Method 2: Fallback to GetComponent via outer
                    if (drifterBagController == null && instance.outer != null && instance.outer.gameObject != null)
                    {
                        drifterBagController = instance.outer.gameObject.GetComponent<DrifterBagController>();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($" [TrySpawnJunk] Traverse returned null, GetComponent returned: {(drifterBagController != null ? drifterBagController.name : "null")}");
                    }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TrySpawnJunk] Reason: {reason}");
                        Log.Info($" [TrySpawnJunk] drifterBagController: {(drifterBagController != null ? drifterBagController.name : "NULL")}");
                        Log.Info($" [TrySpawnJunk] NetworkServer.active: {NetworkServer.active}");
                        Log.Info($" [TrySpawnJunk] instance.outer: {(instance.outer != null ? instance.outer.name : "NULL")}");
                        if (drifterBagController != null)
                        {
                            Log.Info($" [TrySpawnJunk] baggedBody: {(drifterBagController.baggedBody != null ? drifterBagController.baggedBody.name : "NULL")}");
                            Log.Info($" [TrySpawnJunk] baggedAttributes: {(drifterBagController.baggedAttributes != null ? drifterBagController.baggedAttributes.name : "NULL")}");
                        }
                    }

                    if (drifterBagController != null && NetworkServer.active)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [TrySpawnJunk] >>> Calling ExecuteBody() to spawn junk");
                        }
                        drifterBagController.ExecuteBody();
                        drifterBagController.ResetBaggedObject();
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TrySpawnJunk] >>> SKIPPED ExecuteBody - controller null: {drifterBagController == null}, server: {NetworkServer.active}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($" [TrySpawnJunk] Error: {ex.Message}\n{ex.StackTrace}");
                }
            }

            private static void CleanupSkillFromLocator(BaggedObject instance, GenericSkill skill)
            {
                if (!skill) return;
                try
                {
                    // skill.skillOverrides is private List<GenericSkill.SkillOverride>
                    var overridesField = AccessTools.Field(typeof(GenericSkill), "skillOverrides");
                    var overridesList = (System.Collections.IList)overridesField.GetValue(skill);
                    if (overridesList == null) return;

                    // Iterate backwards to safely remove
                    for (int i = overridesList.Count - 1; i >= 0; i--)
                    {
                        var skillOverride = overridesList[i];
                        // skillOverride is a private struct GenericSkill.SkillOverride
                        var sourceField = skillOverride.GetType().GetField("source", BindingFlags.Public | BindingFlags.Instance);
                        var source = sourceField?.GetValue(skillOverride);

                        if (ReferenceEquals(source, instance))
                        {
                            var skillDefField = skillOverride.GetType().GetField("skillDef", BindingFlags.Public | BindingFlags.Instance);
                            var skillDef = skillDefField?.GetValue(skillOverride) as SkillDef;
                            var priorityField = skillOverride.GetType().GetField("priority", BindingFlags.Public | BindingFlags.Instance);
                            var priority = (GenericSkill.SkillOverridePriority)(priorityField?.GetValue(skillOverride) ?? GenericSkill.SkillOverridePriority.Contextual);

                            if (skillDef != null)
                            {
                                skill.UnsetSkillOverride(instance, skillDef, priority);
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            }
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
                if (bagController == null || __instance?.targetObject == null) return;

                // Check if this object was the main seat occupant and is not in an additional seat
                var tracked = BagPatches.GetMainSeatObject(bagController);
                bool isTrackedAsMain = tracked != null && ReferenceEquals(__instance.targetObject, tracked);
                bool inAdditionalSeat = BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null;

                // Check if the object is still actually in a seat (main or additional)
                bool stillInMainSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                       ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, __instance.targetObject);
                bool stillInAnySeat = stillInMainSeat || inAdditionalSeat;

                // Only remove from bag if it was the main seat occupant, not moved to additional seat, and not still in any seat
                // But if the client has authority over the bag controller
                bool isDead = false;
                bool isDestroyed = __instance.targetObject == null || !__instance.targetObject.activeInHierarchy;

                if (__instance.targetObject != null && !isDestroyed)
                {
                    var soa = __instance.targetObject.GetComponent<SpecialObjectAttributes>();
                    if (soa != null && soa.durability <= 0)
                    {
                        isDead = true;
                    }
                }

                try
                {
                    if (!isDead && __instance.targetObject != null)
                    {
                        var holdsDeadBodyMethod = AccessTools.Method(typeof(BaggedObject), "HoldsDeadBody");
                        if (holdsDeadBodyMethod != null)
                        {
                            isDead = (bool)holdsDeadBodyMethod.Invoke(__instance, null);
                        }
                    }
                }
                catch (Exception)
                {
                }

                bool shouldRemove = isDead || isDestroyed;
                bool hasAuthority = bagController != null && bagController.hasAuthority;

                // Don't remove during swapping or auto-grab operations
                bool inSwapOrAutoGrab = DrifterBossGrabPlugin.IsSwappingPassengers ||
                                         CycleNetworkHandler.SuppressBroadcasts;
                if (inSwapOrAutoGrab && !shouldRemove)
                {

                    return;
                }

                if (isTrackedAsMain && !inAdditionalSeat && !stillInAnySeat && (!hasAuthority || shouldRemove))
                {
                    // Check server's authoritative state from network controller before allowing removal
                    Networking.BottomlessBagNetworkController? netController = null;
                    if (bagController != null)
                    {
                        netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                        if (netController != null)
                        {

                        }
                    }

                // Only allow removal if server indicates object is NOT in main seat (selectedIndex < 0)
                bool serverIndicatesObjectNotInMainSeat = netController != null && netController!.selectedIndex < 0;

                if (!serverIndicatesObjectNotInMainSeat)
                {

                    return; // Block removal
                }

                    if (bagController != null && __instance.targetObject != null)
                    {
                        BagPassengerManager.RemoveBaggedObject(bagController, __instance.targetObject);
                    }
                }
                else if (hasAuthority)
                {
                    // Nothing
                }
                else if (stillInAnySeat)
                {
                    // Update carousel since the object is still bagged
                    if (bagController != null)
                    {
                        BagCarouselUpdater.UpdateCarousel(bagController);
                    }
                }
            }
        }

        // Harmony patch for BaggedObject.FixedUpdate.
        // Prevents crashes and handles additional seat logic during fixed updates.
        [HarmonyPatch(typeof(BaggedObject), "FixedUpdate")]
        public class BaggedObject_FixedUpdate
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                try
                {
                    if (__instance == null || __instance.targetObject == null) return false;

                    // 1. Check isBody flag
                    var isBodyVal = _isBodyField?.GetValue(__instance);
                    if (isBodyVal is bool isBody && !isBody)
                    {
                        return false;
                    }

                    // 2. Check targetBody reference
                    var targetBody = _targetBodyField?.GetValue(__instance) as UnityEngine.Object;
                    if (targetBody == null)
                    {
                        // Safest to skip if we don't have a valid body reference but might be treated as one
                        return false;
                    }

                    // 2b. Check drifterBagController field
                    var dbc = _drifterBagControllerField?.GetValue(__instance) as UnityEngine.Object;
                    if (dbc == null)
                    {
                        return false;
                    }

                    // 3. Health Check
                    try
                    {
                        var hc = __instance.targetObject.GetComponent<HealthComponent>();
                        if (hc != null && !hc.alive) return false;
                    }
                    catch { return false; }

                    // 4. Additional Seat Check
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        if (BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null)
                        {
                            return false;
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    // Fail safe: If our checks crash, default to skipping vanilla update to be safe
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Warning($"[BaggedObject_FixedUpdate] Error in prefix: {ex}");
                    return false;
                }
            }
        }

        // Harmony patch for BaggedObject.UpdateBaggedObjectMass.
        // Prevents vanilla penalty addition during FixedUpdate.
        [HarmonyPatch(typeof(BaggedObject), "UpdateBaggedObjectMass")]
        public class BaggedObject_UpdateBaggedObjectMass
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                // Check if we should suppress vanilla penalty updates
                if (__instance == null || __instance.outer == null)
                {
                    return true;
                }

                // Check if this is a mod-managed bag controller
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    return true;
                }

                // Suppress the vanilla penalty update
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObject_UpdateBaggedObjectMass] Suppressing vanilla penalty update for {__instance.targetObject?.name ?? "null"}");
                }
                return false;
            }
        }

        // Harmony patch for EntityStateMachine.SetNextStateToMain.
        [HarmonyPatch(typeof(RoR2.EntityStateMachine), "SetNextStateToMain")]
        public class EntityStateMachine_SetNextStateToMain
        {
            [HarmonyPrefix]
            public static bool Prefix(RoR2.EntityStateMachine __instance)
            {
                if (__instance != null && __instance.customName == "Bag")
                {
                    var bagController = __instance.gameObject.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        var passenger = BagPatches.GetMainSeatObject(bagController);
                        if (passenger == null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger)
                        {
                            passenger = bagController.vehicleSeat.NetworkpassengerBodyObject;
                        }

                        if (passenger != null)
                        {
                            // Check if the passenger is actually tracked as a bagged object
                            // Check if passenger is actually tracked as a bagged object
                            // For capacity=1, check mainSeatDict (authoritative for main seat objects)
                            // For capacity>1, check BaggedObjects list (includes stashed objects)
                            bool isTracked = false;
                            var mainSeatObject = BagPatches.GetMainSeatObject(bagController);
                            if (mainSeatObject != null && ReferenceEquals(mainSeatObject, passenger))
                            {
                                // Object is tracked as main seat occupant
                                isTracked = true;
                            }
                            else if (BagPatches.GetState(bagController).BaggedObjects.Contains(passenger))
                            {
                                // Object is tracked in bag (e.g., stashed in additional seat)
                                isTracked = true;
                            }

                            // Block reset if we have a passenger that is still considered "bagged"
                            // or if it is explicitly suppressed
                            // If it's not tracked, allow reset to Idle
                            if (isTracked || BaggedObjectPatches.IsObjectExitSuppressed(passenger))
                            {

                                return false; // Prevent reset to Idle
                            }
                        }
                    }
                }

                return true;
            }

        }

        // Harmony patch for EntityStateMachine.SetState.
        // Detects when a bagged creature's ESM transitions out of GenericCharacterVehicleSeated
        [HarmonyPatch(typeof(RoR2.EntityStateMachine), "SetState")]
        public class EntityStateMachine_SetState
        {
            [HarmonyPrefix]
            public static void Prefix(RoR2.EntityStateMachine __instance, EntityState newState)
            {
                if (__instance == null) return;
                if (newState == null) return;

                // Skip the Drifter's own Bag state machine
                if (__instance.customName == "Bag") return;
                if (__instance.customName != "Body") return;

                // O(1) check: does this object have a BaggedObjectTracker? (added when object is grabbed)
                var tracker = __instance.gameObject.GetComponent<BaggedObjectTracker>();
                if (tracker == null) return;

                var controller = tracker.controller;
                if (controller == null) return;

                var obj = __instance.gameObject;
                if (obj == null) return;

                // Ignore during intentional passenger swaps
                if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

                string newStateName = newState.GetType().Name;
                string currentStateName = __instance.state?.GetType()?.Name ?? "null";

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[EntityStateMachine_SetState] Tracked object {obj.name} ESM '{__instance.customName}': {currentStateName} → {newStateName}");
                }

                // Skip safe transitions:
                // - VehicleSeated: object is being seated
                // - SpawnState variants: object is still spawning in
                if (newState is EntityStates.GenericCharacterVehicleSeated) return;
                if (newStateName.Contains("SpawnState")) return;

                // Check if the new state matches the ESM's mainStateType
                // If we are not swapping passengers (cycling)
                var newStateType = newState.GetType();
                var mainStateType = __instance.mainStateType.stateType;
                bool isMainState = (newStateType != null && mainStateType != null && newStateType == mainStateType) || newStateName == "GenericCharacterMain";

                // Refined Logic:
                // - FrolicAway is always an escape
                // - Main state is an escape only if we weren't just Seated (allows cycling VehicleSeated -> Main)
                bool isFrolic = newStateName == "FrolicAway";
                bool isMainEscape = isMainState && !currentStateName.Contains("VehicleSeated");

                bool isKnownEscapeState = isFrolic || isMainEscape;

                if (!isKnownEscapeState)
                {
                    // Log non-escape
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[EntityStateMachine_SetState] Tracked object {obj.name}: unknown non-safe state '{newStateName}' — NOT cleaning up (add to escape list if this causes ghost tracking)");
                    }
                    return;
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[EntityStateMachine_SetState] Bagged object {obj.name} ESM '{__instance.customName}' transitioning {currentStateName} → {newStateName} — cleaning up bag tracking");
                }

                // Clean up
                try
                {
                    BagPassengerManager.RemoveBaggedObject(controller, obj);
                }
                catch (Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Warning($"[EntityStateMachine_SetState] Error during RemoveBaggedObject cleanup: {ex.Message}");
                }
            }
        }

    }
}
