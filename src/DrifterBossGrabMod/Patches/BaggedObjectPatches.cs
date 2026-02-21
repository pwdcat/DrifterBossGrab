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
        private static string GetSafeName(UnityEngine.Object? obj) => obj ? obj.name : "null";
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

        // Reflection Cache
        private static readonly FieldInfo _targetObjectField = AccessTools.Field(typeof(BaggedObject), "targetObject");
        private static readonly FieldInfo _targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
        private static readonly FieldInfo _isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
        private static readonly MethodInfo _holdsDeadBodyMethod = AccessTools.Method(typeof(BaggedObject), "HoldsDeadBody");
        private static readonly FieldInfo _vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
        private static readonly FieldInfo _baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
        private static readonly FieldInfo _uiOverlayControllerField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
        private static readonly FieldInfo _overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
        private static readonly FieldInfo _overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
        private static readonly FieldInfo _utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
        private static readonly FieldInfo _primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");
        private static readonly MethodInfo _onSyncBaggedObjectMethod = AccessTools.Method(typeof(DrifterBagController), "OnSyncBaggedObject", new Type[] { typeof(GameObject) });
        private static readonly MethodInfo _tryOverrideUtilityMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
        private static readonly MethodInfo _tryOverridePrimaryMethod = AccessTools.Method(typeof(BaggedObject), "TryOverridePrimary", new Type[] { typeof(GenericSkill) });
        private static readonly FieldInfo _bagScale01Field = AccessTools.Field(typeof(BaggedObject), "bagScale01");
        private static readonly MethodInfo _setScaleMethod = AccessTools.Method(typeof(BaggedObject), "SetScale", new Type[] { typeof(float) });

        // Store per-controller, per-object state data
        private static Dictionary<DrifterBagController, Dictionary<int, BaggedObjectStateData>> _perObjectStateStorage
            = new Dictionary<DrifterBagController, Dictionary<int, BaggedObjectStateData>>();

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
                Log.Info($"[SynchronizeBaggedObjectState] Called with targetObject={targetObject?.name ?? "null"}, EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}, hasAuthority={bagController.hasAuthority}");
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
                var currentBaggedObj = bagController.baggedObject;
                if (currentBaggedObj != targetObject)
                {
                    _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                }
            }
                else if (bagController.hasAuthority)
                {
                    // Check if we need to update to avoid redundant calls
                    var currentBaggedObj = bagController.baggedObject;
                    if (currentBaggedObj != targetObject)
                    {
                        // Use cached reflection to call private OnSyncBaggedObject
                        _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                    }
                }

            // 2. Apply skill overrides (NOT handled by VehicleSeat.OnPassengerEnter())
            if (baggedObject != null)
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

            // 3. Apply balance mode
            if (PluginConfig.Instance.EnableBalance.Value && targetObject != null)
            {
                var calculatedState = StateCalculator.CalculateState(
                    bagController,
                    targetObject,
                    PluginConfig.Instance.StateCalculationMode.Value);

                if (calculatedState != null)
                {
                    // Save individual object state if in Current mode
                    if (PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.Current && targetObject != null)
                    {
                        BaggedObjectStateStorage.SaveObjectState(bagController, targetObject, calculatedState);
                    }

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

            bool isBody = instance.targetObject.GetComponent<CharacterBody>() != null;
            _isBodyField?.SetValue(instance, isBody);

            if (isBody)
            {
                var body = instance.targetObject.GetComponent<CharacterBody>();
                _targetBodyField?.SetValue(instance, body);
            }
            _vehiclePassengerAttributesField?.SetValue(instance, instance.targetObject.GetComponent<SpecialObjectAttributes>());
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
            if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.UncapBagScale.Value)
            {
                value = Mathf.Clamp(mass, 1f, maxCapacity);
            }
            else
            {
                value = Mathf.Max(mass, 1f);
            }

            float t = (value - 1f) / (maxCapacity - 1f);
            float bagScale01 = 0.5f + 0.5f * t;

            _bagScale01Field.SetValue(baggedObject, bagScale01);

            // When UncapBagScale is enabled
            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapBagScale.Value)
            {
                if (controller != null)
                {
                    BagPassengerManager.UpdateUncappedBagScale(controller, mass);
                }
            }
            else
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

            // Check tracked main seat first
            var trackedMainSeat = BagPatches.GetMainSeatObject(bagController);

            // If the controller is tracked in mainSeatDict
            if (BagPatches.GetMainSeatObject(bagController) != null)
            {
                bool isTrackedAsMain = trackedMainSeat != null && ReferenceEquals(targetObject, trackedMainSeat);

                return isTrackedAsMain;
            }

            // Fallback to vehicle seat check only if not logically tracked
            var outerSeat = bagController!.vehicleSeat;
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

                // Only allow if the object is in the main seat OR being cycled to main seat
                if (shouldAllowOverride)
                {

                    return true; // Allow normal execution
                }
                else
                {
                    // If not in main seat and not being cycled to main seat, unset the override
                    var overriddenUtility = (GenericSkill)_overriddenUtilityField.GetValue(__instance);

                    if (overriddenUtility != null)
                    {
                        var utilityOverride = (SkillDef)_utilityOverrideField.GetValue(__instance);
                        overriddenUtility.UnsetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                        _overriddenUtilityField.SetValue(__instance, null);

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

                // Only allow if the object is in the main seat OR being cycled to main seat
                if (shouldAllowOverride)
                {

                    return true; // Allow normal execution
                }
                else
                {
                    // If not in main seat and not being cycled to main seat, unset the override
                    var overriddenPrimary = (GenericSkill)_overriddenPrimaryField.GetValue(__instance);

                    if (overriddenPrimary != null)
                    {
                        var primaryOverride = (SkillDef)_primaryOverrideField.GetValue(__instance);
                        overriddenPrimary.UnsetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                        _overriddenPrimaryField.SetValue(__instance, null);

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
                Log.Info($"[FindOrCreateBaggedObjectState] Called with targetObject={targetObject?.name ?? "null"}, NetworkServer.active={NetworkServer.active}");
            }

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo && bo.targetObject == targetObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[FindOrCreateBaggedObjectState] Found existing BaggedObject state for {targetObject?.name ?? "null"}");
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
                    var constructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        var newBaggedObject = (BaggedObject)constructor.Invoke(null);
                        newBaggedObject.targetObject = targetObject;
                        // DIAGNOSTIC LOG: Log when we create a new BaggedObject
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[FindOrCreateBaggedObjectState] Creating NEW BaggedObject with targetObject={targetObject?.name ?? "null"}");
                        }
                        targetStateMachine.SetState(newBaggedObject);
                        return newBaggedObject;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in FindOrCreateBaggedObjectState: {ex.Message}");
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
                    if (isInBaggedObjects && baggedObjectsList != null) baggedObjectsList.Remove(passenger);

                    BagCarouselUpdater.UpdateCarousel(bagController);
                    BagCarouselUpdater.UpdateNetworkBagState(bagController);
                    BagPassengerManager.ForceRecalculateMass(bagController);
                    RemoveUIOverlay(passenger, bagController);
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
