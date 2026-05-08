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
using DrifterBossGrabMod.Networking;
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // BAGGED OBJECT STATE PATCHES
    // ========================================================================================

    public static class BaggedObjectStatePatches
    {
        public static void PerformPassengerRestoration(DrifterBagController? bagController, UnityEngine.GameObject? restoreTarget, bool force = false)
        {
            if (restoreTarget == null) return;

            // Safety check
            if (bagController != null)
            {
                bool inMain = (bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                               ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, restoreTarget));
                bool inAdditional = BagHelpers.GetAdditionalSeat(bagController, restoreTarget) != null;
                bool isTracked = BagHelpers.IsBaggedObject(bagController, restoreTarget);

                if (!force && (inMain || inAdditional || isTracked))
                {
                    Log.DebugIfEnabled("[PerformPassengerRestoration] Skipping restoration for {0} inMain={1} inAdditional={2} isTracked={3}",
                        restoreTarget.name, inMain, inAdditional, isTracked);
                    return;
                }
            }

            Log.DebugIfEnabled("[PerformPassengerRestoration] Restoring {0}", restoreTarget.name);
            Log.DebugIfEnabled("  Current Pos: {0}", restoreTarget.transform.position);
            Log.DebugIfEnabled("  Current Parent: {0}", (restoreTarget.transform.parent ? restoreTarget.transform.parent.name : "null"));

            // Manually unparent to ensure it's free from the seat/projectile structure
            if (restoreTarget.transform.parent != null)
            {
                Log.DebugIfEnabled("[PerformPassengerRestoration] Unparenting {0} from {1}", restoreTarget.name, restoreTarget.transform.parent.name);
                restoreTarget.transform.SetParent(null);
            }

            if (bagController != null)
            {
                API.DrifterBagAPI.RestoreColliders(bagController, restoreTarget);
                Log.DebugIfEnabled("[PerformPassengerRestoration] Restored movement colliders for {0}", restoreTarget.name);
            }

            // Restore HurtBoxes by resetting the deactivator counter
            var characterBody = restoreTarget.GetComponent<CharacterBody>();
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
                        Log.DebugIfEnabled("[PerformPassengerRestoration] Reset hurtBoxesDeactivatorCounter from {0} to 0 for {1}", oldCounter, restoreTarget.name);
                    }
                }
            }

            // Restore Rigidbody state only if it's not a character body
            var rb = restoreTarget.GetComponent<Rigidbody>();
            if (rb != null && characterBody == null)
            {
                var existingState = bagController != null ? API.DrifterBagAPI.LoadObjectState(bagController, restoreTarget) : null;
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

            var restoredData = bagController != null ? API.DrifterBagAPI.LoadObjectState(bagController, restoreTarget) : null;

            if (restoredData != null && characterBody != null)
            {
                restoredData.ApplyToCharacterBody(characterBody);
                restoredData.ResetBreakoutData();
                Log.DebugIfEnabled("[PerformPassengerRestoration] Applied stats to {0}", restoreTarget.name);
            }
        }

        // ========================================================================================
        // SKILL OVERRIDE CACHE & DATA
        // ========================================================================================

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
            var existingState = API.DrifterBagAPI.FindExistingBaggedObjectState(bagController, targetObject);
            if (existingState != null)
            {
                UnsetAllOverrides(existingState, bagController.gameObject);
            }
        }

        // ========================================================================================
        // BAGGED OBJECT ON ENTER
        // ========================================================================================

        [HarmonyPatch(typeof(BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter
        {

            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                // Guard against infinite re-entry during sync issues
                if (__instance == null)
                {
                    Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] __instance is null");
                    return false;
                }

                if (__instance.targetObject != null)
                {
                    var currentTime = Time.time;
                    if (PluginConfig.Instance.EnableCarouselHUD.Value &&
                        ReferenceEquals(__instance.targetObject, _lastProcessedObject) &&
                        (currentTime - _lastProcessTime) < 0.5f)
                    {
                        // Same object processed very recently - likely a re-entry loop
                        Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] blocking re-entry for {0} processed {1:F3}s ago",
                            __instance.targetObject.name, (currentTime - _lastProcessTime));
                        return false;
                    }
                }

                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] bagController is null, proceeding with vanilla OnEnter");
                    return true;
                }

                var targetObject = __instance?.targetObject;
                if (targetObject == null)
                {
                    // Recovery for clients: targetObject may be null due to network ordering
                    if (!NetworkServer.active && bagController != null)
                    {
                        GameObject? recovered = bagController.baggedObject;
                        if (recovered == null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger)
                        {
                            recovered = bagController.vehicleSeat.NetworkpassengerBodyObject;
                        }

                        if (recovered == null)
                        {
                            recovered = API.DrifterBagAPI.GetMainPassenger(bagController);
                        }

                        if (recovered != null)
                        {
                            __instance!.targetObject = recovered;
                            targetObject = recovered;
                            API.DrifterBagAPI.UpdateTargetFields(__instance);
                            Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] recovered targetObject from controller/seat: {0}", recovered.name);
                        }
                    }

                    if (targetObject == null)
                    {
                        Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] targetObject is null - likely deserialization failure or object destroyed");
                        NetworkUtils.LogObjectDetails(__instance?.outer?.gameObject, "BaggedObject_OnEnter.Prefix");
                        return false; // Block vanilla OnEnter - it will NRE with null targetObject
                    }
                }

                // Validate that the target object is ready for network operations
                if (!Networking.NetworkUtils.ValidateObjectReady(targetObject))
                {
                    Log.DebugIfEnabled($"[BaggedObject_OnEnter.Prefix] {targetObject.name} is not ready for network operations");
                    return false;
                }

                Networking.NetworkUtils.LogNetworkOperation("BaggedObject_OnEnter", targetObject!, NetworkServer.active, new Dictionary<string, object>
                {
                    { "bagController", bagController!.name },
                    { "isAuthority", bagController.hasAuthority }
                });

                // Check if targetObject is in additional seat
                var seatDict = API.DrifterBagAPI.GetAdditionalSeats(bagController);

                if (!DrifterBossGrabPlugin.IsSwappingPassengers && seatDict != null && targetObject != null && seatDict.TryGetValue(targetObject, out var additionalSeat))
                {
                    // Assign to additional seat instead of main
                    additionalSeat.AssignPassenger(targetObject);
                    return false;
                }

                // Client with capacity > 1: allow OnEnter to run for init
                if (!NetworkServer.active && bagController.hasAuthority)
                {
                    int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                    bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;

                    if (effectiveCapacity > 1 && !prioritize)
                    {
                        var list = API.DrifterBagAPI.GetBaggedObjects(bagController);
                        bool isAlreadyTracked = targetObject != null && list.Contains(targetObject);

                        if (!isAlreadyTracked)
                        {
                            // Check if bag is already full before allowing grab
                            int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                            if (currentCount >= effectiveCapacity)
                            {
                                Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] client blocking grab of {0} - bag full ({1}/{2})",
                                    targetObject!.name, currentCount, effectiveCapacity);
                                return false;
                            }

                            Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] client allowing vanilla OnEnter for new grab of {0} capacity={1} but flagging to block seat assignment",
                                targetObject!.name, effectiveCapacity);

                            // list.Add(targetObject); // Handled by BagCarouselUpdater or implicit list logic

                            list.Add(targetObject!);
                            BagHelpers.AddTracker(bagController, targetObject!);
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
                                Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] Client BLOCKING CYCLING of {0} - bag over capacity ({1}/{2})",
                                    targetObject!.name, currentCount, effectiveCapacity);
                                return false;
                            }

                            Log.DebugIfEnabled("[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for CYCLING of {0} (capacity={1})",
                                targetObject!.name, effectiveCapacity);
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
                var targetObject = __instance.targetObject;

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
                targetObject = __instance?.targetObject;

                if (targetObject == null) return;

                BaggedObject_OnExit.MarkObjectSuccessfullyInitialized(targetObject);

                // Restore breakout timer progress when entering main seat
                if (NetworkServer.active)
                {
                    var savedState = API.DrifterBagAPI.LoadObjectState(bagController, targetObject);
                    if (savedState != null)
                    {
                        if (ReflectionCache.EntityState.FixedAge != null && savedState.elapsedBreakoutTime > 0f)
                        {
                            ReflectionCache.EntityState.FixedAge.SetValue(__instance, savedState.elapsedBreakoutTime);
                            Log.DebugIfEnabled("[BaggedObject_OnEnter] Restored main seat breakout timer for {0} to {1:F2}s",
                                targetObject!.name, savedState.elapsedBreakoutTime);
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
                    bool isAlreadyTracked = API.DrifterBagAPI.IsObjectInBag(bagController, targetObject);
                    bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;

                    if (effectiveCapacity > 1 && !isAlreadyTracked && !prioritize)
                    {
                        Log.DebugIfEnabled("[BaggedObject_OnEnter.Postfix] client skipping main seat population for new grab of {0} capacity={1}",
                            targetObject!.name, effectiveCapacity);
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

                        // Safety: if we are currently syncing
                        if (netController != null && netController.IsSyncing)
                        {
                            shouldPopulateMainSeat = false;
                        }

                        if (shouldPopulateMainSeat && API.DrifterBagAPI.GetMainPassenger(bagController) == null && !isInAdditionalSeat)
                        {

                            API.DrifterBagAPI.SetMainSeatObject(bagController, targetObject);
                        }
                    }

                    // Also ensure it's in BaggedObjects list (always do this, regardless of main seat state)
                    if (!API.DrifterBagAPI.IsObjectInBag(bagController, targetObject))
                    {
                        API.DrifterBagAPI.AddBaggedObject(bagController, targetObject);
                        API.DrifterBagAPI.AddInstanceId(bagController, targetObject.GetInstanceID());
                        wasNewlyAddedToBag = true;
                    }
                }

                var outerMainSeat = bagController!.vehicleSeat;

                bool seatHasTarget = outerMainSeat != null && outerMainSeat.hasPassenger && ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject);
                var tracked = API.DrifterBagAPI.GetMainPassenger(bagController);
                bool trackedHasTarget = tracked != null && ReferenceEquals(tracked, targetObject);

                {
                    var netIdentity = targetObject.GetComponent<NetworkIdentity>();
                    string netIdStr = netIdentity != null ? netIdentity.netId.ToString() : "null";
                    Log.DebugIfEnabled("[BaggedObject_OnEnter.Postfix] {0} seatHasTarget={1} tracked={2} trackedHasTarget={3} isInAdditionalSeat={4} NetId {5}",
                        targetObject.name, seatHasTarget, (!tracked ? "null" : tracked!.name), trackedHasTarget, isInAdditionalSeat, netIdStr);
                }

                if (!bagController.hasAuthority && !seatHasTarget && !trackedHasTarget)
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
                var trackedObj = (bagController != null) ? API.DrifterBagAPI.GetMainPassenger(bagController) : null;
                bool isTrackedAsMain = trackedObj != null && ReferenceEquals(trackedObj, targetObject);

                if (isStashed && !isInMain && !isTrackedAsMain)
                {

                    if (__instance != null && __instance.outer != null) __instance.outer.SetNextStateToMain();
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
                            if (__instance != null) API.DrifterBagAPI.UpdateBagScale(__instance, baggedMass);
                            else
                            {
                                Log.DebugIfEnabled($"[BaggedObject_OnEnter.Postfix] __instance is null, cannot update bag scale");
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

        // ========================================================================================
        // BAGGED OBJECT ON EXIT
        // ========================================================================================

        [HarmonyPatch(typeof(BaggedObject), "OnExit")]
        public class BaggedObject_OnExit
        {
            internal static readonly HashSet<GameObject> _suppressedExitObjects = new HashSet<GameObject>();
            internal static readonly HashSet<GameObject> _preserveOverridesDuringCycling = new HashSet<GameObject>();
            internal static readonly HashSet<GameObject> _successfullyInitializedObjects = new HashSet<GameObject>();

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
                    Log.DebugIfEnabled("[BaggedObject_OnExit.Prefix] __instance is null");
                    return true;
                }

                var bagController = __instance.outer?.GetComponent<DrifterBagController>();

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var currentMain = bagController != null ? API.DrifterBagAPI.GetMainPassenger(bagController) : null;
                    var bagStateMachine = EntityStateMachine.FindByCustomName(__instance.outer?.gameObject, "Bag");
                    var currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                    var currentTarget = bagStateMachine?.state is BaggedObject bagged ? bagged.targetObject : null;

                    Log.DebugIfEnabled("[BaggedObject_OnExit.Prefix] InstanceTarget={0}, StateTarget={1}, State={2}, MainPassenger={3}",
                        BagHelpers.GetSafeName(__instance.targetObject), BagHelpers.GetSafeName(currentTarget), currentStateName, BagHelpers.GetSafeName(currentMain));
                }

                // Check if we should keep the overrides
                if (bagController == null)
                {
                    Log.DebugIfEnabled("[BaggedObject_OnExit.Prefix] bagController is null, proceeding with vanilla OnExit");
                    return true;
                }

                // Validate target object
                if (__instance.targetObject == null)
                {
                    Log.DebugIfEnabled("[BaggedObject_OnExit.Prefix] targetObject is null - likely deserialization failure or object destroyed");
                    NetworkUtils.LogObjectDetails(__instance.outer?.gameObject, "BaggedObject_OnExit.Prefix");
                }
                else
                {
                    // Validate that the target object is ready
                    if (!NetworkUtils.ValidateObjectReady(__instance.targetObject))
                    {
                        Log.DebugIfEnabled($"[BaggedObject_OnExit.Prefix] {__instance.targetObject.name} is not ready for network operations");
                    }

                    NetworkUtils.LogNetworkOperation("BaggedObject_OnExit", __instance.targetObject, NetworkServer.active, new Dictionary<string, object>
                    {
                        { "bagController", bagController.name },
                        { "isAuthority", bagController.hasAuthority }
                    });
                }

                bool isSuppressed = false;
                GameObject? suppressedObject = __instance.targetObject;
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
                    Log.DebugIfEnabled(" [BaggedObject_OnExit] Suppressed OnExit");
                    return false;
                }

                if (!__instance.targetObject)
                {
                    Log.DebugIfEnabled(" [BaggedObject_OnExit] targetObject is null/destroyed");

                    // Manually trigger junk spawning since we're skipping vanilla OnExit
                    TrySpawnJunkForSkippedOnExit(__instance, "null/destroyed targetObject");
                    RemoveWalkSpeedPenalty(__instance);

                    // Forcefully clear UI
                    var uiOverlayController = (OverlayController)ReflectionCache.BaggedObject.UIOverlayController.GetValue(__instance);
                    if (uiOverlayController != null)
                    {
                        HudOverlayManager.RemoveOverlay(uiOverlayController);
                        ReflectionCache.BaggedObject.UIOverlayController.SetValue(__instance, null);
                    }
                    return false;
                }

                bool isDead = false;
                if (__instance.targetObject != null)
                {
                    var isInAdditionalSeat = (bagController != null) && BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null;
                    var isInMainSeat = (bagController != null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger && ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, __instance.targetObject));

                    if (!isInAdditionalSeat && !isInMainSeat)
                    {
                        PerformPassengerRestoration(bagController, __instance.targetObject);
                    }
                    isDead = __instance.targetObject.TryGetComponent<HealthComponent>(out var hc) && !hc.alive;
                }

                if (isDead)
                {
                    Log.DebugIfEnabled(" [BaggedObject_OnExit] targetObject is dead/dying {0}", BagHelpers.GetSafeName(__instance.targetObject));
                    // Also need to spawn junk for dead bodies since we're skipping vanilla OnExit
                    TrySpawnJunkForSkippedOnExit(__instance, $"dead/dying {BagHelpers.GetSafeName(__instance.targetObject)}");
                    RemoveWalkSpeedPenalty(__instance);

                    // Forcefully clear UI
                    var uiOverlayController = (OverlayController)ReflectionCache.BaggedObject.UIOverlayController.GetValue(__instance);
                    if (uiOverlayController != null)
                    {
                        HudOverlayManager.RemoveOverlay(uiOverlayController);
                        ReflectionCache.BaggedObject.UIOverlayController.SetValue(__instance, null);
                    }
                    return false;
                }

                if (bagController == null) return true;

                var mainSeatObj = API.DrifterBagAPI.GetMainPassenger(bagController);
                bool isStillMain = ReferenceEquals(__instance.targetObject, mainSeatObj);
                bool isActuallyInBag = BagHelpers.IsBaggedObject(bagController, __instance.targetObject);

                // Skip vanilla OnExit (HUD removal) and mod cleanup
                if (PluginConfig.Instance.EnableCarouselHUD.Value && (DrifterBossGrabPlugin.IsSwappingPassengers || (isStillMain && isActuallyInBag)))
                {
                    Log.DebugIfEnabled("[BaggedObject_OnExit] Skipping vanilla cleanup for {0} Carousel mode",
                        BagHelpers.GetSafeName(__instance.targetObject));

                    RemoveWalkSpeedPenalty(__instance);

                    return false;
                }

                if (!isStillMain && !isActuallyInBag)
                {
                    Log.DebugIfEnabled("[BaggedObject_OnExit] Not swapping/jittering, manually cleaning up overrides for {0}",
                        BagHelpers.GetSafeName(__instance.targetObject));
                    UnsetAllOverrides(__instance, bagController.gameObject);
                }

                return true;
            }
        }

        internal static void UnsetAllOverrides(BaggedObject? instance, object source)
        {
            if (source == null) return;
            try
            {
                var body = (source as GameObject)?.GetComponent<CharacterBody>()
                           ?? (instance?.outer?.GetComponent<CharacterBody>());

                if (body == null && source is GameObject go) body = go.GetComponent<CharacterBody>();

                bool isSourceBagController = source is GameObject sourceGo && sourceGo.GetComponent<DrifterBagController>() != null;

                Log.DebugIfEnabled("[UnsetAllOverrides] Authoritative cleanup for {0} Instance provided {1} Source isBagController {2}",
                    BagHelpers.GetSafeName(body), (instance != null), isSourceBagController);

                // 1. If we have a BaggedObject instance, clear its internal tracking fields
                if (instance != null)
                {
                    if (ReflectionCache.BaggedObject.OverriddenUtility != null && ReflectionCache.BaggedObject.UtilityOverride != null)
                    {
                        var overriddenUtility = (GenericSkill)ReflectionCache.BaggedObject.OverriddenUtility.GetValue(instance);
                        var utilityOverride = (SkillDef)ReflectionCache.BaggedObject.UtilityOverride.GetValue(instance);
                        if (overriddenUtility != null && utilityOverride != null)
                        {
                            Log.DebugIfEnabled("[UnsetAllOverrides] Unsetting instance Utility override: {0} from {1}", ((ScriptableObject)utilityOverride).name, overriddenUtility.name);
                            overriddenUtility.UnsetSkillOverride(instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                            ReflectionCache.BaggedObject.OverriddenUtility.SetValue(instance, null);
                        }
                    }

                    if (ReflectionCache.BaggedObject.OverriddenPrimary != null && ReflectionCache.BaggedObject.PrimaryOverride != null)
                    {
                        var overriddenPrimary = (GenericSkill)ReflectionCache.BaggedObject.OverriddenPrimary.GetValue(instance);
                        var primaryOverride = (SkillDef)ReflectionCache.BaggedObject.PrimaryOverride.GetValue(instance);
                        if (overriddenPrimary != null && primaryOverride != null)
                        {
                            Log.DebugIfEnabled("[UnsetAllOverrides] Unsetting instance Primary override: {0} from {1}", ((ScriptableObject)primaryOverride).name, overriddenPrimary.name);
                            overriddenPrimary.UnsetSkillOverride(instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                            ReflectionCache.BaggedObject.OverriddenPrimary.SetValue(instance, null);
                        }
                    }
                }

                // 2. ALWAYS attempt to clear the locator using the stable source (the BagController)
                // This is the most reliable cleanup as the BagController persists during throws.
                var skillLocator = body?.skillLocator;
                if (skillLocator != null)
                {
                    // If the source is a BaggedObject instance that is about to be destroyed, 
                    // we also pass the BagController as a secondary source.
                    var bagController = (source as GameObject)?.GetComponent<DrifterBagController>() ?? (instance?.outer?.GetComponent<DrifterBagController>());

                    if (skillLocator.primary) CleanupSkillFromLocator(source, skillLocator.primary, bagController);
                    if (skillLocator.secondary) CleanupSkillFromLocator(source, skillLocator.secondary, bagController);
                    if (skillLocator.utility) CleanupSkillFromLocator(source, skillLocator.utility, bagController);
                    if (skillLocator.special) CleanupSkillFromLocator(source, skillLocator.special, bagController);
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
                    Log.DebugIfEnabled(" [TrySpawnJunk] Reflection failed: {0}", ex.Message);
                }

                // Method 2: Fallback to GetComponent via outer
                if (drifterBagController == null && instance != null && instance.outer != null && instance.outer.gameObject != null)
                {
                    drifterBagController = instance.outer.gameObject.GetComponent<DrifterBagController>();
                    Log.DebugIfEnabled(" [TrySpawnJunk] Traverse returned null, GetComponent returned: {0}", (!drifterBagController ? "null" : drifterBagController!.name));
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string bName = BagHelpers.GetSafeName(drifterBagController);
                    string bbName = drifterBagController != null ? BagHelpers.GetSafeName(drifterBagController.baggedBody) : "NULL";
                    string attrName = drifterBagController != null ? BagHelpers.GetSafeName(drifterBagController.baggedAttributes) : "NULL";
                    Log.DebugIfEnabled("[TrySpawnJunk] Reason: {0} | bagController: {1} | Server: {2} | baggedBody: {3} | attributes: {4}", reason, bName, NetworkServer.active, bbName, attrName);
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
                            lock (BaggedObject_OnExit._successfullyInitializedObjects)
                            {
                                wasSuccessfullyInitialized = BaggedObject_OnExit._successfullyInitializedObjects.Contains(stateMachine.gameObject);
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
                        Log.DebugIfEnabled("[TrySpawnJunk] targetObject is null/destroyed");

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
                        Log.DebugIfEnabled("[TrySpawnJunk] skipped junk spawn isSwapping={0} hasValidBaggedObjectState={1}", DrifterBossGrabPlugin.IsSwappingPassengers, hasValidBaggedObjectState);
                    }
                    else if (targetIsDestroyedOrNull && !wasSuccessfullyInitialized)
                    {
                        Log.DebugIfEnabled("[TrySpawnJunk] skipped junk spawn null target detected during grab operation");
                    }
                    else
                    {
                        if (drifterBagController.baggedBody != null && instance != null && drifterBagController.baggedBody != instance.targetObject)
                        {
                            Log.DebugIfEnabled("[TrySpawnJunk] baggedBody changed! Manually spawning junk for {0} to protect new passenger {1}", BagHelpers.GetSafeName(instance?.targetObject), BagHelpers.GetSafeName(drifterBagController.baggedBody));

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
                            Log.DebugIfEnabled("[TrySpawnJunk] Calling ExecuteBody for {0}", BagHelpers.GetSafeName(instance?.targetObject));
                            drifterBagController!.ExecuteBody();
                            drifterBagController.ResetBaggedObject();
                        }
                    }
                }
                Log.DebugIfEnabled("[TrySpawnJunk] >>> SKIPPED ExecuteBody - controller null: {0}, server: {1}", drifterBagController == null, NetworkServer.active);
            }
            catch (Exception ex)
            {
                Log.Error($" [TrySpawnJunk] Error: {ex.Message}");
            }
        }

        private static void CleanupSkillFromLocator(object source, GenericSkill skill, object? secondarySource = null)
        {
            if (!skill || source == null) return;
            try
            {
                if (ReflectionCache.GenericSkill.SkillOverrides == null || _skillOverrideSourceField == null) return;
                var overridesList = (System.Collections.IList)ReflectionCache.GenericSkill.SkillOverrides.GetValue(skill);
                if (overridesList == null || overridesList.Count == 0) return;

                // Iterate backwards to safely remove
                for (int i = overridesList.Count - 1; i >= 0; i--)
                {
                    var skillOverride = overridesList[i];
                    var overrideSource = _skillOverrideSourceField?.GetValue(skillOverride);
                    var skillDef = _skillOverrideSkillDefField?.GetValue(skillOverride) as SkillDef;
                    var priority = (GenericSkill.SkillOverridePriority)(_skillOverridePriorityField?.GetValue(skillOverride) ?? GenericSkill.SkillOverridePriority.Contextual);

                    bool shouldRemove = false;
                    string reason = "";

                    // 1. Direct source match
                    if (ReferenceEquals(overrideSource, source))
                    {
                        shouldRemove = true;
                        reason = "Direct source match";
                    }
                    // 2. Secondary source match
                    else if (secondarySource != null && ReferenceEquals(overrideSource, secondarySource))
                    {
                        shouldRemove = true;
                        reason = "Secondary source match";
                    }
                    // 3. Fallback
                    else if (skillDef != null)
                    {
                        string name = ((ScriptableObject)skillDef).name;
                        string token = skillDef.skillNameToken;
                        var stateType = skillDef.activationState.stateType;

                        if (name == "EmptyBag" || name == "SuffocateSlam" ||
                            token == "DRIFTER_SKILL_EMPTYBAG_NAME" || token == "DRIFTER_SKILL_SUFFOCATESLAM_NAME" ||
                            stateType == typeof(EntityStates.Drifter.EmptyBag) ||
                            stateType == typeof(EntityStates.Drifter.SuffocateSlam))
                        {
                            shouldRemove = true;
                            reason = $"Fallback match: Name={name}, Token={token}, Type={stateType?.Name}";
                        }
                    }

                    if (shouldRemove && skillDef != null)
                    {
                        Log.DebugIfEnabled("[CleanupSkillFromLocator] Removing override {0} from {1} Priority {2} Reason {3}",
                            ((ScriptableObject)skillDef).name, skill.name, priority, reason);

                        skill.UnsetSkillOverride(overrideSource, skillDef, priority);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[CleanupSkillFromLocator] Failed to cleanup skill overrides for {skill.name}: {ex.Message}");
            }
        }
        [HarmonyPostfix]
        public static void Postfix(BaggedObject __instance)
        {
            var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
            if (bagController == null || __instance?.targetObject == null) return;

            // Check if this object was the main seat occupant and is not in an additional seat
            var tracked = API.DrifterBagAPI.GetMainPassenger(bagController);
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
                Log.Error($"[BaggedObject_OnExit.Postfix] Failed to check HoldsDeadBody: {ex.Message}");
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

        // ========================================================================================
        // BAGGED OBJECT FIXED UPDATE
        // ========================================================================================

        [HarmonyPatch(typeof(BaggedObject), "FixedUpdate")]
        public class BaggedObject_FixedUpdate
        {
            // Throttle debug logging to avoid spamming every FixedUpdate frame
            private static float _lastFixedUpdateLogTime;
            private static string _lastFixedUpdateBlockReason = "";
            private static int _recoveryRetryCount = 0;
            private const int MAX_RECOVERY_RETRIES = 120; // ~2 seconds of FixedUpdates at 60Hz

            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                try
                {
                    bool shouldLog = PluginConfig.Instance.EnableDebugLogs.Value && (Time.time - _lastFixedUpdateLogTime > 2f);

                    if (__instance == null) return true;

                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        var mainSeatObj = API.DrifterBagAPI.GetMainPassenger(bagController);
                        bool isMain = mainSeatObj != null && __instance.targetObject != null && ReferenceEquals(mainSeatObj, __instance.targetObject);

                        if (!isMain && (!DrifterBossGrabPlugin.IsSwappingPassengers || mainSeatObj == null))
                        {
                            UnsetAllOverrides(__instance, bagController.gameObject);

                            if (mainSeatObj == null)
                            {
                                __instance.outer?.SetNextStateToMain();
                                return false;
                            }
                        }
                    }

                    if (__instance.targetObject == null)
                    {
                        // Attempt recovery on client
                        if (!NetworkServer.active && __instance != null)
                        {
                            var bagCtrl = __instance.outer?.GetComponent<DrifterBagController>();
                            if (bagCtrl != null)
                            {
                                GameObject? recovered = bagCtrl.baggedObject
                                    ?? bagCtrl.vehicleSeat?.NetworkpassengerBodyObject
                                    ?? API.DrifterBagAPI.GetMainPassenger(bagCtrl);

                                if (recovered != null)
                                {
                                    __instance.targetObject = recovered;
                                    API.DrifterBagAPI.UpdateTargetFields(__instance);
                                    _recoveryRetryCount = 0;
                                    Log.DebugIfEnabled("[BaggedObject_FixedUpdate] recovered targetObject: {0}", recovered.name);
                                    return true;
                                }
                            }

                            _recoveryRetryCount++;
                            if (_recoveryRetryCount > MAX_RECOVERY_RETRIES)
                            {
                                Log.DebugIfEnabled("[BaggedObject_FixedUpdate] Recovery failed. Forcing exit to Main.");
                                __instance.outer?.SetNextStateToMain();
                                _recoveryRetryCount = 0;
                                return false;
                            }
                        }

                        if (shouldLog && _lastFixedUpdateBlockReason != "null_instance")
                        {
                            _lastFixedUpdateBlockReason = "null_instance";
                            _lastFixedUpdateLogTime = Time.time;
                            Log.DebugIfEnabled("[BaggedObject_FixedUpdate] blocked: instance or targetObject is null retries={0}", _recoveryRetryCount);
                        }
                        return false;
                    }
                    _recoveryRetryCount = 0;

                    // 1. Check isBody flag
                    var isBodyVal = ReflectionCache.BaggedObject.IsBody?.GetValue(__instance);
                    if (isBodyVal is bool isBody && !isBody)
                    {
                        if (shouldLog && _lastFixedUpdateBlockReason != "isBody_false")
                        {
                            _lastFixedUpdateBlockReason = "isBody_false";
                            _lastFixedUpdateLogTime = Time.time;
                            Log.DebugIfEnabled("[BaggedObject_FixedUpdate] blocked: isBody=false for {0}", __instance.targetObject.name);
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
                            Log.DebugIfEnabled("[BaggedObject_FixedUpdate] blocked: targetBody is null for {0}", __instance.targetObject.name);
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
                            Log.DebugIfEnabled("[BaggedObject_FixedUpdate] blocked: drifterBagController is null for {0}", __instance.targetObject.name);
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
                                Log.DebugIfEnabled("[BaggedObject_FixedUpdate] blocked: target is dead for {0}", __instance.targetObject.name);
                            }
                            return false;
                        }
                    }
                    catch { return false; }

                    // 4. Additional Seat Check
                    if (bagController != null)
                    {
                        if (!UnityEngine.Networking.NetworkServer.active)
                        {
                            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                            if (netController != null && netController.selectedIndex == -1)
                            {
                                return false;
                            }
                        }

                        bool isInAdditionalSeat = BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null;

                        // Fallback: If dictionary is empty, check synced passenger IDs list
                        if (!isInAdditionalSeat)
                        {
                            var networkController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                            if (networkController != null && __instance.targetObject.TryGetComponent<NetworkIdentity>(out var ni))
                            {
                                var passengerIds = networkController.BaggedObjectNetIds;
                                int selIndex = networkController.selectedIndex;

                                for (int i = 0; i < passengerIds.Count; i++)
                                {
                                    if (passengerIds[i] == ni.netId.Value)
                                    {
                                        // If it's NOT the currently active main object (Index 0), it's in an additional seat
                                        if (i != 0 || selIndex != 0)
                                        {
                                            isInAdditionalSeat = true;
                                        }
                                        break;
                                    }
                                }
                            }
                        }

                        if (isInAdditionalSeat)
                        {
                            if (shouldLog && _lastFixedUpdateBlockReason != "additional_seat")
                            {
                                _lastFixedUpdateBlockReason = "additional_seat";
                                _lastFixedUpdateLogTime = Time.time;
                                Log.DebugIfEnabled("[BaggedObject_FixedUpdate] blocked: target is in additional seat for {0}", __instance.targetObject.name);
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
                        Log.DebugIfEnabled("[BaggedObject_FixedUpdate] allowed for {0} fixedAge={1:F2} breakoutTime={2:F2} attempts={3}", __instance.targetObject.name, currentAge, bTime, bAttempts);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    // Fail safe: If our checks crash, default to skipping vanilla update to be safe
                    Log.DebugIfEnabled("[BaggedObject_FixedUpdate] Error in prefix: {0}", ex);
                    return false;
                }
            }
        }

        // ========================================================================================
        // BAGGED OBJECT MASS UPDATE
        // ========================================================================================

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
                Log.DebugIfEnabled("[BaggedObject_UpdateBaggedObjectMass] Suppressing vanilla penalty update for {0}", (!__instance.targetObject ? "null" : __instance.targetObject!.name));
                return false;
            }
        }

        // ========================================================================================
        // ENTITY STATE MACHINE PATCHES
        // ========================================================================================

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
                        var passenger = API.DrifterBagAPI.GetMainPassenger(bagController);
                        if (passenger == null)
                        {
                            passenger = bagController.baggedObject;

                            if (passenger == null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger)
                            {
                                passenger = bagController.vehicleSeat.NetworkpassengerBodyObject;
                            }
                        }

                        if (passenger != null)
                        {
                            bool isTracked = false;
                            var mainSeatObject = API.DrifterBagAPI.GetMainPassenger(bagController);
                            if (mainSeatObject != null && ReferenceEquals(mainSeatObject, passenger))
                            {
                                isTracked = true;
                            }
                            else if (API.DrifterBagAPI.IsObjectInBag(bagController, passenger))
                            {
                                isTracked = true;
                            }
                            bool isDead = API.DrifterBagAPI.IsPassengerDeadOrDestroyed(passenger);
                            bool isSuppressed = API.DrifterBagAPI.IsObjectExitSuppressed(passenger);

                            Log.DebugIfEnabled("[SetNextStateToMain] {0} isDead={1} isTracked={2} isSuppressed={3}", passenger.name, isDead, isTracked, isSuppressed);

                            if (!isDead && isSuppressed)
                            {
                                Log.DebugIfEnabled("[SetNextStateToMain] blocking transition for {0} isSuppressed={1}", passenger.name, isSuppressed);
                                return false;
                            }
                        }
                    }
                }
                return true;
            }

        }

        // Registry of ESMs that belong to bagged objects to avoid expensive GetComponent checks in SetState.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityStateMachine, BaggedObjectTracker> _trackedESMs
            = new System.Runtime.CompilerServices.ConditionalWeakTable<EntityStateMachine, BaggedObjectTracker>();

        public static void RegisterTrackedESM(EntityStateMachine esm, BaggedObjectTracker tracker)
        {
            if (esm == null || tracker == null) return;
            _trackedESMs.Remove(esm);
            _trackedESMs.Add(esm, tracker);
        }

        public static void UnregisterTrackedESM(EntityStateMachine esm)
        {
            if (esm == null) return;
            _trackedESMs.Remove(esm);
        }

        // ========================================================================================
        // ENTITY STATE MACHINE SET STATE
        // ========================================================================================

        [HarmonyPatch(typeof(RoR2.EntityStateMachine), "SetState")]
        public class EntityStateMachine_SetState
        {
            [HarmonyPrefix]
            public static void Prefix(RoR2.EntityStateMachine __instance, EntityState newState)
            {
                if (__instance == null || newState == null) return;

                // O(1) lookup in our registry
                if (!_trackedESMs.TryGetValue(__instance, out var tracker)) return;

                // Guard against manual removals or passenger swaps
                if (tracker == null || tracker.isRemovingManual || DrifterBossGrabPlugin.IsSwappingPassengers) return;

                var controller = tracker.controller;
                if (controller == null) return;

                var obj = __instance.gameObject;
                if (obj == null) return;

                string newStateName = newState.GetType().Name;
                string currentStateName = __instance.state?.GetType()?.Name ?? "null";

                // Skip safe transitions:
                // - VehicleSeated: object is being seated
                // - SpawnState variants: object is still spawning in
                if (newState is EntityStates.GenericCharacterVehicleSeated) return;
                if (newStateName.Contains("SpawnState")) return;

                // Safe falling back to Idle/Uninitialized or intentional Main/Stun transitions
                var newStateType = newState.GetType();
                var mainStateType = __instance.mainStateType.stateType;
                bool isMainState = (newStateType != null && mainStateType != null && newStateType == mainStateType) || newStateName == "GenericCharacterMain";

                bool isIdleOrInit = newStateName.Contains("Idle") || newStateName.Contains("Uninitialized");
                bool isMainSafe = isMainState && currentStateName.Contains("VehicleSeated");
                bool isStunSafe = newStateName.Contains("StunState") && currentStateName.Contains("VehicleSeated");

                if (isIdleOrInit || isMainSafe || isStunSafe) return;

                Log.DebugIfEnabled("[EntityStateMachine_SetState] Bagged object {0} ESM {1} transitioning {2} to {3} cleaning up bag tracking", obj.name, __instance.customName, currentStateName, newStateName);

                // Clean up
                try
                {
                    PerformPassengerRestoration(controller, obj, force: true);
                    BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying: false);
                    BagCarouselUpdater.UpdateCarousel(controller);
                }
                catch (Exception ex)
                {
                    Log.DebugIfEnabled("[EntityStateMachine_SetState] Error during escape cleanup: {0}", ex.Message);
                }
            }
        }

        // ========================================================================================
        // EMPTY BAG PATCHES
        // ========================================================================================

        [HarmonyPatch(typeof(EntityStates.Drifter.EmptyBag), "OnEnter")]
        public class EmptyBag_OnEnter_Patch
        {
            public static void Postfix(EntityStates.Drifter.EmptyBag __instance)
            {
                if (__instance == null || !__instance.outer) return;
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    Log.DebugIfEnabled("[EmptyBag_OnEnter] Force cleaning overrides for Drifter.");

                    UnsetAllOverrides(null, bagController.gameObject);
                }
            }
        }
    }
}
