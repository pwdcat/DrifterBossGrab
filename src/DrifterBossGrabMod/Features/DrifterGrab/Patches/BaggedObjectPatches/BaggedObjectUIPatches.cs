#nullable enable
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

    public static class BaggedObjectUIPatches
    {

        private static readonly FieldInfo _uiOverlayControllerField = ReflectionCache.BaggedObject.UIOverlayController;
        private static readonly FieldInfo _overriddenUtilityField = ReflectionCache.BaggedObject.OverriddenUtility;
        private static readonly FieldInfo _overriddenPrimaryField = ReflectionCache.BaggedObject.OverriddenPrimary;
        private static readonly FieldInfo _utilityOverrideField = ReflectionCache.BaggedObject.UtilityOverride;
        private static readonly FieldInfo _primaryOverrideField = ReflectionCache.BaggedObject.PrimaryOverride;
        private static readonly PropertyInfo _instancesListProperty = typeof(OverlayController).GetProperty("instancesList", BindingFlags.Public | BindingFlags.Instance);
        private static readonly MethodInfo _onUIOverlayInstanceRemoveMethod = ReflectionCache.Misc.OnUIOverlayInstanceRemove;

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

            if (targetObject == null)
            {
                RemoveUIOverlayForNullState(actualBagController);
                return;
            }

            bool isNowMainSeatOccupant = false;

            var outerSeat = actualBagController.vehicleSeat;
            if (outerSeat != null)
            {
                var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;

                if (outerCurrentPassengerBodyObject != null)
                {
                    isNowMainSeatOccupant = ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                }

            }

            var trackedMainSeatOccupant = BagPatches.GetMainSeatObject(actualBagController);
            if (!isNowMainSeatOccupant && trackedMainSeatOccupant != null)
            {
                isNowMainSeatOccupant = ReferenceEquals(targetObject, trackedMainSeatOccupant);
            }

            bool isInAdditionalSeat = BagHelpers.GetAdditionalSeat(actualBagController, targetObject) != null;
            if (isInAdditionalSeat)
            {
                return;
            }

            BaggedObjectPatches.SynchronizeBaggedObjectState(actualBagController, targetObject);
            return;
        }

        public static void RemoveUIOverlay(GameObject targetObject, DrifterBagController? bagController = null)
        {
            if (targetObject == null)
            {
                return;
            }

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

            if (bagController != null)
            {

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

                var currentlyTracked = BagPatches.GetMainSeatObject(bagController);
                bool isTrackedAsMainSeat = currentlyTracked != null && ReferenceEquals(targetObject, currentlyTracked);

                if (isActuallyInMainSeat || isTrackedAsMainSeat)
                {

                    return;
                }
            }
            else
            {

            }

            var existingController = baggedObject != null ? (OverlayController)_uiOverlayControllerField.GetValue(baggedObject) : null;
            if (existingController != null)
            {

                HudOverlayManager.RemoveOverlay(existingController);
                _uiOverlayControllerField.SetValue(baggedObject, null);
            }
            else
            {

            }
        }

        public static void RemoveUIOverlayForNullState(DrifterBagController bagController)
        {
            if (bagController == null) return;

            BaggedObject? baggedObject = null;

            var stateMachines = bagController!.GetComponentsInChildren<EntityStateMachine>(true);
            foreach (var sm in stateMachines)
            {
                if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                {
                    baggedObject = (BaggedObject)sm.state;
                    break;
                }
            }

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

            if (baggedObject != null)
            {
                var overriddenUtility = (GenericSkill)_overriddenUtilityField.GetValue(baggedObject);
                if (overriddenUtility != null)
                {
                    var utilityOverride = (SkillDef)_utilityOverrideField.GetValue(baggedObject);
                    if (utilityOverride != null)
                    {
                        overriddenUtility.UnsetSkillOverride(baggedObject, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                    }
                    _overriddenUtilityField.SetValue(baggedObject, null);
                }

                var overriddenPrimary = (GenericSkill)_overriddenPrimaryField.GetValue(baggedObject);
                if (overriddenPrimary != null)
                {
                    var primaryOverride = (SkillDef)_primaryOverrideField.GetValue(baggedObject);
                    if (primaryOverride != null)
                    {
                        overriddenPrimary.UnsetSkillOverride(baggedObject, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                    }
                    _overriddenPrimaryField.SetValue(baggedObject, null);
                }
            }

            bool hasTrackedMainSeat = BagPatches.GetMainSeatObject(bagController) != null;

            bool hasActualMainSeatPassenger = false;
            if (bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger)
            {
                hasActualMainSeatPassenger = true;
            }
            if (hasTrackedMainSeat || hasActualMainSeatPassenger)
            {
                return;
            }
            var uiOverlayController = _uiOverlayControllerField != null ? (OverlayController)_uiOverlayControllerField.GetValue(baggedObject) : null;
            if (uiOverlayController != null)
            {
                try
                {

                    var onUIOverlayInstanceRemoveMethod = _onUIOverlayInstanceRemoveMethod;
                    if (onUIOverlayInstanceRemoveMethod != null && _instancesListProperty != null)
                    {
                        try
                        {
                            var instancesList = (IReadOnlyList<GameObject>)_instancesListProperty.GetValue(uiOverlayController);
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
                        catch (Exception ex)
                        {
                            Log.Error($"[RemoveUIOverlayForNullState] Failed to iterate overlay instances: {ex.Message}");
                        }
                    }

                    HudOverlayManager.RemoveOverlay(uiOverlayController);
                    _uiOverlayControllerField?.SetValue(baggedObject, null);
                }
                catch (Exception e)
                {
                    Log.Debug($" [RemoveUIOverlayForNullState] Exception removing overlay: {e.Message}");
                }
            }
        }
    }
}
