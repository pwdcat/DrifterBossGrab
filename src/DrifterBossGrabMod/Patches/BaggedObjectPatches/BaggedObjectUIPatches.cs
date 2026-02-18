using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.HudOverlay;
using RoR2.Skills;
using EntityStates.Drifter.Bag;
using EntityStateMachine = RoR2.EntityStateMachine;
using DrifterBossGrabMod.Core;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    // Helper class for managing UI overlays related to bagged objects - provides methods to refresh, remove, and handle UI overlays for main seat and null states
    public static class BaggedObjectUIPatches
    {
        // Reflection Cache
        private static readonly FieldInfo _uiOverlayControllerField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
        private static readonly FieldInfo _overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
        private static readonly FieldInfo _overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
        private static readonly FieldInfo _utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
        private static readonly FieldInfo _primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");

        // Refreshes the UI overlay for the main seat occupant of a bag controller
        // bagController: The bag controller to refresh the UI for
        // targetObject: The target object to display in the UI
        public static void RefreshUIOverlayForMainSeat(DrifterBagController? bagController, GameObject? targetObject)
        {
            DrifterBagController actualBagController = bagController!;
            if (actualBagController == null && targetObject != null)
            {

                foreach (var controller in BagPatches.GetAllControllers())
                {
                    var msObj = BagPatches.GetMainSeatObject(controller);
                    if (msObj != null && msObj.GetInstanceID() == targetObject.GetInstanceID())
                    {
                        actualBagController = controller;
                        break;
                    }
                }
            }
            if (actualBagController == null)
            {
                return;
            }

            // If targetObject is null
            if (targetObject == null)
            {
                RemoveUIOverlayForNullState(actualBagController);
                return;
            }

            bool isNowMainSeatOccupant = false;
            // Method 1: Check vehicle seat state
            var outerSeat = actualBagController.vehicleSeat;
            if (outerSeat != null)
            {
                var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;

                // Check if targetObject matches the current passenger
                if (outerCurrentPassengerBodyObject != null)
                {
                    isNowMainSeatOccupant = ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                }

            }
            // Method 2: Check tracked main seat state
            var trackedMainSeatOccupant = BagPatches.GetMainSeatObject(actualBagController);
            if (!isNowMainSeatOccupant && trackedMainSeatOccupant != null)
            {
                isNowMainSeatOccupant = ReferenceEquals(targetObject, trackedMainSeatOccupant);
            }

            // Check if the target object is in an additional seat - if so, don't create UI
            bool isInAdditionalSeat = BagHelpers.GetAdditionalSeat(actualBagController, targetObject) != null;
            if (isInAdditionalSeat)
            {
                return;
            }

            // Update BaggedObject state
            // Load stored state if cycling to a previously-bagged object
            if (targetObject != null)
            {
                var storedState = BaggedObjectStateStorage.LoadObjectState(actualBagController, targetObject);
            }

            BaggedObjectPatches.SynchronizeBaggedObjectState(actualBagController, targetObject);
            return;
        }

        // Removes the UI overlay for an object that has left the main seat
        // targetObject: The target object to remove the UI overlay for
        // bagController: Optional bag controller
        public static void RemoveUIOverlay(GameObject targetObject, DrifterBagController? bagController = null)
        {
            if (targetObject == null)
            {
                return;
            }

            // If bagController is not provided, try to find it
            if (bagController == null)
            {
                foreach (var controller in BagPatches.GetAllControllers())
                {
                    if (ReferenceEquals(BagPatches.GetMainSeatObject(controller), targetObject))
                    {
                        bagController = controller;
                        break;
                    }
                }
            }

            BaggedObject? baggedObject = null;
            if (bagController != null)
            {
                baggedObject = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
            }

            if (baggedObject == null)
            {
                return;
            }

            // This prevents premature removal during cycling transitions
            if (bagController != null)
            {
                // Check if object is still in main seat (actual state)
                bool isActuallyInMainSeat = false;
                var outerSeat = bagController.vehicleSeat;
                if (outerSeat != null)
                {
                    var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
                    if (outerCurrentPassengerBodyObject != null)
                    {
                        isActuallyInMainSeat = ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                    }
                }
                // Check if object is tracked as main seat
                var currentlyTracked = BagPatches.GetMainSeatObject(bagController);
                bool isTrackedAsMainSeat = currentlyTracked != null && ReferenceEquals(targetObject, currentlyTracked);

                // Only remove overlay if object is neither actually in main seat nor tracked as main seat
                if (isActuallyInMainSeat || isTrackedAsMainSeat)
                {

                    return; // Don't remove overlay if still in main seat
                }
            }
            else
            {

            }
            // Remove any existing overlay controller
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var existingController = baggedObject != null ? (OverlayController)uiOverlayField.GetValue(baggedObject) : null;
            if (existingController != null)
            {

                HudOverlayManager.RemoveOverlay(existingController);
                uiOverlayField.SetValue(baggedObject, null);
            }
            else
            {

            }
        }

        // Handles UI removal when cycling to null state (main seat becomes empty)
        // bagController: The bag controller to handle null state for
        public static void RemoveUIOverlayForNullState(DrifterBagController bagController)
        {
            if (bagController == null) return;
            // When cycling to null, the BaggedObject state may no longer be active
            // We need to find it in the state machines or use cached instances
            BaggedObject? baggedObject = null;
            // First, try to find active BaggedObject state
            var stateMachines = bagController!.GetComponentsInChildren<EntityStateMachine>(true);
            foreach (var sm in stateMachines)
            {
                if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                {
                    baggedObject = (BaggedObject)sm.state;
                    break;
                }
            }
            // If not found in active states, try to find it in any state machine
            if (baggedObject == null)
            {
                foreach (var sm in stateMachines)
                {
                    if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                    {
                        baggedObject = (BaggedObject)sm.state;
                        break;
                    }
                }
            }
            if (baggedObject == null)
            {
                return;
            }

            // Clear skill overrides since we're in null state
            if (baggedObject != null)
            {
                var overriddenUtility = (GenericSkill)_overriddenUtilityField.GetValue(baggedObject);
                if (overriddenUtility != null)
                {
                    var utilityOverride = (SkillDef)_utilityOverrideField.GetValue(baggedObject);
                    overriddenUtility.UnsetSkillOverride(baggedObject, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                    _overriddenUtilityField.SetValue(baggedObject, null);
                }

                var overriddenPrimary = (GenericSkill)_overriddenPrimaryField.GetValue(baggedObject);
                if (overriddenPrimary != null)
                {
                    var primaryOverride = (SkillDef)_primaryOverrideField.GetValue(baggedObject);
                    overriddenPrimary.UnsetSkillOverride(baggedObject, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                    _overriddenPrimaryField.SetValue(baggedObject, null);
                }
            }
            // Only remove overlay if we're truly transitioning to null state
            // Check if there's actually a tracked main seat occupant before removing
            bool hasTrackedMainSeat = BagPatches.GetMainSeatObject(bagController) != null;
            // Also check if there's actually a passenger in the main seat
            bool hasActualMainSeatPassenger = false;
            if (bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger)
            {
                hasActualMainSeatPassenger = true;
            }
            if (hasTrackedMainSeat || hasActualMainSeatPassenger)
            {
                return; // Don't remove overlay if there's still a tracked main seat or actual passenger
            }
            var uiOverlayController = (OverlayController)_uiOverlayControllerField.GetValue(baggedObject);
            if (uiOverlayController != null)
            {
                try
                {
                    // Get the OnUIOverlayInstanceRemove method
                    var onUIOverlayInstanceRemoveMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceRemove");
                    if (onUIOverlayInstanceRemoveMethod != null)
                    {
                        // Get instancesList property to call OnUIOverlayInstanceRemove for each instance
                        var instancesListProperty = typeof(OverlayController).GetProperty("instancesList", BindingFlags.Public | BindingFlags.Instance);
                        if (instancesListProperty != null)
                        {
                            try
                            {
                                var instancesList = (IReadOnlyList<GameObject>)instancesListProperty.GetValue(uiOverlayController);
                                if (instancesList != null)
                                {
                                    foreach (var instance in instancesList)
                                    {
                                        if (instance != null)
                                        {
                                            onUIOverlayInstanceRemoveMethod.Invoke(baggedObject, new object[] { uiOverlayController, instance });
                                        }
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                    // Remove the overlay from HudOverlayManager
                    HudOverlayManager.RemoveOverlay(uiOverlayController);
                    _uiOverlayControllerField.SetValue(baggedObject, null);
                }
                catch (Exception e)
                {
                    Log.Info($" [RemoveUIOverlayForNullState] Exception removing overlay: {e.Message}");
                }
            }
        }
    }
}
