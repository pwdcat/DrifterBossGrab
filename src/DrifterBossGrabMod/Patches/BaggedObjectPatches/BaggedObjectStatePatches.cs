#nullable enable
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
        private static string GetSafeName(UnityEngine.Object? obj) => obj ? obj!.name : "null";
        // GenericSkill.SkillOverride struct field cache
        private static readonly FieldInfo _skillOverrideSourceField = typeof(GenericSkill.SkillOverride).GetField("source", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo _skillOverrideSkillDefField = typeof(GenericSkill.SkillOverride).GetField("skillDef", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo _skillOverridePriorityField = typeof(GenericSkill.SkillOverride).GetField("priority", BindingFlags.Public | BindingFlags.Instance);

        // Track last processed object to prevent infinite re-entry during sync issues
        private static GameObject? _lastProcessedObject;
        private static float _lastProcessTime;

        // Public entry point for cleanup of overrides when BaggedObject.OnExit may not have run
        public static void ForceCleanupOverrides(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return;
            var existingState = BaggedObjectPatches.FindExistingBaggedObjectState(bagController, targetObject);
            if (existingState != null)
            {
                BaggedObject_OnExit.UnsetAllOverrides(existingState);
            }
        }
 
        // Harmony patch for BaggedObject.OnEnter.
        // Handles initialization and state management when entering into bagged state.
        [HarmonyPatch(typeof(BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter
        {
            // Flag to signal that we are currently initializing a BaggedObject on a client
            // This is used by VehicleSeat.AssignPassenger patch to block assignment
            public static GameObject? InitializingPassenger;
  
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                // Guard against infinite re-entry during sync issues
                if (__instance == null)
                {
                    Log.Warning("[BaggedObject_OnEnter.Prefix] __instance is null");
                    return false;
                }

                if (__instance.targetObject != null)
                {
                    var currentTime = Time.time;
                    if (ReferenceEquals(__instance.targetObject, _lastProcessedObject) &&
                        (currentTime - _lastProcessTime) < 0.5f)
                    {
                        // Same object processed very recently - likely a re-entry loop
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[BaggedObject_OnEnter.Prefix] Blocking re-entry for {__instance.targetObject.name} (processed {(currentTime - _lastProcessTime):F3}s ago)");
                        return false;
                    }
                }

                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    Log.Warning("[BaggedObject_OnEnter.Prefix] bagController is null, proceeding with vanilla OnEnter");
                    return true;
                }

                var targetObject = __instance?.targetObject;
                if (targetObject == null)
                {
                    // This can happen when deserialization fails or object was destroyed
                    Log.Warning("[BaggedObject_OnEnter.Prefix] targetObject is null - likely deserialization failure or object destroyed");
                    NetworkUtils.LogObjectDetails(__instance?.outer?.gameObject, "BaggedObject_OnEnter.Prefix");
                    return false; // Skip to original OnEnter to prevent NRE
                }

                // Validate that the target object is ready for network operations
                if (!Networking.NetworkUtils.ValidateObjectReady(targetObject))
                {
                    Log.Warning($"[BaggedObject_OnEnter.Prefix] {targetObject.name} is not ready for network operations");
                    return false;
                }

                // Check if object is currently undergoing throw operation
                if (ProjectileRecoveryPatches.IsUndergoingThrowOperation(targetObject))
                {
                    Log.Warning($"[BaggedObject_OnEnter.Prefix] Blocking grab of {targetObject.name} - object is currently undergoing throw operation");
                    return false;
                }

                // Log the OnEnter operation
                Networking.NetworkUtils.LogNetworkOperation("BaggedObject_OnEnter", targetObject, NetworkServer.active, new Dictionary<string, object>
                {
                    { "bagController", bagController.name },
                    { "isAuthority", bagController.hasAuthority }
                });

                // Check if targetObject is in additional seat
                var seatDict = BagPatches.GetState(bagController).AdditionalSeats;
                if (seatDict != null && seatDict.TryGetValue(targetObject, out var additionalSeat))
                {
                    // Assign to additional seat instead of main
                    additionalSeat.AssignPassenger(targetObject);
                    // Don't call original OnEnter logic
                    return false;
                }

                // Client with capacity > 1: allow OnEnter to run for initialization, but block seat assignment
                // We use InitializingPassenger flag to tell VehicleSeat.AssignPassenger to skip
                if (!NetworkServer.active && bagController.hasAuthority)
                {
                    int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                    bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;
                    
                    if (effectiveCapacity > 1 && !prioritize)
                    {
                        var list = BagPatches.GetState(bagController).BaggedObjects;
                        bool isAlreadyTracked = list.Contains(targetObject);

                        if (!isAlreadyTracked)
                        {
                            // Check if bag is already full before allowing grab
                            int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                            if (currentCount >= effectiveCapacity)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Debug($"[BaggedObject_OnEnter.Prefix] Client BLOCKING grab of {targetObject.name} - bag full ({currentCount}/{effectiveCapacity})");
                                return false;
                            }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for NEW GRAB of {targetObject!.name} (capacity={effectiveCapacity}) but FLAGGING to block seat assignment");

                            InitializingPassenger = targetObject;

                            list.Add(targetObject);
                            BagHelpers.AddTracker(bagController, targetObject);
                            BagCarouselUpdater.UpdateCarousel(bagController);
                            BagCarouselUpdater.UpdateNetworkBagState(bagController);
                            BagPassengerManager.ForceRecalculateMass(bagController);
                        }
                        else
                        {
                            // Object is already in the bag
                            // But if the bag is over capacity, block to prevent forced cycling overrides
                            int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                            if (currentCount > effectiveCapacity)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Debug($"[BaggedObject_OnEnter.Prefix] Client BLOCKING CYCLING of {targetObject!.name} - bag over capacity ({currentCount}/{effectiveCapacity})");
                                return false;
                            }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for CYCLING of {targetObject!.name} (capacity={effectiveCapacity})");
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
                
                BaggedObject_OnExit.MarkObjectSuccessfullyInitialized(targetObject);

                // Restore breakout timer progress when entering main seat
                if (NetworkServer.active)
                {
                    var savedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (savedState != null)
                    {
                        if (ReflectionCache.EntityState.FixedAge != null && savedState.elapsedBreakoutTime > 0f)
                        {
                            ReflectionCache.EntityState.FixedAge.SetValue(__instance, savedState.elapsedBreakoutTime);
                            
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Debug($"[DEBUG] [BaggedObject_OnEnter] Restored main seat breakout timer for {targetObject!.name} to {savedState.elapsedBreakoutTime:F2}s");
                            }
                        }

                        if (savedState.breakoutTime > 0f)
                        {
                            if (ReflectionCache.BaggedObject.BreakoutTime != null) ReflectionCache.BaggedObject.BreakoutTime.SetValue(__instance, savedState.breakoutTime);
                        }

                        if (savedState.breakoutAttempts > 0f)
                        {
                            if (ReflectionCache.BaggedObject.BreakoutAttempts != null) ReflectionCache.BaggedObject.BreakoutAttempts.SetValue(__instance, savedState.breakoutAttempts);
                        }
                    }
                }

                // Check if object is in an additional seat - this is used in multiple places
                bool isInAdditionalSeat = BagHelpers.GetAdditionalSeat(bagController, targetObject) != null;
                bool wasNewlyAddedToBag = false;

                // Only populate if the network controller hasn't synced a null state (selectedIndex=-1)
                if (bagController.hasAuthority && !NetworkServer.active)
                {
                    // Don't populate main seat on client for new grabs when capacity > 1
                    // But do allow it during cycling
                    int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                    bool isAlreadyTracked = BagPatches.GetState(bagController).BaggedObjects.Contains(targetObject);
                    bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;

                    if (effectiveCapacity > 1 && !isAlreadyTracked && !prioritize)
                    {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[BaggedObject_OnEnter.Postfix] Client skipping main seat population for NEW GRAB of {targetObject!.name} (capacity={effectiveCapacity})");
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
            var state = BagPatches.GetState(bagController);
            var list = state.BaggedObjects;
            if (list != null && !list.Contains(targetObject))
            {
                list.Add(targetObject);
                state.AddInstanceId(targetObject.GetInstanceID());
                BagHelpers.AddTracker(bagController, targetObject);
                wasNewlyAddedToBag = true;
            }
                }

                var outerMainSeat = bagController!.vehicleSeat;

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
                        var uiOverlayController = (OverlayController)ReflectionCache.BaggedObject.UIOverlayController.GetValue(__instance);
                        if (uiOverlayController != null)
                        {
                            HudOverlayManager.RemoveOverlay(uiOverlayController);
                            ReflectionCache.BaggedObject.UIOverlayController.SetValue(__instance, null);
                        }
                    }
                }
                // Ensure UI and networking are updated for new grabs
                if (bagController != null && targetObject != null)
                {
                    BagCarouselUpdater.UpdateCarousel(bagController);
                    
                    // Sync to network so server knows about client grabs ONLY if it's a new grab
                    if (wasNewlyAddedToBag && bagController.hasAuthority)
                    {
                        BagCarouselUpdater.UpdateNetworkBagState(bagController);
                    }
                }
                else
                {
                    // Ensure UI is created/refreshed for main seat objects
                    if (bagController != null && targetObject != null && !isInAdditionalSeat)
                    {
                        BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    }
                }
                // Remove the overlay to use carousel instead
                if (PluginConfig.Instance.EnableCarouselHUD.Value)
                {
                    var uiOverlayController2 = (OverlayController)ReflectionCache.BaggedObject.UIOverlayController.GetValue(__instance);
                    if (uiOverlayController2 != null)
                    {
                        HudOverlayManager.RemoveOverlay(uiOverlayController2);
                        ReflectionCache.BaggedObject.UIOverlayController.SetValue(__instance, null);
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
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                    if (PluginConfig.Instance.IsBagScaleCapInfinite || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                    {
                        try
                        {
                            float baggedMass = bagController != null ? bagController.baggedMass : (float)ReflectionCache.BaggedObject.BaggedMass.GetValue(__instance);
                            if (__instance != null) BaggedObjectPatches.UpdateBagScale(__instance, baggedMass);
                            else
                            {
                                Log.Warning($"[BaggedObject_OnEnter.Postfix] __instance is null, cannot update bag scale");
                            }

                        }
                        catch (Exception ex)
                        {
                            Log.Error($" [BaggedObject_OnEnter_Postfix] Error uncapping bag scale: {ex}");
                        }
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
            private static readonly HashSet<GameObject> _preserveOverridesDuringCycling = new HashSet<GameObject>();
            
            private static readonly HashSet<GameObject> _successfullyInitializedObjects = new HashSet<GameObject>();
            
            public static void MarkObjectSuccessfullyInitialized(GameObject obj)
            {
                if (obj == null) return;
                lock (_successfullyInitializedObjects)
                {
                    _successfullyInitializedObjects.Add(obj);
                }
            }
            
            public static void ClearObjectSuccessfullyInitialized(GameObject obj)
            {
                if (obj == null) return;
                lock (_successfullyInitializedObjects)
                {
                    _successfullyInitializedObjects.Remove(obj);
                }
            }

            public static void MarkPreserveOverridesDuringCycling(GameObject obj)
            {
                if (obj == null) return;
                lock (_preserveOverridesDuringCycling)
                {
                    _preserveOverridesDuringCycling.Add(obj);
                }
            }

            public static void ClearPreserveOverridesDuringCycling(GameObject obj)
            {
                if (obj == null) return;
                lock (_preserveOverridesDuringCycling)
                {
                    _preserveOverridesDuringCycling.Remove(obj);
                }
            }

            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (__instance == null)
                {
                    Log.Warning("[BaggedObject_OnExit.Prefix] __instance is null");
                    return true;
                }

                var bagController = __instance.outer?.GetComponent<DrifterBagController>();

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var currentMain = bagController != null ? BagPatches.GetMainSeatObject(bagController) : null;
                    var bagStateMachine = EntityStateMachine.FindByCustomName(__instance.outer?.gameObject, "Bag");
                    var currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                    var currentTarget = bagStateMachine?.state is BaggedObject bagged ? bagged.targetObject : null;

                    Log.Debug($"[BaggedObject_OnExit.Prefix] CALLED: InstanceTarget={GetSafeName(__instance?.targetObject)}, StateTarget={GetSafeName(currentTarget)}, State={currentStateName}, MainPassenger={GetSafeName(currentMain)}");
                }

                // Check if we should keep the overrides (i.e. object is still being held/tracked)
                if (bagController == null)
                {
                    Log.Warning("[BaggedObject_OnExit.Prefix] bagController is null, proceeding with vanilla OnExit");
                    return true;
                }

                // Validate target object
                if (__instance == null || __instance.targetObject == null)
                {
                    Log.Warning("[BaggedObject_OnExit.Prefix] targetObject is null - likely deserialization failure or object destroyed");
                    NetworkUtils.LogObjectDetails(__instance?.outer?.gameObject, "BaggedObject_OnExit.Prefix");
                    // Continue to handle the null targetObject case below
                }
                else
                {
                    // Validate that the target object is ready
                    if (!NetworkUtils.ValidateObjectReady(__instance.targetObject))
                    {
                        Log.Warning($"[BaggedObject_OnExit.Prefix] {__instance.targetObject.name} is not ready for network operations");
                        // Continue to handle anyway
                    }

                    // Log the OnExit operation
                    NetworkUtils.LogNetworkOperation("BaggedObject_OnExit", __instance.targetObject, NetworkServer.active, new Dictionary<string, object>
                    {
                        { "bagController", bagController.name },
                        { "isAuthority", bagController.hasAuthority }
                    });
                }

                bool shouldKeepOverrides = false;
                bool isDifferentObjectInMainSeat = false;
                bool isDeadCheck = false;
                GameObject? targetObject = __instance?.targetObject;

                if (bagController != null && targetObject != null)
                {
                    // Check if object is still tracked as main seat
                    var tracked = BagPatches.GetMainSeatObject(bagController);
                    bool isTrackedAsMain = tracked != null && ReferenceEquals(targetObject, tracked);

                    // Check if object is physically in seat
                    bool isPhysicallyInSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                            ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject);

                    // If anything is in the main seat that's not this object, force unset overrides
                    isDifferentObjectInMainSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                                        !ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject);

                    // We keep overrides only if THIS object is in the main seat, AND not dead/destroyed
                    try { isDeadCheck = targetObject.TryGetComponent<HealthComponent>(out var healthComponent) && !healthComponent.alive; } catch { isDeadCheck = true; }

                    shouldKeepOverrides = isTrackedAsMain && isPhysicallyInSeat && !isDeadCheck && targetObject.activeInHierarchy && !isDifferentObjectInMainSeat;
                }

                if (shouldKeepOverrides)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                    Log.Info($" [BaggedObject_OnExit] Skipping UnsetAllOverrides - object {GetSafeName(targetObject)} is still tracked or in seat.");
                    }
                }
                else
                {
                    // Check if object is marked to preserve overrides during cycling
                    bool preserveDuringCycling = false;
                    if (targetObject != null)
                    {
                        lock (_preserveOverridesDuringCycling)
                        {
                            preserveDuringCycling = _preserveOverridesDuringCycling.Contains(targetObject!);
                            // Clear the flag after checking to prevent indefinite preservation
                            _preserveOverridesDuringCycling.Remove(targetObject);
                        }
                    }

                    if (preserveDuringCycling && !isDifferentObjectInMainSeat)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject_OnExit] Skipping UnsetAllOverrides - object {GetSafeName(targetObject)} is marked to preserve overrides during cycling.");
                        }
                    }
                    else
                    {
                        if (preserveDuringCycling && PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject_OnExit] Forcing UnsetAllOverrides during cycling - different object in main seat or object is dead.");
                        }
                        // This ensures that even if we skip the original OnExit or it fails, the overrides are gone.
                        if (__instance != null)
                        {
                            UnsetAllOverrides(__instance);
                        }
                    }
                }

                bool isSuppressed = false;
                GameObject? suppressedObject = __instance?.targetObject;
                if (suppressedObject)
                {
                    lock (_suppressedExitObjects)
                    {
                        if (_suppressedExitObjects.Contains(suppressedObject!))
                        {
                            isSuppressed = true;
                            _suppressedExitObjects.Remove(suppressedObject!);
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

                if (!__instance?.targetObject)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] targetObject is null/destroyed, skipping original OnExit to prevent NRE (cleanup already attempted).");
                    }

                    // Manually trigger junk spawning since we're skipping vanilla OnExit
                    // Vanilla OnExit would call ExecuteBody() when HoldsDeadBody() is true
                    if (__instance != null)
                    {
                        TrySpawnJunkForSkippedOnExit(__instance, "null/destroyed targetObject");
                        RemoveWalkSpeedPenalty(__instance);
                    }
                    return false;
                }

                bool isDead = false;
                if (__instance?.targetObject != null)
                {
                    var isInAdditionalSeat = (bagController != null) && BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null;
                    if (!isInAdditionalSeat)
                    {
                        var restoreTarget = __instance.targetObject;
                        var bagState = (bagController != null) ? BagPatches.GetState(bagController) : null;
                        if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(restoreTarget, out var states))
                        {
                            BodyColliderCache.RestoreMovementColliders(states);
                            bagState.DisabledCollidersByObject.Remove(restoreTarget, out _);
                            
                        }

                        // Ensure visual model is synced for world transition
                        var modelLocator = restoreTarget.GetComponent<ModelLocator>();
                        var characterBody = restoreTarget.GetComponent<CharacterBody>();
                        var restoredData = bagController != null ? BaggedObjectPatches.LoadObjectState(bagController, restoreTarget) : null;

                        if (restoredData != null && characterBody != null)
                        {
                            restoredData.ApplyToCharacterBody(characterBody);
                        }

                        if (modelLocator != null)
                        {
                            modelLocator.autoUpdateModelTransform = restoredData != null ? restoredData.originalAutoUpdateModelTransform : true;
                            modelLocator.dontDetatchFromParent = true;

                            // Refresh visual state to clear pink textures or shader artifacts
                            VisualRefreshUtility.Refresh(restoreTarget);
                        }
                        
                        
                    }
                    isDead = __instance.targetObject.TryGetComponent<HealthComponent>(out var hc) && !hc.alive;
                }

                if (isDead)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] targetObject is dead/dying ({GetSafeName(__instance?.targetObject)}), skipping original OnExit to avoid crashes (cleanup already attempted).");
                    }
                    // Also need to spawn junk for dead bodies since we're skipping vanilla OnExit
                    if (__instance != null)
                    {
                        TrySpawnJunkForSkippedOnExit(__instance, $"dead/dying {GetSafeName(__instance?.targetObject)}");
                        RemoveWalkSpeedPenalty(__instance!);
                    }
                    return false;
                }

                return true;
            }

            internal static void UnsetAllOverrides(BaggedObject instance)
            {
                try
                {
                    var body = instance.outer?.GetComponent<CharacterBody>();
                    
                    // Unsubscribe from onSkillChanged FIRST (matches vanilla OnExit order: lines 311-336)
                    // This must happen BEFORE unsetting overrides to prevent stale callbacks
                    if (body && body!.skillLocator)
                    {
                        var utility = body.skillLocator.utility;
                        if (utility && ReflectionCache.BaggedObject.TryOverrideUtility != null)
                        {
                            var utilityDelegate = AccessTools.MethodDelegate<Action<GenericSkill>>(
                                ReflectionCache.BaggedObject.TryOverrideUtility, instance);
                            utility.onSkillChanged -= utilityDelegate;
                        }

                        var primary = body.skillLocator.primary;
                        if (primary && ReflectionCache.BaggedObject.TryOverridePrimary != null)
                        {
                            var primaryDelegate = AccessTools.MethodDelegate<Action<GenericSkill>>(
                                ReflectionCache.BaggedObject.TryOverridePrimary, instance);
                            primary.onSkillChanged -= primaryDelegate;
                        }
                    }

                    // Field-based cleanup (the standard way) - use cached fields, not AccessTools
                    // Unset Utility
                    if (ReflectionCache.BaggedObject.OverriddenUtility != null && ReflectionCache.BaggedObject.UtilityOverride != null)
                    {
                        var overriddenUtility = (GenericSkill)ReflectionCache.BaggedObject.OverriddenUtility.GetValue(instance);
                        var utilityOverride = (SkillDef)ReflectionCache.BaggedObject.UtilityOverride.GetValue(instance);

                        if (overriddenUtility != null)
                        {
                            if (utilityOverride == null)
                            {
                                Log.Warning($"[UnsetAllOverrides] utilityOverride is null from {instance.GetType().Name}");
                                ReflectionCache.BaggedObject.OverriddenUtility.SetValue(instance, null);
                            }
                            else
                            {
                                overriddenUtility.UnsetSkillOverride(instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                                ReflectionCache.BaggedObject.OverriddenUtility.SetValue(instance, null);
                            }
                        }
                    }

                    // Unset Primary - NO early return, always continue
                    if (ReflectionCache.BaggedObject.OverriddenPrimary != null && ReflectionCache.BaggedObject.PrimaryOverride != null)
                    {
                        var overriddenPrimary = (GenericSkill)ReflectionCache.BaggedObject.OverriddenPrimary.GetValue(instance);
                        var primaryOverride = (SkillDef)ReflectionCache.BaggedObject.PrimaryOverride.GetValue(instance);

                        if (overriddenPrimary != null)
                        {
                            if (primaryOverride == null)
                            {
                                Log.Warning($"[UnsetAllOverrides] primaryOverride is null from {instance.GetType().Name}");
                                ReflectionCache.BaggedObject.OverriddenPrimary.SetValue(instance, null);
                            }
                            else
                            {
                                overriddenPrimary.UnsetSkillOverride(instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                                ReflectionCache.BaggedObject.OverriddenPrimary.SetValue(instance, null);
                            }
                        }
                    }

                    // Nuclear Option - Scan the character's skills directly for any override sourced by this instance
                    if (body && body!.skillLocator)
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

                        if (ReflectionCache.BaggedObject.WalkSpeedModifier != null)
                        {
                            var modifier = ReflectionCache.BaggedObject.WalkSpeedModifier.GetValue(instance) as CharacterMotor.WalkSpeedPenaltyModifier;
                            if (modifier != null)
                            {

                                motor.RemoveWalkSpeedPenalty(modifier);
                                ReflectionCache.BaggedObject.WalkSpeedModifier.SetValue(instance, null);
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
            private static void TrySpawnJunkForSkippedOnExit(BaggedObject? instance, string reason)
            {
                try
                {
                    DrifterBagController? drifterBagController = null;

                    // Method 1: Try cached reflection to get the private field
                    try
                    {
                        drifterBagController = ReflectionCache.BaggedObject.DrifterBagController?.GetValue(instance) as DrifterBagController;
                    }
                    catch (Exception ex)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($" [TrySpawnJunk] Reflection failed: {ex.Message}");
                    }

                    // Method 2: Fallback to GetComponent via outer
                    if (drifterBagController == null && instance != null && instance.outer != null && instance.outer.gameObject != null)
                    {
                        drifterBagController = instance.outer.gameObject.GetComponent<DrifterBagController>();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($" [TrySpawnJunk] Traverse returned null, GetComponent returned: {(drifterBagController != null ? drifterBagController.name : "null")}");
                    }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        string bName = GetSafeName(drifterBagController);
                        string bbName = drifterBagController != null ? GetSafeName(drifterBagController.baggedBody) : "NULL";
                        string attrName = drifterBagController != null ? GetSafeName(drifterBagController.baggedAttributes) : "NULL";
                        Log.Info($"[TrySpawnJunk] Reason: {reason} | bagController: {bName} | Server: {NetworkServer.active} | baggedBody: {bbName} | attributes: {attrName}");
                    }

                    if (drifterBagController != null && NetworkServer.active)
                    {
                        bool targetIsDestroyedOrNull = instance?.targetObject == null;
                        bool wasSuccessfullyInitialized = false;
                        if (instance?.outer != null)
                        {
                            var stateMachine = EntityStateMachine.FindByCustomName(instance.outer.gameObject, "Body");
                            if (stateMachine != null)
                            {
                                lock (_successfullyInitializedObjects)
                                {
                                    wasSuccessfullyInitialized = _successfullyInitializedObjects.Contains(stateMachine.gameObject);
                                }
                            }
                        }
                        
                        // Check if we're in a valid swap operation
                        var bagStateMachine = EntityStateMachine.FindByCustomName(drifterBagController.gameObject, "Bag");
                        bool hasValidBaggedObjectState = false;
                        if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo)
                        {
                            hasValidBaggedObjectState = bo.targetObject != null;
                        }
                        
                        bool isSwappingOrHasTarget = DrifterBossGrabPlugin.IsSwappingPassengers || hasValidBaggedObjectState;
                        
                        // Only spawn junk if:
                        // 1. Target is destroyed/null
                        // 2. Not swapping/has target
                        // 3. Object was successfully initialized via OnEnter
                        if (targetIsDestroyedOrNull && !isSwappingOrHasTarget && wasSuccessfullyInitialized)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[TrySpawnJunk] targetObject is null/destroyed — spawning junk WITHOUT ExecuteBody() to avoid incrementing wrong object's invisibilityCount");
                            
                            // Unground the Drifter's motor
                            var drifterBody = drifterBagController.GetComponent<CharacterBody>();
                            var drifterMotor = drifterBody?.characterMotor;
                            if (drifterMotor != null)
                            {
                                drifterMotor.Motor.ForceUnground(0.1f);
                                drifterMotor.velocity = new Vector3(drifterMotor.velocity.x, Mathf.Max(drifterMotor.velocity.y, 8f), drifterMotor.velocity.z);
                            }
                            
                            // Spawn junk
                            Vector3 dropLocation = drifterBody
                                ? drifterBody!.corePosition
                                : drifterBagController!.transform.position;
                            var junkCtrl = ReflectionCache.DrifterBagController.JunkController?.GetValue(drifterBagController) as JunkController;
                            if (junkCtrl != null)
                            {
                                junkCtrl.CallCmdGenerateJunkQuantity(dropLocation, 4);
                            }
                        }
                        else if (targetIsDestroyedOrNull && isSwappingOrHasTarget)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[TrySpawnJunk] SKIPPED junk spawn - null target detected during passenger swap (isSwapping={DrifterBossGrabPlugin.IsSwappingPassengers}, hasValidBaggedObjectState={hasValidBaggedObjectState})");
                        }
                        else if (targetIsDestroyedOrNull && !wasSuccessfullyInitialized)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[TrySpawnJunk] SKIPPED junk spawn - null target detected during grab operation (object was not successfully initialized via OnEnter)");
                        }
                        else
                        {
                            if (drifterBagController.baggedBody != null && instance != null && drifterBagController.baggedBody != instance.targetObject)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[TrySpawnJunk] >>> baggedBody changed (auto-promoted)! Manually spawning junk for {GetSafeName(instance?.targetObject)} to protect new passenger {GetSafeName(drifterBagController.baggedBody)}.");
                                
                                // Decrease invisibility for the actual target
                                if (instance != null && instance.targetObject != null)
                                {
                                    var characterModel = instance.targetObject.GetComponent<ModelLocator>()?.modelTransform?.GetComponent<CharacterModel>();
                                    if (characterModel != null) characterModel.invisibilityCount--;
                                }

                                // Spawn junk manually based on the actual target's attributes
                                var targetAttributes = (instance != null && instance.targetObject != null) ? instance.targetObject.GetComponent<SpecialObjectAttributes>() : null;
                                var drifterBody = drifterBagController.GetComponent<CharacterBody>();
                                Vector3 dropLocation = drifterBody ? drifterBody.corePosition : drifterBagController.transform.position;
                                
                                int scrapCount = 4; // Default fallback for medium enemies
                                var junkCtrl = ReflectionCache.DrifterBagController.JunkController?.GetValue(drifterBagController) as JunkController;
                                if (junkCtrl != null) junkCtrl.CallCmdGenerateJunkQuantity(dropLocation, scrapCount);
                            }
                            else
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[TrySpawnJunk] >>> Calling ExecuteBody() to spawn junk for {GetSafeName(instance?.targetObject)}");
                                drifterBagController!.ExecuteBody();
                                drifterBagController.ResetBaggedObject();
                            }
                        }
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[TrySpawnJunk] >>> SKIPPED ExecuteBody - controller null: {drifterBagController == null}, server: {NetworkServer.active}");
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
                    if (ReflectionCache.GenericSkill.SkillOverrides == null || _skillOverrideSourceField == null) return;
                    var overridesList = (System.Collections.IList)ReflectionCache.GenericSkill.SkillOverrides.GetValue(skill);
                    if (overridesList == null) return;

                    // Iterate backwards to safely remove
                    for (int i = overridesList.Count - 1; i >= 0; i--)
                    {
                        var skillOverride = overridesList[i];
                        // skillOverride is a private struct GenericSkill.SkillOverride
                        var source = _skillOverrideSourceField?.GetValue(skillOverride);

                        if (ReferenceEquals(source, instance))
                        {
                            var skillDef = _skillOverrideSkillDefField?.GetValue(skillOverride) as SkillDef;
                            var priority = (GenericSkill.SkillOverridePriority)(_skillOverridePriorityField?.GetValue(skillOverride) ?? GenericSkill.SkillOverridePriority.Contextual);

                            if (skillDef != null)
                            {
                                skill.UnsetSkillOverride(instance, skillDef, priority);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[CleanupSkillFromLocator] Failed to cleanup skill overrides: {ex.Message}\n{ex.StackTrace}");
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
                        if (ReflectionCache.BaggedObject.HoldsDeadBody != null)
                        {
                            isDead = (bool)ReflectionCache.BaggedObject.HoldsDeadBody.Invoke(__instance, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[BaggedObject_OnExit.Postfix] Failed to check HoldsDeadBody: {ex.Message}\n{ex.StackTrace}");
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

                // Only allow removal if server indicates object is not in main seat (selectedIndex < 0)
                bool serverIndicatesObjectNotInMainSeat = netController != null && netController!.selectedIndex < 0;

                if (!serverIndicatesObjectNotInMainSeat)
                {

                    return;
                }

                    if (bagController != null && __instance.targetObject != null)
                    {
                        BagPassengerManager.RemoveBaggedObject(bagController, __instance.targetObject);
                    }
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
            // Throttle debug logging to avoid spamming every FixedUpdate frame
            private static float _lastFixedUpdateLogTime;
            private static string _lastFixedUpdateBlockReason = "";
            
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                try
                {
                    bool shouldLog = PluginConfig.Instance.EnableDebugLogs.Value && (Time.time - _lastFixedUpdateLogTime > 2f);
                    
                    if (__instance == null || __instance.targetObject == null)
                    {
                        if (shouldLog && _lastFixedUpdateBlockReason != "null_instance")
                        {
                            _lastFixedUpdateBlockReason = "null_instance";
                            _lastFixedUpdateLogTime = Time.time;
                            Log.Info($"[BaggedObject_FixedUpdate] BLOCKED: instance or targetObject is null");
                        }
                        return false;
                    }

                    // 1. Check isBody flag
                    var isBodyVal = ReflectionCache.BaggedObject.IsBody?.GetValue(__instance);
                    if (isBodyVal is bool isBody && !isBody)
                    {
                        if (shouldLog && _lastFixedUpdateBlockReason != "isBody_false")
                        {
                            _lastFixedUpdateBlockReason = "isBody_false";
                            _lastFixedUpdateLogTime = Time.time;
                            Log.Info($"[BaggedObject_FixedUpdate] BLOCKED: isBody=false for {__instance.targetObject.name}");
                        }
                        return false;
                    }

                    // 2. Check targetBody reference
                    var targetBody = ReflectionCache.BaggedObject.TargetBody?.GetValue(__instance) as UnityEngine.Object;
                    if (targetBody == null)
                    {
                        if (shouldLog && _lastFixedUpdateBlockReason != "targetBody_null")
                        {
                            _lastFixedUpdateBlockReason = "targetBody_null";
                            _lastFixedUpdateLogTime = Time.time;
                            Log.Info($"[BaggedObject_FixedUpdate] BLOCKED: targetBody is null for {__instance.targetObject.name}");
                        }
                        return false;
                    }

                    // 2b. Check drifterBagController field
                    var dbc = ReflectionCache.BaggedObject.DrifterBagController?.GetValue(__instance) as UnityEngine.Object;
                    if (dbc == null)
                    {
                        if (shouldLog && _lastFixedUpdateBlockReason != "dbc_null")
                        {
                            _lastFixedUpdateBlockReason = "dbc_null";
                            _lastFixedUpdateLogTime = Time.time;
                            Log.Info($"[BaggedObject_FixedUpdate] BLOCKED: drifterBagController is null for {__instance.targetObject.name}");
                        }
                        return false;
                    }

                    // 3. Health Check
                    try
                    {
                        var hc = __instance.targetObject.GetComponent<HealthComponent>();
                        if (hc != null && !hc.alive)
                        {
                            if (shouldLog && _lastFixedUpdateBlockReason != "dead")
                            {
                                _lastFixedUpdateBlockReason = "dead";
                                _lastFixedUpdateLogTime = Time.time;
                                Log.Info($"[BaggedObject_FixedUpdate] BLOCKED: target is dead for {__instance.targetObject.name}");
                            }
                            return false;
                        }
                    }
                    catch { return false; }

                    // 4. Additional Seat Check
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        if (BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null)
                        {
                            if (shouldLog && _lastFixedUpdateBlockReason != "additional_seat")
                            {
                                _lastFixedUpdateBlockReason = "additional_seat";
                                _lastFixedUpdateLogTime = Time.time;
                                Log.Info($"[BaggedObject_FixedUpdate] BLOCKED: target is in additional seat for {__instance.targetObject.name}");
                            }
                            return false;
                        }
                    }

                    // Log that FixedUpdate is allowed to run (throttled)
                    if (shouldLog && _lastFixedUpdateBlockReason != "allowed")
                    {
                        float currentAge = ReflectionCache.EntityState.FixedAge != null ? (float)ReflectionCache.EntityState.FixedAge.GetValue(__instance) : -1f;
                        float bTime = ReflectionCache.BaggedObject.BreakoutTime != null ? (float)ReflectionCache.BaggedObject.BreakoutTime.GetValue(__instance) : -1f;
                        float bAttempts = ReflectionCache.BaggedObject.BreakoutAttempts != null ? (float)ReflectionCache.BaggedObject.BreakoutAttempts.GetValue(__instance) : -1f;

                        _lastFixedUpdateBlockReason = "allowed";
                        _lastFixedUpdateLogTime = Time.time;
                        Log.Info($"[BaggedObject_FixedUpdate] allowed for {__instance.targetObject.name}: fixedAge={currentAge:F2}, breakoutTime={bTime:F2}, attempts={bAttempts}");
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

                if (tracker.isRemovingManual) return;

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

                // Refined Logic (Safe-List):
                // - Idle and Uninitialized are safe fallback states.
                // - Main state is safe ONLY if we just transitioned from VehicleSeated (allows intentional cycling).
                // - StunState is safe ONLY if we just transitioned from VehicleSeated (prevents breakout during cycling).
                bool isIdleOrInit = newStateName.Contains("Idle") || newStateName.Contains("Uninitialized");
                bool isMainSafe = isMainState && currentStateName.Contains("VehicleSeated");
                bool isStunSafe = newStateName.Contains("StunState") && currentStateName.Contains("VehicleSeated");
                
                bool isSafeState = isIdleOrInit || isMainSafe || isStunSafe;

                if (isSafeState)
                {
                    return; // Safe transition inside the bag, do not clean up
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[EntityStateMachine_SetState] Bagged object {obj.name} ESM '{__instance.customName}' transitioning {currentStateName} → {newStateName} (UNAUTHORIZED) — treating as escape and cleaning up bag tracking");
                }

                // Clean up
                try
                {
                    BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying: true);
                }
                catch (Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Warning($"[EntityStateMachine_SetState] Error during RemoveBaggedObject cleanup: {ex.Message}");
                }

                // Force immediate carousel refresh after cleanup
                if (controller != null)
                {
                    BagCarouselUpdater.UpdateCarousel(controller);
                }
            }
        }

    }
}
