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
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // BAGGED OBJECT PATCHES
    // ========================================================================================

    public static class BaggedObjectPatches
    {
        // ========================================================================================
        // EXIT SUPPRESSION
        // ========================================================================================

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
        // ========================================================================================
        // STATE SYNCHRONIZATION
        // ========================================================================================

        private static readonly MethodInfo _onSyncBaggedObjectMethod = ReflectionCache.DrifterBagController.OnSyncBaggedObject;
        private static readonly MethodInfo _tryOverrideUtilityMethod = ReflectionCache.BaggedObject.TryOverrideUtility;
        private static readonly MethodInfo _tryOverridePrimaryMethod = ReflectionCache.BaggedObject.TryOverridePrimary;
        private static readonly FieldInfo _bagScale01Field = ReflectionCache.BaggedObject.BagScale01;
        private static readonly MethodInfo _setScaleMethod = ReflectionCache.BaggedObject.SetScale;
        private static bool _isSynchronizing = false;

        public static BaggedObject? FindExistingBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null || targetObject == null) return null;

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo)
            {
                return bo;
            }
            return null;
        }

        public static void SynchronizeBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null) return;

            if (_isSynchronizing) return;
            _isSynchronizing = true;

            try
            {
                if (targetObject == null)
                {
                    Log.DebugIfEnabled("[SynchronizeBaggedObjectState] targetObject is null");
                    BaggedObjectStatePatches.UnsetAllOverrides(null, bagController.gameObject);
                    return;
                }

                Log.DebugIfEnabled("[SynchronizeBaggedObjectState] Called with targetObject={0}, EnableBalance={1}, NetworkServer.active={2}, hasAuthority={3}.",
                    targetObject.name, PluginConfig.Instance.EnableBalance.Value, NetworkServer.active, bagController.hasAuthority);

                BaggedObject? baggedObject = null;
                if (targetObject != null)
                {
                    baggedObject = FindOrCreateBaggedObjectState(bagController, targetObject);
                    if (baggedObject == null)
                    {
                        Log.DebugIfEnabled("[SynchronizeBaggedObjectState] FindOrCreateBaggedObjectState returned null for {0}", targetObject.name);
                    }
                    else
                    {
                        // Set the target immediately to ensure it's available when the state machine transitions
                        baggedObject.targetObject = targetObject;
                        UpdateTargetFields(baggedObject);
                        Log.DebugIfEnabled("[SynchronizeBaggedObjectState] Set targetObject and called UpdateTargetFields for {0}", targetObject.name);
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
                            Log.DebugIfEnabled("[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {0}", (!targetObject ? "null" : targetObject!.name));
                            _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                        }
                    }
                    else
                    {
                        Log.DebugIfEnabled("[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
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
                            Log.DebugIfEnabled("[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {0}", (!targetObject ? "null" : targetObject!.name));
                            // Use cached reflection to call private OnSyncBaggedObject
                            _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                        }
                    }
                    else
                    {
                        Log.DebugIfEnabled("[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
                    }
                }

                // 2. Apply skill overrides (not handled by VehicleSeat.OnPassengerEnter())
                if (baggedObject != null && targetObject != null)
                {
                    var baggedList = API.DrifterBagAPI.GetBaggedObjects(bagController);
                    bool isInBag = baggedList != null && baggedList.Contains(targetObject);

                    // Also consider the object "in bag" if it's the main seat occupant or physically in the vehicle seat
                    // This handles the client-side timing window where the object enters BaggedObject state
                    // before BaggedObject_OnEnter.Postfix adds it to the BaggedObjects list
                    if (!isInBag)
                    {
                        var mainSeat = API.DrifterBagAPI.GetMainPassenger(bagController);
                        if (mainSeat != null && ReferenceEquals(mainSeat, targetObject))
                        {
                            isInBag = true;
                        }
                        else if (bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                 ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject))
                        {
                            isInBag = true;
                        }
                        else if (baggedObject.targetObject != null && ReferenceEquals(baggedObject.targetObject, targetObject))
                        {
                            isInBag = true;
                        }
                    }

                    bool isProjectile = ProjectileRecoveryPatches.IsInProjectileState(targetObject);

                    Log.DebugIfEnabled("[SynchronizeBaggedObjectState] Override check for {0}: isInBag={1}, isProjectile={2}", targetObject.name, isInBag, isProjectile);

                    if (isInBag && !isProjectile)
                    {
                        var skillLocator = baggedObject.outer.GetComponent<SkillLocator>();
                        if (skillLocator != null)
                        {
                            Log.DebugIfEnabled("[SynchronizeBaggedObjectState] Applying skill overrides for {0}", targetObject.name);
                            if (skillLocator.utility != null)
                            {
                                _tryOverrideUtilityMethod?.Invoke(baggedObject, new object[] { skillLocator.utility });
                            }
                            if (skillLocator.primary != null)
                            {
                                _tryOverridePrimaryMethod?.Invoke(baggedObject, new object[] { skillLocator.primary });
                            }
                        }
                        else
                        {
                            Log.DebugIfEnabled("[SynchronizeBaggedObjectState] SkillLocator is null for {0}", targetObject.name);
                        }
                    }
                }

                // 3. Apply balance mode
                if (PluginConfig.Instance.EnableBalance.Value && targetObject != null)
                {
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
            finally
            {
                _isSynchronizing = false;
            }
        }
        // ========================================================================================
        // UTILITIES
        // ========================================================================================

        public static void UpdateTargetFields(BaggedObject? instance)
        {
            if (instance == null || instance.targetObject == null) return;

            Log.DebugIfEnabled("[UpdateTargetFields] ENTRY: instance.targetObject={0}", instance.targetObject.name);

            bool isBody = instance.targetObject.TryGetComponent<CharacterBody>(out var body);
            if (ReflectionCache.BaggedObject.IsBody != null)
            {
                ReflectionCache.BaggedObject.IsBody.SetValue(instance, isBody);
                Log.DebugIfEnabled("[UpdateTargetFields] Set isBody={0}", isBody);
            }

            if (isBody && ReflectionCache.BaggedObject.TargetBody != null)
            {
                ReflectionCache.BaggedObject.TargetBody.SetValue(instance, body);
                Log.DebugIfEnabled("[UpdateTargetFields] Set targetBody={0}", (!body ? "null" : body!.name));
            }
            if (ReflectionCache.BaggedObject.VehiclePassengerAttributes != null)
            {
                instance.targetObject.TryGetComponent<SpecialObjectAttributes>(out var attributes);
                ReflectionCache.BaggedObject.VehiclePassengerAttributes.SetValue(instance, attributes);
                Log.DebugIfEnabled("[UpdateTargetFields] Set vehiclePassengerAttributes={0}", (attributes != null ? "not null" : "null"));
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
                value = Mathf.Clamp(mass, 1f, maxCapacity);
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
                _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
            }
        }

        // ========================================================================================
        // STATE STORAGE
        // ========================================================================================

        public static void SaveObjectState(DrifterBagController controller, GameObject obj, BaggedObjectStateData state)
        {
            BaggedObjectStateStorage.SaveObjectState(controller, obj, state);
        }

        public static BaggedObjectStateData? LoadObjectState(DrifterBagController controller, GameObject obj)
        {
            return BaggedObjectStateStorage.LoadObjectState(controller, obj);
        }

        public static BaggedObjectStateData? FindStateForObject(GameObject obj)
        {
            return BaggedObjectStateStorage.FindStateForObject(obj);
        }

        public static void CleanupObjectState(DrifterBagController controller, GameObject obj, bool preserveForThrow = false)
        {
            BaggedObjectStateStorage.CleanupObjectState(controller, obj, preserveForThrow);
        }

        public static void PreserveStateForThrow(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.PreserveStateForThrow(controller, obj);
        }

        public static void RestorePreservedState(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.RestorePreservedState(controller, obj);
        }

        public static void ClearTemporaryPreservation(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.ClearTemporaryPreservation(controller, obj);
        }

        public static void ClearAllTemporaryPreservation(DrifterBagController controller)
        {
            BaggedObjectStateStorage.ClearAllTemporaryPreservation(controller);
        }

        // ========================================================================================
        // UI OVERLAY HELPERS
        // ========================================================================================

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

            var trackedMainSeat = API.DrifterBagAPI.GetMainPassenger(bagController);
            bool result = false;
            string reason = "";

            if (trackedMainSeat != null)
            {
                result = ReferenceEquals(targetObject, trackedMainSeat);
                reason = $"tracked main seat match: {result}";
            }
            else
            {
                var outerSeat = bagController.vehicleSeat;
                if (outerSeat == null)
                {
                    reason = "vehicle seat is null";
                }
                else
                {
                    var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
                    result = outerCurrentPassengerBodyObject != null && ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                    reason = $"physical seat match: {result}";

                    if (result && BagHelpers.GetAdditionalSeat(bagController, targetObject) != null)
                    {
                        result = false;
                        reason += " (but in additional seat)";
                    }
                }
            }

            Log.DebugIfEnabled("[IsInMainSeat] {0}: result={1}, reason={2}", (targetObject ? targetObject.name : "null"), result, reason);

            return result;
        }

        private static bool IsTargetMainSeatOccupant(BaggedObject instance, out DrifterBagController? bagController, out GameObject? mainSeatObj)
        {
            bagController = null;
            mainSeatObj = null;
            if (instance == null || !instance.outer) return false;
            bagController = instance.outer.GetComponent<DrifterBagController>();
            if (bagController == null) return false;

            mainSeatObj = API.DrifterBagAPI.GetMainPassenger(bagController);
            return mainSeatObj != null && ReferenceEquals(mainSeatObj, instance.targetObject);
        }

        // ========================================================================================
        // HARMONY PATCHES
        // ========================================================================================

        [HarmonyPatch(typeof(BaggedObject), "TryOverrideUtility")]
        public class BaggedObject_TryOverrideUtility
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                if (IsTargetMainSeatOccupant(__instance, out var bagController, out var mainSeatObj))
                {
                    Log.DebugIfEnabled("[TryOverrideUtility] target={0}, mainSeat={1}, isMain=True",
                        BagHelpers.GetSafeName(__instance.targetObject), BagHelpers.GetSafeName(mainSeatObj));

                    if (skill != null && __instance.utilityOverride != null)
                    {
                        skill.SetSkillOverride(bagController!.gameObject, __instance.utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(BaggedObject), "TryOverridePrimary")]
        public class BaggedObject_TryOverridePrimary
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                if (IsTargetMainSeatOccupant(__instance, out var bagController, out var mainSeatObj))
                {
                    Log.DebugIfEnabled("[TryOverridePrimary] target={0}, mainSeat={1}, isMain=True",
                        BagHelpers.GetSafeName(__instance.targetObject), BagHelpers.GetSafeName(mainSeatObj));

                    if (skill != null && __instance.primaryOverride != null)
                    {
                        skill.SetSkillOverride(bagController!.gameObject, __instance.primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                    }
                }
                else
                {
                    Log.DebugIfEnabled("[TryOverridePrimary] Skipping cleanup for {0}");
                }

                return false;
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

            Log.DebugIfEnabled("[FindOrCreateBaggedObjectState] Called with targetObject={0}, NetworkServer.active={1}",
                (!targetObject ? "null" : targetObject!.name), NetworkServer.active);

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo && bo.targetObject == targetObject)
            {
                Log.DebugIfEnabled("[FindOrCreateBaggedObjectState] Found existing BaggedObject state for {0}", (targetObject ? targetObject.name : "null"));
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
                    var baggedList = API.DrifterBagAPI.GetBaggedObjects(bagController);
                    bool isTracked = baggedList != null && targetObject != null && baggedList.Contains(targetObject);
                    if (!isTracked)
                    {
                        int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                        int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                        if (currentCount >= effectiveCapacity)
                        {
                            Log.DebugIfEnabled("[FindOrCreateBaggedObjectState] Skipping - bag full ({0}/{1}) for {2}",
                                currentCount, effectiveCapacity, (!targetObject ? "null" : targetObject!.name));
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
                        Log.DebugIfEnabled("[FindOrCreateBaggedObjectState] Creating NEW BaggedObject with targetObject={0}, drifterBagController={1}",
                            (!targetObject ? "null" : targetObject!.name), (!bagCtrl ? "null" : bagCtrl!.name));
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

        // ========================================================================================
        // PASSENGER EXIT HANDLING
        // ========================================================================================

        public static void HandlePassengerExit(RoR2.VehicleSeat seat, GameObject passenger)
        {
            if (seat == null || passenger == null) return;
            var bagController = seat.GetComponent<DrifterBagController>();
            if (bagController == null) return;
            if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

            var mainSeatObject = API.DrifterBagAPI.GetMainPassenger(bagController);
            bool isTrackedAsMainSeat = mainSeatObject != null && ReferenceEquals(mainSeatObject, passenger);

            var baggedObjectsList = API.DrifterBagAPI.GetBaggedObjects(bagController);
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
                return;
            }

            if ((isTrackedAsMainSeat || isInBaggedObjects) && !IsPassengerDeadOrDestroyed(passenger))
            {
                if (isTrackedAsMainSeat) API.DrifterBagAPI.SetMainSeatObject(bagController, null);
                if (isInBaggedObjects && baggedObjectsList != null)
                {
                    API.DrifterBagAPI.RemoveInstanceId(bagController, passenger.GetInstanceID());
                    API.DrifterBagAPI.RemoveBaggedObject(bagController, passenger);
                }

                BagCarouselUpdater.UpdateCarousel(bagController);
                BagCarouselUpdater.UpdateNetworkBagState(bagController);
                BagPassengerManager.ForceRecalculateMass(bagController);
                RemoveUIOverlay(passenger, bagController);

                // Clean up initialization tracking when passenger truly exits
                BaggedObjectStatePatches.BaggedObject_OnExit.ClearObjectSuccessfullyInitialized(passenger);
            }
        }

        public static bool IsPassengerDeadOrDestroyed(GameObject passenger)
        {
            if (passenger == null) return true;
            var healthComponent = passenger.GetComponent<HealthComponent>();
            if (healthComponent != null && !healthComponent.alive) return true;
            if (passenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0) return true;
            return false;
        }
    }
}
