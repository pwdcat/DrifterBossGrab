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
using DrifterBossGrabMod.Features;
using DrifterBossGrabMod.Core;
namespace DrifterBossGrabMod.Patches
{
    public static class BaggedObjectPatches
    {
        private static string GetSafeName(UnityEngine.Object? obj) => obj ? obj!.name : "null";
        private static readonly HashSet<GameObject> _suppressedExitObjects = new HashSet<GameObject>();

        public static void SuppressExitForObject(GameObject obj)
        {
            if (obj == null) return;
            lock (_suppressedExitObjects)
            {
                _suppressedExitObjects.Add(obj);
            }
            // Reset after 2 seconds to be safe
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(ResetSuppressionForObject(obj, 2f));
            }
        }
        public static bool IsObjectExitSuppressed(GameObject obj)
        {
            if (obj == null) return false;
            lock (_suppressedExitObjects)
            {
                return _suppressedExitObjects.Contains(obj);
            }
        }
        private static System.Collections.IEnumerator ResetSuppressionForObject(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            lock (_suppressedExitObjects)
            {
                _suppressedExitObjects.Remove(obj);
            }
        }

        // Reflection Cache - using centralized ReflectionCache
        private static readonly MethodInfo _onSyncBaggedObjectMethod = ReflectionCache.DrifterBagController.OnSyncBaggedObject;
        private static readonly MethodInfo _tryOverrideUtilityMethod = ReflectionCache.BaggedObject.TryOverrideUtility;
        private static readonly MethodInfo _tryOverridePrimaryMethod = ReflectionCache.BaggedObject.TryOverridePrimary;
        private static readonly FieldInfo _bagScale01Field = ReflectionCache.BaggedObject.BagScale01;
        private static readonly MethodInfo _setScaleMethod = ReflectionCache.BaggedObject.SetScale;

