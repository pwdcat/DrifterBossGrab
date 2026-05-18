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

    // ========================================================================================
    // BAGGED OBJECT STATE PATCHES
    // ========================================================================================
    public static class BaggedObjectStatePatches
    {
        public static void PerformPassengerRestoration(DrifterBagController? bagController, UnityEngine.GameObject? restoreTarget)
        {
            if (restoreTarget == null) return;

            if (restoreTarget.transform.parent != null)
            {
                Log.Debug($"[PerformPassengerRestoration] Unparenting {restoreTarget.name} from {restoreTarget.transform.parent.name}");
                restoreTarget.transform.SetParent(null, true);
            }

            Log.Debug($"[PerformPassengerRestoration] Restoring {restoreTarget.name}");
            Log.Debug($"  Current Pos: {restoreTarget.transform.position}");
            Log.Debug($"  Current Parent: {(restoreTarget.transform.parent != null ? restoreTarget.transform.parent.name : "null")}");

            var bagState = (bagController != null) ? BagPatches.GetState(bagController) : null;
            if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(restoreTarget, out var states))
            {
                BodyColliderCache.RestoreMovementColliders(states);
                bagState.DisabledCollidersByObject.Remove(restoreTarget, out _);
                Log.Debug($"[PerformPassengerRestoration] Restored movement colliders for {restoreTarget.name}");
            }

            var characterBody = restoreTarget.GetComponent<CharacterBody>();
            var restoredData = bagController != null ? BaggedObjectPatches.LoadObjectState(bagController, restoreTarget) : null;

            if (restoredData != null && characterBody != null)
            {
                restoredData.ApplyToCharacterBody(characterBody);

                restoredData.ResetBreakoutData();
                Log.Debug($"[PerformPassengerRestoration] Applied stats to {restoreTarget.name}");
            }

            if (restoredData != null)
            {
                restoredData.RestorePhysicsAndHurtboxes(restoreTarget);
            }

        }

        // ========================================================================================
        // SKILL OVERRIDE CACHE & DATA
        // ========================================================================================
        private static readonly FieldInfo _skillOverrideSourceField = typeof(GenericSkill.SkillOverride).GetField("source", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo _skillOverrideSkillDefField = typeof(GenericSkill.SkillOverride).GetField("skillDef", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo _skillOverridePriorityField = typeof(GenericSkill.SkillOverride).GetField("priority", BindingFlags.Public | BindingFlags.Instance);

        private static GameObject? _lastProcessedObject;
        private static float _lastProcessTime;

        public static void ForceCleanupOverrides(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return;
            var existingState = BaggedObjectPatches.FindExistingBaggedObjectState(bagController, targetObject);
            if (existingState != null)
            {
                UnsetAllOverrides(existingState);
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

                    if (!NetworkServer.active && bagController != null)
                    {
                        GameObject? recovered = bagController.baggedObject;
                        if (recovered == null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger)
                        {
                            recovered = bagController.vehicleSeat.NetworkpassengerBodyObject;
                        }

                        if (recovered == null)
                        {
                            recovered = BagPatches.GetMainSeatObject(bagController);
                        }

                        if (recovered != null)
                        {
                            __instance!.targetObject = recovered;
                            targetObject = recovered;
                            BaggedObjectPatches.UpdateTargetFields(__instance);
                            Log.Debug($"[BaggedObject_OnEnter.Prefix] RECOVERED targetObject from controller/seat: {recovered.name}");
                        }
                    }

                    if (targetObject == null)
                    {
                        Log.Warning("[BaggedObject_OnEnter.Prefix] targetObject is null - likely deserialization failure or object destroyed");
                        NetworkUtils.LogObjectDetails(__instance?.outer?.gameObject, "BaggedObject_OnEnter.Prefix");
                        return false;
                    }
                }

                if (!Networking.NetworkUtils.ValidateObjectReadyWithRecovery(targetObject))
                {
                    Log.Warning($"[BaggedObject_OnEnter.Prefix] {targetObject.name} is not ready for network operations (recovery attempted)");
                    return false;
                }

                if (ProjectileRecoveryPatches.IsUndergoingThrowOperation(targetObject!))
                {
                    Log.Warning($"[BaggedObject_OnEnter.Prefix] Blocking grab of {targetObject!.name} - object is currently undergoing throw operation");
                    return false;
                }

                Networking.NetworkUtils.LogNetworkOperation("BaggedObject_OnEnter", targetObject!, NetworkServer.active, new Dictionary<string, object>
                {
                    { "bagController", bagController!.name },
                    { "isAuthority", bagController.hasAuthority }
                });

                var seatDict = BagPatches.GetState(bagController).AdditionalSeats;
                if (seatDict != null && seatDict.TryGetValue(targetObject, out var additionalSeat))
                {

                    additionalSeat.AssignPassenger(targetObject);

                    if (Networking.NetworkUtils.IsNetworkIdentityInactive(targetObject))
                    {
                        Networking.NetworkUtils.TryEnsureNetworkIdentityActive(targetObject);
                    }

                    __instance?.outer?.SetNextStateToMain();

                    return false;
                }

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

                            int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                            if (currentCount >= effectiveCapacity)
                            {
                                Log.Debug($"[BaggedObject_OnEnter.Prefix] Client BLOCKING grab of {targetObject.name} - bag full ({currentCount}/{effectiveCapacity})");
                                return false;
                            }

                            Log.Debug($"[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for NEW GRAB of {targetObject!.name} (capacity={effectiveCapacity}) but FLAGGING to block seat assignment");

                            list.Add(targetObject);
                            BagHelpers.AddTracker(bagController, targetObject);
                            BagCarouselUpdater.UpdateCarousel(bagController);
                            BagCarouselUpdater.UpdateNetworkBagState(bagController);
                            BagPassengerManager.ForceRecalculateMass(bagController);
                        }
                        else
                        {

                            int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                            if (currentCount > effectiveCapacity)
                            {
                                Log.Debug($"[BaggedObject_OnEnter.Prefix] Client BLOCKING CYCLING of {targetObject!.name} - bag over capacity ({currentCount}/{effectiveCapacity})");
                                return false;
                            }

                            Log.Debug($"[BaggedObject_OnEnter.Prefix] Client allowing vanilla OnEnter for CYCLING of {targetObject!.name} (capacity={effectiveCapacity})");
                        }

                        return true;
                    }
                }

                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                if (__instance == null) return;
                var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                var targetObject = __instance.targetObject;

                if (targetObject != null)
                {
                    var fuse = targetObject.GetComponent<RoR2.Projectile.ProjectileFuse>();
                    if (fuse != null) fuse.enabled = false;
                }

                if (targetObject != null && bagController != null)
                {
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (storedState != null)
                    {
                        storedState.ApplyToBaggedObject(__instance);

                        if (ReflectionCache.DrifterBagController.Smacks != null)
                        {
                            int passengerSmacks = storedState.smacks;
                            ReflectionCache.DrifterBagController.Smacks.SetValue(bagController, passengerSmacks);
                        }

                        Log.Debug($"[BaggedObject_OnEnter.Postfix] Restored breakout state for {targetObject.name}: age={storedState.elapsedBreakoutTime:F2}s, smacks={storedState.smacks}");
                    }

                    BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                }

                BagPassengerManager.SuppressVanillaWalkSpeedModifier(__instance!);

                if (targetObject != null)
                {
                    _lastProcessedObject = targetObject;
                    _lastProcessTime = Time.time;
                }
                else
                {

                    _lastProcessedObject = null;
                    _lastProcessTime = Time.time;
                }

                if (bagController == null || targetObject == null) return;

                BaggedObject_OnExit.MarkObjectSuccessfullyInitialized(targetObject);

                bool isInAdditionalSeat = BagHelpers.GetAdditionalSeat(bagController, targetObject) != null;
                bool wasNewlyAddedToBag = false;

                if (bagController.hasAuthority && !NetworkServer.active)
                {

                    int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                    bool isAlreadyTracked = BagPatches.GetState(bagController).BaggedObjects.Contains(targetObject);
                    bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;

                    if (effectiveCapacity > 1 && !isAlreadyTracked && !prioritize)
                    {
                        Log.Debug($"[BaggedObject_OnEnter.Postfix] Client skipping main seat population for NEW GRAB of {targetObject!.name} (capacity={effectiveCapacity})");

                    }
                    else
                    {

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

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var netIdentity = targetObject.GetComponent<NetworkIdentity>();
                    string netIdStr = netIdentity != null ? netIdentity.netId.ToString() : "null";
                    Log.Debug($"[BaggedObject_OnEnter.Postfix] {targetObject.name}: " +
                            $"seatHasTarget={seatHasTarget}, " +
                            $"tracked={(!tracked ? "null" : tracked!.name)}, " +
                            $"trackedHasTarget={trackedHasTarget}, " +
                            $"isInAdditionalSeat={isInAdditionalSeat}. " +
                            $"NetId: {netIdStr}.");
                }

                if (bagController.hasAuthority)
                {

                }
                else if (!seatHasTarget && !trackedHasTarget)
                {

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

                if (bagController != null && targetObject != null)
                {
                    BagCarouselUpdater.UpdateCarousel(bagController);

                    if (wasNewlyAddedToBag && bagController.hasAuthority)
                    {
                        BagCarouselUpdater.UpdateNetworkBagState(bagController);
                    }
                }
                else
                {

                    if (bagController != null && targetObject != null && !isInAdditionalSeat)
                    {
                        BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    }
                }

                if (PluginConfig.Instance.EnableCarouselHUD.Value)
                {
                    var uiOverlayController2 = ReflectionCache.BaggedObject.UIOverlayController?.GetValue(__instance) as OverlayController;
                    if (uiOverlayController2 != null)
                    {
                        HudOverlayManager.RemoveOverlay(uiOverlayController2);
                        ReflectionCache.BaggedObject.UIOverlayController?.SetValue(__instance, null);
                    }
                }

                if (!isInAdditionalSeat)
                {
                    BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                }

                bool isStashed = isInAdditionalSeat;
                bool isInMain = (bagController != null && bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger && ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject));

                var trackedObj = (bagController != null) ? BagPatches.GetMainSeatObject(bagController) : null;
                bool isTrackedAsMain = trackedObj != null && ReferenceEquals(trackedObj, targetObject);

                if (isStashed && !isInMain && !isTrackedAsMain)
                {

                    if (__instance != null && __instance.outer != null) __instance.outer.SetNextStateToMain();
                }
                else if (isStashed && !isInMain && isTrackedAsMain)
                {

                }

                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                    if (PluginConfig.Instance.IsBagScaleCapInfinite || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                    {
                        try
                        {
                            float baggedMass;
                            if (bagController != null)
                            {
                                baggedMass = 0f;
                                var bagState = BagPatches.GetState(bagController);
                                if (bagState?.BaggedObjects != null)
                                {
                                    foreach (var obj in bagState.BaggedObjects)
                                    {
                                        if (obj != null)
                                        {
                                            baggedMass += bagController.CalculateBaggedObjectMass(obj);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                baggedMass = (float)ReflectionCache.BaggedObject.BaggedMass.GetValue(__instance);
                            }
                            if (__instance != null)
                            {
                                ReflectionCache.BaggedObject.BaggedMass?.SetValue(__instance, baggedMass);
                                BaggedObjectPatches.UpdateBagScale(__instance, baggedMass);
                            }
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

        // ========================================================================================
        // BAGGED OBJECT ON EXIT
        // ========================================================================================
        [HarmonyPatch(typeof(BaggedObject), "OnExit")]
        public class BaggedObject_OnExit
        {
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

                    Log.Debug($"[BaggedObject_OnExit.Prefix] CALLED: InstanceTarget={BagHelpers.GetSafeName(__instance?.targetObject)}, StateTarget={BagHelpers.GetSafeName(currentTarget)}, State={currentStateName}, MainPassenger={BagHelpers.GetSafeName(currentMain)}");
                }

                if (bagController == null)
                {
                    Log.Warning("[BaggedObject_OnExit.Prefix] bagController is null, proceeding with vanilla OnExit");
                    return true;
                }

                if (__instance == null || __instance.targetObject == null)
                {
                    Log.Warning("[BaggedObject_OnExit.Prefix] targetObject is null - likely deserialization failure or object destroyed");
                    NetworkUtils.LogObjectDetails(__instance?.outer?.gameObject, "BaggedObject_OnExit.Prefix");

                }
                else
                {

                    if (!NetworkUtils.ValidateObjectReady(__instance.targetObject))
                    {
                        Log.Warning($"[BaggedObject_OnExit.Prefix] {__instance.targetObject.name} is not ready for network operations");

                    }

                    NetworkUtils.LogNetworkOperation("BaggedObject_OnExit", __instance.targetObject, NetworkServer.active, new Dictionary<string, object>
                    {
                        { "bagController", bagController.name },
                        { "isAuthority", bagController.hasAuthority }
                    });
                }

                if (__instance != null && __instance.targetObject != null)
                {
                    var fuse = __instance.targetObject.GetComponent<RoR2.Projectile.ProjectileFuse>();
                    if (fuse != null) fuse.enabled = true;
                }

                bool shouldKeepOverrides = false;
                bool isDifferentObjectInMainSeat = false;
                bool isDeadCheck = false;
                bool isTrackedAsMain = false;
                bool isPhysicallyInSeat = false;
                GameObject? targetObject = __instance?.targetObject;

                if (bagController != null && targetObject != null)
                {

                    var tracked = BagPatches.GetMainSeatObject(bagController);
                    isTrackedAsMain = tracked != null && ReferenceEquals(targetObject, tracked);

                    isPhysicallyInSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                            ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject);

                    isDifferentObjectInMainSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                                        !ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject);

                    isDeadCheck = targetObject.TryGetComponent<HealthComponent>(out var healthComponent) && !healthComponent.alive;

                    shouldKeepOverrides = isTrackedAsMain && isPhysicallyInSeat && !isDeadCheck && targetObject.activeInHierarchy && !isDifferentObjectInMainSeat;
                }

                if (shouldKeepOverrides)
                {
                    Log.Debug($" [BaggedObject_OnExit] {BagHelpers.GetSafeName(targetObject)}: " +
                            $"isTrackedAsMain={isTrackedAsMain}, " +
                            $"isPhysicallyInSeat={isPhysicallyInSeat}, " +
                            $"isDeadCheck={isDeadCheck}, " +
                            $"activeInHierarchy={(targetObject?.activeInHierarchy ?? false)}, " +
                            $"isDifferentObjectInMainSeat={isDifferentObjectInMainSeat}, " +
                            $"shouldKeepOverrides={shouldKeepOverrides}.");
                }
                else
                {

                    bool preserveDuringCycling = false;
                    if (targetObject != null)
                    {
                        lock (_preserveOverridesDuringCycling)
                        {
                            preserveDuringCycling = _preserveOverridesDuringCycling.Contains(targetObject!);

                            _preserveOverridesDuringCycling.Remove(targetObject);
                        }
                    }

                    if (preserveDuringCycling && !isDifferentObjectInMainSeat)
                    {
                        Log.Debug($" [BaggedObject_OnExit] Skipping UnsetAllOverrides - object {BagHelpers.GetSafeName(targetObject)} is marked to preserve overrides during cycling.");
                    }
                    else
                    {
                        if (preserveDuringCycling)
                        {
                            Log.Debug($" [BaggedObject_OnExit] Forcing UnsetAllOverrides during cycling - different object in main seat or object is dead.");
                        }

                        if (__instance != null)
                        {
                            UnsetAllOverrides(__instance);
                        }
                    }
                }

                if (!__instance?.targetObject)
                {
                    Log.Debug($" [BaggedObject_OnExit] targetObject is null/destroyed, skipping original OnExit to prevent NRE (cleanup already attempted).");

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
                    bool isInAdditionalSeat = (bagController != null) && BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null;
                    bool isCurrentlyTrackedAsMain = (bagController != null) && BagPatches.GetMainSeatObject(bagController) == __instance.targetObject;

                    if (bagController != null && (isInAdditionalSeat || isCurrentlyTrackedAsMain))
                    {
                        var stateToSave = BaggedObjectPatches.LoadObjectState(bagController, __instance.targetObject) ?? new Core.BaggedObjectStateData();
                        stateToSave.CaptureBreakoutStateFromBaggedObject(__instance);
                        BaggedObjectPatches.SaveObjectState(bagController, __instance.targetObject, stateToSave);
                    }

                    if (!isInAdditionalSeat)
                    {
                        PerformPassengerRestoration(bagController, __instance.targetObject);
                    }
                    isDead = __instance.targetObject.TryGetComponent<HealthComponent>(out var hc) && !hc.alive;
                }

                if (isDead)
                {
                    Log.Debug($" [BaggedObject_OnExit] targetObject is dead/dying ({BagHelpers.GetSafeName(__instance?.targetObject)}), skipping original OnExit to avoid crashes (cleanup already attempted).");

                    if (__instance != null)
                    {
                        TrySpawnJunkForSkippedOnExit(__instance, $"dead/dying {BagHelpers.GetSafeName(__instance?.targetObject)}");
                        RemoveWalkSpeedPenalty(__instance!);
                    }
                    return false;
                }

                return true;
            }
        }

        internal static void UnsetAllOverrides(BaggedObject instance)
        {
            try
            {
                var body = instance.outer?.GetComponent<CharacterBody>();
                Log.Debug($"[BaggedObjectStatePatches.UnsetAllOverrides] Starting cleanup for instance of {instance.GetType().Name} on {(!body ? "null" : body!.name)}.");

                if (ReflectionCache.BaggedObject.OverriddenUtility != null && ReflectionCache.BaggedObject.UtilityOverride != null)
                {
                    var overriddenUtility = (GenericSkill)ReflectionCache.BaggedObject.OverriddenUtility.GetValue(instance);
                    var utilityOverride = (SkillDef)ReflectionCache.BaggedObject.UtilityOverride.GetValue(instance);
                    if (overriddenUtility != null)
                    {
                        if (utilityOverride != null) overriddenUtility.UnsetSkillOverride(instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                        ReflectionCache.BaggedObject.OverriddenUtility.SetValue(instance, null);
                    }
                }

                if (ReflectionCache.BaggedObject.OverriddenPrimary != null && ReflectionCache.BaggedObject.PrimaryOverride != null)
                {
                    var overriddenPrimary = (GenericSkill)ReflectionCache.BaggedObject.OverriddenPrimary.GetValue(instance);
                    var primaryOverride = (SkillDef)ReflectionCache.BaggedObject.PrimaryOverride.GetValue(instance);
                    if (overriddenPrimary != null)
                    {
                        if (primaryOverride != null) overriddenPrimary.UnsetSkillOverride(instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                        ReflectionCache.BaggedObject.OverriddenPrimary.SetValue(instance, null);
                    }
                }

                var skillLocator = body?.skillLocator;
                if (skillLocator != null)
                {
                    if (skillLocator.primary) CleanupSkillFromLocator(instance, skillLocator.primary);
                    if (skillLocator.secondary) CleanupSkillFromLocator(instance, skillLocator.secondary);
                    if (skillLocator.utility) CleanupSkillFromLocator(instance, skillLocator.utility);
                    if (skillLocator.special) CleanupSkillFromLocator(instance, skillLocator.special);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in UnsetAllOverrides: {ex.Message}");
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

        private static void TrySpawnJunkForSkippedOnExit(BaggedObject? instance, string reason)
        {
            try
            {
                DrifterBagController? drifterBagController = null;

                try
                {
                    drifterBagController = ReflectionCache.BaggedObject.DrifterBagController?.GetValue(instance) as DrifterBagController;
                }
                catch (Exception ex)
                {
                    Log.Debug($" [TrySpawnJunk] Reflection failed: {ex.Message}");
                }

                if (drifterBagController == null && instance != null && instance.outer != null && instance.outer.gameObject != null)
                {
                    drifterBagController = instance.outer.gameObject.GetComponent<DrifterBagController>();
                    Log.Debug($" [TrySpawnJunk] Traverse returned null, GetComponent returned: {(!drifterBagController ? "null" : drifterBagController!.name)}");
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string bName = BagHelpers.GetSafeName(drifterBagController);
                    string bbName = drifterBagController != null ? BagHelpers.GetSafeName(drifterBagController.baggedBody) : "NULL";
                    string attrName = drifterBagController != null ? BagHelpers.GetSafeName(drifterBagController.baggedAttributes) : "NULL";
                    Log.Debug($"[TrySpawnJunk] Reason: {reason} | bagController: {bName} | Server: {NetworkServer.active} | baggedBody: {bbName} | attributes: {attrName}");
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

                    var bagStateMachine = EntityStateMachine.FindByCustomName(drifterBagController.gameObject, "Bag");
                    bool hasValidBaggedObjectState = false;
                    if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo)
                    {
                        hasValidBaggedObjectState = bo.targetObject != null;
                    }

                    bool isSwappingOrHasTarget = DrifterBossGrabPlugin.IsSwappingPassengers || hasValidBaggedObjectState;

                    if (targetIsDestroyedOrNull && !isSwappingOrHasTarget && wasSuccessfullyInitialized)
                    {
                        Log.Debug($"[TrySpawnJunk] targetObject is null/destroyed — spawning junk WITHOUT ExecuteBody() to avoid incrementing wrong object's invisibilityCount");

                        var drifterBody = drifterBagController.GetComponent<CharacterBody>();
                        var drifterMotor = drifterBody?.characterMotor;
                        if (drifterMotor != null)
                        {
                            drifterMotor.Motor.ForceUnground(0.1f);
                            drifterMotor.velocity = new Vector3(drifterMotor.velocity.x, Mathf.Max(drifterMotor.velocity.y, 8f), drifterMotor.velocity.z);
                        }

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
                        Log.Debug($"[TrySpawnJunk] SKIPPED junk spawn - null target detected during passenger swap (isSwapping={DrifterBossGrabPlugin.IsSwappingPassengers}, hasValidBaggedObjectState={hasValidBaggedObjectState})");
                    }
                    else if (targetIsDestroyedOrNull && !wasSuccessfullyInitialized)
                    {
                        Log.Debug($"[TrySpawnJunk] SKIPPED junk spawn - null target detected during grab operation (object was not successfully initialized via OnEnter)");
                    }
                    else
                    {
                        if (drifterBagController.baggedBody != null && instance != null && drifterBagController.baggedBody != instance.targetObject)
                        {
                            Log.Debug($"[TrySpawnJunk] >>> baggedBody changed (auto-promoted)! Manually spawning junk for {BagHelpers.GetSafeName(instance?.targetObject)} to protect new passenger {BagHelpers.GetSafeName(drifterBagController.baggedBody)}.");

                            var targetAttributes = (instance != null && instance.targetObject != null) ? instance.targetObject.GetComponent<SpecialObjectAttributes>() : null;
                            var drifterBody = drifterBagController.GetComponent<CharacterBody>();
                            Vector3 dropLocation = drifterBody ? drifterBody.corePosition : drifterBagController.transform.position;

                            int scrapCount = 4;
                            var junkCtrl = ReflectionCache.DrifterBagController.JunkController?.GetValue(drifterBagController) as JunkController;
                            if (junkCtrl != null) junkCtrl.CallCmdGenerateJunkQuantity(dropLocation, scrapCount);
                        }
                        else
                        {
                            Log.Debug($"[TrySpawnJunk] >>> Calling ExecuteBody() to spawn junk for {BagHelpers.GetSafeName(instance?.targetObject)}");
                            drifterBagController!.ExecuteBody();
                            drifterBagController.ResetBaggedObject();
                        }
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[TrySpawnJunk] >>> SKIPPED ExecuteBody - controller null: {drifterBagController == null}, server: {NetworkServer.active}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($" [TrySpawnJunk] Error: {ex.Message}");
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

                for (int i = overridesList.Count - 1; i >= 0; i--)
                {
                    var skillOverride = overridesList[i];

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
                Log.Error($"[CleanupSkillFromLocator] Failed to cleanup skill overrides: {ex.Message}");
            }
        }
        [HarmonyPostfix]
        public static void Postfix(BaggedObject __instance)
        {
            var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
            if (bagController == null || __instance?.targetObject == null) return;

            var tracked = BagPatches.GetMainSeatObject(bagController);
            bool isTrackedAsMain = tracked != null && ReferenceEquals(__instance.targetObject, tracked);
            bool inAdditionalSeat = BagHelpers.GetAdditionalSeat(bagController, __instance.targetObject) != null;

            bool stillInMainSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                   ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, __instance.targetObject);
            bool stillInAnySeat = stillInMainSeat || inAdditionalSeat;

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

            bool inSwapOrAutoGrab = DrifterBossGrabPlugin.IsSwappingPassengers ||
                                     CycleNetworkHandler.SuppressBroadcasts;
            if (inSwapOrAutoGrab && !shouldRemove)
            {

                return;
            }

            if (isTrackedAsMain && !inAdditionalSeat && !stillInAnySeat)
            {

                Networking.BottomlessBagNetworkController? netController = null;
                if (bagController != null)
                {
                    netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                    if (netController != null)
                    {

                    }
                }

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

                if (bagController != null)
                {
                    BagCarouselUpdater.UpdateCarousel(bagController);
                }
            }
        }

        [HarmonyPatch(typeof(BaggedObject), "HoldsDeadBody")]
        public class BaggedObject_HoldsDeadBody_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, ref bool __result)
            {
                if (__instance == null || __instance.targetObject == null)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(BaggedObject), nameof(BaggedObject.FixedUpdate))]
        public class BaggedObject_FixedUpdate_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {

                if (__instance == null || __instance.targetObject == null || __instance.outer == null ||
                    __instance.drifterBagController == null)
                {
                    return false;
                }

                if (__instance.isAuthority && __instance.baseAI != null && __instance.targetBody == null)
                {
                    return false;
                }

                return true;
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

                if (__instance == null || __instance.outer == null)
                {
                    return true;
                }

                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    return true;
                }

                Log.Debug($"[BaggedObject_UpdateBaggedObjectMass] Suppressing vanilla penalty update for {(!__instance.targetObject ? "null" : __instance.targetObject!.name)}");
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
                        var passenger = BagPatches.GetMainSeatObject(bagController);
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
                            var mainSeatObject = BagPatches.GetMainSeatObject(bagController);
                            if (mainSeatObject != null && ReferenceEquals(mainSeatObject, passenger))
                            {
                                isTracked = true;
                            }
                            else if (BagPatches.GetState(bagController).BaggedObjects.Contains(passenger))
                            {
                                isTracked = true;
                            }
                            if (!isTracked)
                            {
                                if (bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger && ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, passenger))
                                {
                                    isTracked = true;
                                }
                                else
                                {
                                    var childSeats = bagController.GetComponentsInChildren<VehicleSeat>(true);
                                    foreach (var seat in childSeats)
                                    {
                                        if (seat != null && seat.hasPassenger && ReferenceEquals(seat.NetworkpassengerBodyObject, passenger))
                                        {
                                            isTracked = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            bool isDead = BaggedObjectPatches.IsPassengerDeadOrDestroyed(passenger);
                            if (!isDead && isTracked)
                            {
                                return false;
                            }
                        }
                    }
                }
                return true;
            }

        }

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

                if (!_trackedESMs.TryGetValue(__instance, out var tracker)) return;

                if (tracker == null || tracker.isRemovingManual || DrifterBossGrabPlugin.IsSwappingPassengers) return;

                var controller = tracker.controller;
                if (controller == null) return;

                var obj = __instance.gameObject;
                if (obj == null) return;

                string newStateName = newState.GetType().Name;
                string currentStateName = __instance.state?.GetType()?.Name ?? "null";

                if (newState is EntityStates.GenericCharacterVehicleSeated) return;
                if (newStateName.Contains("SpawnState")) return;

                var newStateType = newState.GetType();
                var mainStateType = __instance.mainStateType.stateType;
                bool isMainState = (newStateType != null && mainStateType != null && newStateType == mainStateType) || newStateName == "GenericCharacterMain";

                bool isIdleOrInit = newStateName.Contains("Idle") || newStateName.Contains("Uninitialized");
                bool isMainSafe = isMainState && currentStateName.Contains("VehicleSeated");
                bool isStunSafe = newStateName.Contains("StunState") && currentStateName.Contains("VehicleSeated");

                if (isIdleOrInit || isMainSafe || isStunSafe) return;

                Log.Debug($"[EntityStateMachine_SetState] Bagged object {obj.name} ESM '{__instance.customName}' transitioning {currentStateName} → {newStateName} (UNAUTHORIZED/ESCAPE) — cleaning up bag tracking");

                try
                {
                    PerformPassengerRestoration(controller, obj);
                    BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying: false);
                    BagCarouselUpdater.UpdateCarousel(controller);
                }
                catch (Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Warning($"[EntityStateMachine_SetState] Error during escape cleanup: {ex.Message}");
                }
            }
        }

    }
}