        public static BaggedObject? FindExistingBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null || targetObject == null) return null;

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo)
            {
                // Return the existing state even if targetObject differs — just update it
                return bo;
            }
            return null;
        }

        public static void SynchronizeBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[SynchronizeBaggedObjectState] Called with targetObject={targetObject?.name ?? "null"}, EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}, hasAuthority={bagController.hasAuthority}");
            }
            BaggedObject? baggedObject = null;
            if (targetObject != null)
            {
                baggedObject = FindOrCreateBaggedObjectState(bagController, targetObject);
                if (baggedObject != null)
                {
                    // Set the target immediately to ensure it's available when the state machine transitions
                    baggedObject.targetObject = targetObject;
                    UpdateTargetFields(baggedObject);
                }
            }

            // 1. Update network state (for multiplayer)
            if (NetworkServer.active)
            {
                if (bagController.NetworkbaggedObject != targetObject)
                {
                    bagController.NetworkbaggedObject = targetObject;
                }

                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    var currentBaggedObj = bagController.baggedObject;
                    if (currentBaggedObj != targetObject)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {targetObject?.name ?? "null"}");
                        _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
                }
            }
            else if (bagController.hasAuthority)
            {
                // Check if we need to update to avoid redundant calls
                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    var currentBaggedObj = bagController.baggedObject;
                    if (currentBaggedObj != targetObject)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {targetObject?.name ?? "null"}");
                        // Use cached reflection to call private OnSyncBaggedObject
                        _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
                }
            }

            // 2. Apply skill overrides (not handled by VehicleSeat.OnPassengerEnter())
            if (baggedObject != null && targetObject != null)
            {
                var baggedList = BagPatches.GetState(bagController).BaggedObjects;
                bool isInBag = baggedList != null && baggedList.Contains(targetObject);
                bool isProjectile = ProjectileRecoveryPatches.IsInProjectileState(targetObject);

                if (isInBag && !isProjectile)
                {
                    var skillLocator = baggedObject.outer.GetComponent<SkillLocator>();
                    if (skillLocator != null)
                    {
                        if (skillLocator.utility != null)
                        {
                            _tryOverrideUtilityMethod?.Invoke(baggedObject, new object[] { skillLocator.utility });
                        }
                        if (skillLocator.primary != null)
                        {
                            _tryOverridePrimaryMethod?.Invoke(baggedObject, new object[] { skillLocator.primary });
                        }
                    }
                }
            }

            // 3. Apply balance mode
            if (PluginConfig.Instance.EnableBalance.Value && targetObject != null)
            {
                // Ensure individual state is calculated and saved properly.
                // Doing this prevents 'All' mode aggregate from overwriting the pure state of the individual object.
                var individualState = StateCalculator.CalculateState(bagController, targetObject, StateCalculationMode.Current);
                if (individualState != null)
                {
                    BaggedObjectStateStorage.SaveObjectState(bagController, targetObject, individualState);
                }

                // THEN calculate the state intended for the physical BaggedObject (which could be the aggregate)
                var calculatedState = StateCalculator.CalculateState(
                    bagController,
                    targetObject,
                    PluginConfig.Instance.StateCalculationMode.Value);

                if (calculatedState != null)
                {
                    // Apply to BaggedObject state if it exists
                    if (baggedObject != null)
                    {
                        calculatedState.ApplyToBaggedObject(baggedObject);
                    }
                }
            }
        }
        public static void UpdateTargetFields(BaggedObject? instance)
        {
            if (instance == null || instance.targetObject == null) return;

            bool isBody = instance.targetObject.TryGetComponent<CharacterBody>(out var body);
            if (ReflectionCache.BaggedObject.IsBody != null)
            {
                ReflectionCache.BaggedObject.IsBody.SetValue(instance, isBody);
            }

            if (isBody && ReflectionCache.BaggedObject.TargetBody != null)
            {
                ReflectionCache.BaggedObject.TargetBody.SetValue(instance, body);
            }
            if (ReflectionCache.BaggedObject.VehiclePassengerAttributes != null)
            {
                instance.targetObject.TryGetComponent<SpecialObjectAttributes>(out var attributes);
                ReflectionCache.BaggedObject.VehiclePassengerAttributes.SetValue(instance, attributes);
            }
        }

        public static void UpdateBagScale(BaggedObject baggedObject, float mass)
        {
            if (baggedObject == null) return;

            float maxCapacity = DrifterBagController.maxMass;
            var controller = baggedObject.outer.GetComponent<DrifterBagController>();
            if (controller != null)
            {
                maxCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
            }

            float value = mass;
            if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.IsBagScaleCapInfinite)
            {
                value = Mathf.Clamp(mass, 1f, maxCapacity); // Scale is handled by UncappedBagScaleComponent if it exceeds maxCapacity
            }
            else
            {
                value = Mathf.Max(mass, 1f);
            }

            float t = (value - 1f) / (maxCapacity - 1f);
            float bagScale01 = 0.5f + 0.5f * t;

            if (_bagScale01Field != null)
            {
                _bagScale01Field.SetValue(baggedObject, bagScale01);
            }

            // When BagScaleCap is enabled
            if (PluginConfig.Instance.EnableBalance.Value)
            {
                bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                if (isScaleUncapped || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                {
                    if (controller != null)
                    {
                        BagPassengerManager.UpdateUncappedBagScale(controller, mass);
                    }
                }
            }
            else if (_setScaleMethod != null)
            {
                // Use original animation parameter method when not uncapped
                _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
            }
        }

        // Helper methods for per-object state storage
        public static void SaveObjectState(DrifterBagController controller, GameObject obj, BaggedObjectStateData state)
        {
            BaggedObjectStateStorage.SaveObjectState(controller, obj, state);
        }

        public static BaggedObjectStateData? LoadObjectState(DrifterBagController controller, GameObject obj)
        {
            return BaggedObjectStateStorage.LoadObjectState(controller, obj);
        }

        public static void CleanupObjectState(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.CleanupObjectState(controller, obj);
        }

        // Remove the UI overlay for an object that has left the main seat.
        public static void RemoveUIOverlay(GameObject targetObject, DrifterBagController? bagController = null)
        {
            BaggedObjectUIPatches.RemoveUIOverlay(targetObject, bagController);
        }

        // Handle UI removal when cycling to null state (main seat becomes empty)
        public static void RemoveUIOverlayForNullState(DrifterBagController bagController)
        {
            BaggedObjectUIPatches.RemoveUIOverlayForNullState(bagController);
        }

        public static void RefreshUIOverlayForMainSeat(DrifterBagController? bagController, GameObject? targetObject)
        {
            BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
        }

        // Helper method to check if an object is in the main seat
        private static bool IsInMainSeat(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return false;

            // Check tracked main seat (authoritative)
            var trackedMainSeat = BagPatches.GetMainSeatObject(bagController);
            if (trackedMainSeat != null)
            {
                return ReferenceEquals(targetObject, trackedMainSeat);
            }

            // Fallback to vehicle seat check only if not logically tracked
            var outerSeat = bagController.vehicleSeat;
            if (outerSeat == null) return false;

            var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
            bool result = outerCurrentPassengerBodyObject != null && ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);

            // Also check if it's in an additional seat
            if (result && BagHelpers.GetAdditionalSeat(bagController, targetObject) != null)
            {
                result = false;
            }

            return result;
        }

        // Patch TryOverrideUtility to only allow skill overrides for main vehicle seat objects
        [HarmonyPatch(typeof(BaggedObject), "TryOverrideUtility")]
        public class BaggedObject_TryOverrideUtility
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                // Get the DrifterBagController
                var bagController = __instance!.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    return true; // Allow normal execution
                }
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);

                // Also allow if object is being cycled to main seat (tracked but not yet physically assigned)
                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                bool isBeingCycledToMain = trackedMain != null &&
                                         ReferenceEquals(trackedMain, targetObject);

                bool shouldAllowOverride = isMainSeatOccupant || isBeingCycledToMain;

                if (shouldAllowOverride)
                {
                    return true; // Allow normal execution
                }
                else
                {
                    // If not in main seat and not being cycled to main seat, unset the override
                    if (ReflectionCache.BaggedObject.OverriddenUtility != null && ReflectionCache.BaggedObject.UtilityOverride != null)
                    {
                        var overriddenUtility = (GenericSkill)ReflectionCache.BaggedObject.OverriddenUtility.GetValue(__instance);

                        if (overriddenUtility != null)
                        {
                            var utilityOverride = (SkillDef)ReflectionCache.BaggedObject.UtilityOverride.GetValue(__instance);
                            overriddenUtility.UnsetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                            ReflectionCache.BaggedObject.OverriddenUtility.SetValue(__instance, null);

                        }
                    }
                    return false; // Skip the original method - no skill override
                }
            }

        }
        // Patch TryOverridePrimary to only allow skill overrides for main vehicle seat objects
        [HarmonyPatch(typeof(BaggedObject), "TryOverridePrimary")]
        public class BaggedObject_TryOverridePrimary
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                // Get the DrifterBagController
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {

                    return true; // Allow normal execution
                }
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);

                // NEW: Also allow if object is being cycled to main seat (tracked but not yet physically assigned)
                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                bool isBeingCycledToMain = trackedMain != null &&
                                         ReferenceEquals(trackedMain, targetObject);

                bool shouldAllowOverride = isMainSeatOccupant || isBeingCycledToMain;

                if (shouldAllowOverride)
                {
                    return true; // Allow normal execution
                }
                else
                {
                    // If not in main seat and not being cycled to main seat, unset the override
                    if (ReflectionCache.BaggedObject.OverriddenPrimary != null && ReflectionCache.BaggedObject.PrimaryOverride != null)
                    {
                        var overriddenPrimary = (GenericSkill)ReflectionCache.BaggedObject.OverriddenPrimary.GetValue(__instance);

                        if (overriddenPrimary != null)
                        {
                            var primaryOverride = (SkillDef)ReflectionCache.BaggedObject.PrimaryOverride.GetValue(__instance);
                            overriddenPrimary.UnsetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                            ReflectionCache.BaggedObject.OverriddenPrimary.SetValue(__instance, null);

                        }
                    }
                    return false; // Skip the original method - no skill override
                }
            }

        }

        public static GameObject? GetMainSeatOccupant(DrifterBagController controller)
        {
            if (controller == null || controller.vehicleSeat == null) return null;
            if (!controller.vehicleSeat.hasPassenger) return null;
            return controller.vehicleSeat.currentPassengerBody != null ? controller.vehicleSeat.currentPassengerBody.gameObject : null;
        }

        public static BaggedObject? FindOrCreateBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null || targetObject == null) return null;

            // DIAGNOSTIC LOG: Track when we're creating a new BaggedObject state
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[FindOrCreateBaggedObjectState] Called with targetObject={(targetObject != null ? targetObject.name : "null")}, NetworkServer.active={NetworkServer.active}");
            }

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo && bo.targetObject == targetObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[FindOrCreateBaggedObjectState] Found existing BaggedObject state for {(targetObject != null ? targetObject.name : "null")}");
                }
                return bo;
            }

            try
            {
                var targetStateMachine = bagStateMachine;
                if (targetStateMachine == null)
                {
                    targetStateMachine = bagController.gameObject.AddComponent<EntityStateMachine>();
                    targetStateMachine.customName = "Bag";
                }

                if (targetStateMachine != null)
                {
                    var baggedList = BagPatches.GetState(bagController).BaggedObjects;
                    bool isTracked = baggedList != null && targetObject != null && baggedList.Contains(targetObject);
                    if (!isTracked)
                    {
                        int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                        int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                        if (currentCount >= effectiveCapacity)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Debug($"[FindOrCreateBaggedObjectState] Skipping - bag full ({currentCount}/{effectiveCapacity}) for {(targetObject != null ? targetObject.name : "null")}");
                            return null;
                        }
                    }

                    var constructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        var newBaggedObject = (BaggedObject)constructor.Invoke(null);
                        newBaggedObject.targetObject = targetObject;
                        var bagCtrl = bagStateMachine != null ? bagStateMachine.GetComponent<DrifterBagController>() : bagController.gameObject.GetComponent<DrifterBagController>();
                        if (bagCtrl != null)
                        {
                            ReflectionCache.BaggedObject.DrifterBagController?.SetValue(newBaggedObject, bagCtrl);
                        }
                        // DIAGNOSTIC LOG: Log when we create a new BaggedObject
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[FindOrCreateBaggedObjectState] Creating NEW BaggedObject with targetObject={(targetObject != null ? targetObject.name : "null")}, drifterBagController={(bagCtrl != null ? bagCtrl.name : "null")}");
                        }
                        targetStateMachine.SetState(newBaggedObject);
                        return newBaggedObject;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[FindOrCreateBaggedObjectState] Error: {ex}");
            }
            return null;
        }

        [HarmonyPatch(typeof(RoR2.VehicleSeat), "OnPassengerExit")]
        public class VehicleSeat_OnPassengerExit
        {
            [HarmonyPostfix]
            public static void Postfix(RoR2.VehicleSeat __instance, GameObject passenger)
            {
                if (passenger == null) return;

                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController == null) return;
                if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

                var mainSeatObject = BagPatches.GetMainSeatObject(bagController);
                bool isTrackedAsMainSeat = mainSeatObject != null && ReferenceEquals(mainSeatObject, passenger);

                var baggedObjectsList = BagPatches.GetState(bagController).BaggedObjects;
                bool isInBaggedObjects = baggedObjectsList != null && baggedObjectsList!.Contains(passenger);

                // Check if the passenger was reassigned to another seat
                bool isInMainSeat = bagController.vehicleSeat != null &&
                                    bagController.vehicleSeat.hasPassenger &&
                                    ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, passenger);

                // Get the additional seat currently assigned to this object in our tracking dictionary
                var currentAssignedSeat = BagHelpers.GetAdditionalSeat(bagController, passenger);
                bool isInAdditionalSeat = currentAssignedSeat != null &&
                                          currentAssignedSeat.hasPassenger &&
                                          ReferenceEquals(currentAssignedSeat.NetworkpassengerBodyObject, passenger);

                if (isInMainSeat || isInAdditionalSeat)
                {
                    return; // Passenger was moved to another seat, not truly ejected
                }

                if ((isTrackedAsMainSeat || isInBaggedObjects) && !IsPassengerDeadOrDestroyed(passenger))
                {
                    if (isTrackedAsMainSeat) BagPatches.SetMainSeatObject(bagController, null);
                    if (isInBaggedObjects && baggedObjectsList != null)
                    {
                        baggedObjectsList.Remove(passenger);
                        BagPatches.GetState(bagController).RemoveInstanceId(passenger.GetInstanceID());
                    }

                    BagCarouselUpdater.UpdateCarousel(bagController);
                    BagCarouselUpdater.UpdateNetworkBagState(bagController);
                    BagPassengerManager.ForceRecalculateMass(bagController);
                    RemoveUIOverlay(passenger, bagController);
                    
                    // Clean up initialization tracking when passenger truly exits
                    BaggedObjectStatePatches.BaggedObject_OnExit.ClearObjectSuccessfullyInitialized(passenger);
                }
            }

            private static bool IsPassengerDeadOrDestroyed(GameObject passenger)
            {
                if (passenger == null) return true;
                var healthComponent = passenger.GetComponent<HealthComponent>();
                if (healthComponent != null && !healthComponent.alive) return true;
                if (passenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0) return true;
                return false;
            }
        }
    }
}
