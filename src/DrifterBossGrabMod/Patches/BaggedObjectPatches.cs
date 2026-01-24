using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using RoR2.HudOverlay;
using RoR2.UI;
using UnityEngine;
using EntityStates;
using EntityStates.Drifter.Bag;
using EntityStateMachine = RoR2.EntityStateMachine;
namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch]
    public static class BaggedObjectPatches
    {
        private static readonly Dictionary<DrifterBagController, BaggedObject> _baggedObjectCache = new Dictionary<DrifterBagController, BaggedObject>();
        private static readonly Dictionary<DrifterBagController, string> _lastStateCache = new Dictionary<DrifterBagController, string>();
        private static readonly Dictionary<DrifterBagController, bool> _stateValidationCache = new Dictionary<DrifterBagController, bool>();
        private static readonly Dictionary<DrifterBagController, int> _stateAccessCount = new Dictionary<DrifterBagController, int>();
        [HarmonyPrepare]
        public static void Prepare()
        {
        }
        public static void RefreshUIOverlayForMainSeat(DrifterBagController bagController, GameObject targetObject)
        {
            // Allow targetObject == null to clear the state
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] Called for targetObject: {targetObject?.name ?? "null"}, bagController: {bagController?.name ?? "null"}");
                Log.Info($" [RefreshUIOverlayForMainSeat] Cache states - mainSeatDict count: {BagPatches.mainSeatDict.Count}, additionalSeatsDict count: {BagPatches.additionalSeatsDict.Count}");
                foreach (var kvp in BagPatches.mainSeatDict)
                {
                    try
                    {
                        string keyName = (kvp.Key != null && kvp.Key.gameObject != null) ? kvp.Key.name : "destroyed/null";
                        string valueName = (kvp.Value != null && kvp.Value.gameObject != null) ? kvp.Value.name : "destroyed/null";
                        Log.Info($" [RefreshUIOverlayForMainSeat] mainSeatDict: {keyName} -> {valueName}");
                    }
                    catch
                    {
                        Log.Info($" [RefreshUIOverlayForMainSeat] mainSeatDict: <error accessing names>");
                    }
                }
            }
            // Clean up destroyed objects from mainSeatDict
            var keysToRemove = new List<DrifterBagController>();
            foreach (var kvp in BagPatches.mainSeatDict)
            {
                if (kvp.Value == null || (kvp.Value.gameObject == null))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                BagPatches.mainSeatDict.TryRemove(key, out _);
            }
            DrifterBagController actualBagController = bagController!;
            if (actualBagController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] bagController is null, searching in mainSeatDict for targetObject");
                }
                foreach (var kvp in BagPatches.mainSeatDict)
                {
                    if (kvp.Value != null && kvp.Value.GetInstanceID() == targetObject!.GetInstanceID())
                    {
                        actualBagController = kvp.Key;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [RefreshUIOverlayForMainSeat] Found actualBagController: {actualBagController?.name ?? "null"}");
                        }
                        break;
                    }
                }
            }
            if (actualBagController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] EARLY RETURN: actualBagController is null, cannot proceed");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] actualBagController: {actualBagController?.name ?? "null"}");
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] Checking seat state for targetObject: {targetObject?.name ?? "null"}");
            }
            bool isNowMainSeatOccupant = false;
            // Method 1: Check vehicle seat state
            var outerSeat = actualBagController.vehicleSeat;
            if (outerSeat != null)
            {
                var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] outerSeat: {outerSeat?.name ?? "null"}");
                    Log.Info($" [RefreshUIOverlayForMainSeat] outerCurrentPassengerBodyObject: {outerCurrentPassengerBodyObject?.name ?? "null"}");
                }
                // Check if targetObject matches the current passenger
                if (outerCurrentPassengerBodyObject != null)
                {
                    isNowMainSeatOccupant = ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] isNowMainSeatOccupant from seat: {isNowMainSeatOccupant}");
                }
            }
            // Method 2: Check tracked main seat state
            // This is during cycling transitions
            if (!isNowMainSeatOccupant && BagPatches.mainSeatDict.TryGetValue(actualBagController, out var trackedMainSeatOccupant))
            {
                isNowMainSeatOccupant = ReferenceEquals(targetObject, trackedMainSeatOccupant);
                if (PluginConfig.Instance.EnableDebugLogs.Value && isNowMainSeatOccupant)
                {
                    Log.Info(" [RefreshUIOverlayForMainSeat] Using tracked main seat state");
                }
            }
            // If still not main seat occupant but client has authority, assume it's the main seat (for grabbing client)
            if (!isNowMainSeatOccupant && actualBagController.hasAuthority)
            {
                isNowMainSeatOccupant = true;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info(" [RefreshUIOverlayForMainSeat] Assuming main seat because client has authority");
                }
            }
            // Check if the target object is in an additional seat - if so, don't create UI
            bool isInAdditionalSeat = BagPatches.GetAdditionalSeat(actualBagController, targetObject) != null;
            if (isInAdditionalSeat)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] EARLY RETURN: Target is in additional seat, not creating UI");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] Final isNowMainSeatOccupant: {isNowMainSeatOccupant}");
            }
            // Update BaggedObject state even though we skip overlay creation since carousel handles UI now
            BaggedObject? baggedObject = FindOrCreateBaggedObjectState(actualBagController, targetObject);
            if (baggedObject != null)
            {
                // Check if targetObject has changed
                var currentTargetObject = baggedObject.targetObject;
                bool targetObjectChanged = !ReferenceEquals(currentTargetObject, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value && targetObjectChanged)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] targetObject changed from {currentTargetObject?.name ?? "null"} to {targetObject?.name ?? "null"}");
                }
                // Update the targetObject field of BaggedObject to point to the new target
                baggedObject.targetObject = targetObject;
                // Update targetBody and vehiclePassengerAttributes for the new target first
                var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
                var isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
                var vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
                var baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
                HealthComponent healthComponent;
                if (targetObject.TryGetComponent<HealthComponent>(out healthComponent))
                {
                    targetBodyField.SetValue(baggedObject, healthComponent.body);
                    isBodyField.SetValue(baggedObject, true);
                    vehiclePassengerAttributesField.SetValue(baggedObject, targetObject.GetComponent<SpecialObjectAttributes>());
                }
                else
                {
                    targetBodyField.SetValue(baggedObject, null);
                    isBodyField.SetValue(baggedObject, false);
                }
                var specialObjectAttributes = targetObject.GetComponent<SpecialObjectAttributes>();
                if (specialObjectAttributes != null)
                {
                    vehiclePassengerAttributesField.SetValue(baggedObject, specialObjectAttributes);
                }
                // Recalculate bagged mass for the new target
                if (bagController != null)
                {
                    float newMass;
                    if (bagController == targetObject)
                    {
                        newMass = bagController.baggedMass;
                    }
                    else
                    {
                        newMass = bagController.CalculateBaggedObjectMass(targetObject);
                    }
                    baggedMassField.SetValue(baggedObject, newMass);
                }
                // Force a skill override check by calling TryOverrideUtility and TryOverridePrimary with the respective skills
                var skillLocator = baggedObject.outer.GetComponent<SkillLocator>();
                if (skillLocator != null)
                {
                    if (skillLocator.utility != null)
                    {
                        var tryOverrideUtilityMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
                        if (tryOverrideUtilityMethod != null)
                        {
                            tryOverrideUtilityMethod.Invoke(baggedObject, new object[] { skillLocator.utility });
                        }
                    }
                    if (skillLocator.primary != null)
                    {
                        var tryOverridePrimaryMethod = AccessTools.Method(typeof(BaggedObject), "TryOverridePrimary");
                        if (tryOverridePrimaryMethod != null)
                        {
                            tryOverridePrimaryMethod.Invoke(baggedObject, new object[] { skillLocator.primary });
                        }
                    }
                }
                // Refresh UI overlay if targetObject changed
                if (targetObjectChanged)
                {
                    RefreshUIOverlay(baggedObject);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [RefreshUIOverlayForMainSeat] Refreshed UI overlay due to targetObject change");
                    }
                }
            }
            return;
        }
        private static void UpdateTargetFields(BaggedObject? baggedObject, GameObject targetObject, DrifterBagController bagController)
        {
            var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
            var isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
            var vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
            var baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
            HealthComponent healthComponent;
            if (targetObject.TryGetComponent<HealthComponent>(out healthComponent))
            {
                targetBodyField.SetValue(baggedObject!, healthComponent.body);
                isBodyField.SetValue(baggedObject!, true);
                vehiclePassengerAttributesField.SetValue(baggedObject!, targetObject.GetComponent<SpecialObjectAttributes>());
            }
            else
            {
                targetBodyField.SetValue(baggedObject!, null);
                isBodyField.SetValue(baggedObject!, false);
            }
            var specialObjectAttributes = targetObject.GetComponent<SpecialObjectAttributes>();
            if (specialObjectAttributes != null)
            {
                vehiclePassengerAttributesField.SetValue(baggedObject!, specialObjectAttributes);
            }
            else
            {
            }
            // Recalculate bagged mass for the new target
            if (bagController != null)
            {
                float newMass;
                if (bagController == targetObject)
                {
                    newMass = bagController.baggedMass;
                }
                else
                {
                    newMass = bagController.CalculateBaggedObjectMass(targetObject);
                }
                baggedMassField.SetValue(baggedObject!, newMass);
            }
        }
        // Remove the UI overlay for an object that has left the main seat.
        public static void RemoveUIOverlay(GameObject targetObject)
        {
            if (targetObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] EXIT: targetObject is null");
                }
                return;
            }
            var baggedObject = targetObject.GetComponent<BaggedObject>();
            if (baggedObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] EXIT: no BaggedObject component on {targetObject.name}");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RemoveUIOverlay] Called for {targetObject.name}");
            }
            // This prevents premature removal during cycling transitions
            var bagController = baggedObject!.outer.GetComponent<DrifterBagController>();
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
                bool isTrackedAsMainSeat = BagPatches.mainSeatDict.TryGetValue(bagController, out var currentlyTracked) &&
                                          ReferenceEquals(targetObject, currentlyTracked);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] isActuallyInMainSeat: {isActuallyInMainSeat}, isTrackedAsMainSeat: {isTrackedAsMainSeat}");
                }
                // Only remove overlay if object is neither actually in main seat nor tracked as main seat
                if (isActuallyInMainSeat || isTrackedAsMainSeat)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [RemoveUIOverlay] NOT removing overlay - object still in main seat or tracked");
                    }
                    return; // Don't remove overlay if still in main seat
                }
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] No bagController found");
                }
            }
            // Remove any existing overlay controller
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var existingController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            if (existingController != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] Removing existing overlay controller");
                }
                HudOverlayManager.RemoveOverlay(existingController);
                uiOverlayField.SetValue(baggedObject, null);
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] No existing overlay controller to remove");
                }
            }
        }
        // Helper method to dump all BaggedObject fields for debugging
        public static void DumpBaggedObjectFields(BaggedObject baggedObject, string context)
        {
            if (baggedObject == null || !PluginConfig.Instance.EnableDebugLogs.Value) return;

            Log.Info($" [DUMP] {context} - Dumping all BaggedObject fields:");
            try
            {
                var fields = typeof(BaggedObject).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(baggedObject);
                        string valueStr = value != null ? value.ToString() : "null";
                        if (value is UnityEngine.Object unityObj && unityObj != null)
                        {
                            valueStr = $"{unityObj.name} ({unityObj.GetType().Name})";
                        }
                        Log.Info($"    {field.Name}: {valueStr}");
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"    {field.Name}: <error getting value: {ex.Message}>");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info($" [DUMP] Error dumping fields: {ex.Message}");
            }
        }

        // Handle UI removal when cycling to null state (main seat becomes empty)
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
            // If not found in active states, try to get from cache
            if (baggedObject == null && _baggedObjectCache.TryGetValue(bagController, out var cachedBaggedObject))
            {
                baggedObject = cachedBaggedObject;
            }
            // If still not found, try to find it in any state machine (including inactive)
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
            // This handles the case where we need to clean up UI state even when the state machine is missing
            if (baggedObject == null)
            {
                baggedObject = ForceCreateBaggedObjectState(bagController!, null!);
            }
            if (baggedObject == null)
            {
                // Clear the cache since we can't find the BaggedObject state
                _baggedObjectCache.Remove(bagController);
                return;
            }
            DumpBaggedObjectFields(baggedObject, "RemoveUIOverlayForNullState");
            var targetObjectField = AccessTools.Field(typeof(BaggedObject), "targetObject");
            var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
            var vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
            var baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            // Clear skill overrides since we're in null state
            if (baggedObject != null)
            {
                var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                var overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
                var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
                var primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");

                var overriddenUtility = (GenericSkill)overriddenUtilityField.GetValue(baggedObject);
                if (overriddenUtility != null)
                {
                    var utilityOverride = (SkillDef)utilityOverrideField.GetValue(baggedObject);
                    overriddenUtility.UnsetSkillOverride(baggedObject, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                    overriddenUtilityField.SetValue(baggedObject, null);
                }

                var overriddenPrimary = (GenericSkill)overriddenPrimaryField.GetValue(baggedObject);
                if (overriddenPrimary != null)
                {
                    var primaryOverride = (SkillDef)primaryOverrideField.GetValue(baggedObject);
                    overriddenPrimary.UnsetSkillOverride(baggedObject, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                    overriddenPrimaryField.SetValue(baggedObject, null);
                }
            }
            // Only remove overlay if we're truly transitioning to null state
            // Check if there's actually a tracked main seat occupant before removing
            bool hasTrackedMainSeat = BagPatches.mainSeatDict.TryGetValue(bagController, out var trackedMainSeat) && trackedMainSeat != null;
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
            var uiOverlayController = (OverlayController)uiOverlayField.GetValue(baggedObject);
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
                    uiOverlayField.SetValue(baggedObject, null);
                }
                catch (Exception e)
                {
                    Log.Info($" [RemoveUIOverlayForNullState] Exception removing overlay: {e.Message}");
                }
            }
            // Set targetObject to null for null state
            baggedObject.targetObject = null;
            targetBodyField.SetValue(baggedObject, null);
            vehiclePassengerAttributesField.SetValue(baggedObject, null);
            baggedMassField.SetValue(baggedObject, 0f);
            // This ensures that when we cycle back, we'll properly detect and create the BaggedObject state
            _baggedObjectCache.Remove(bagController);
        }
        // Helper method to check if an object is in the main seat
        private static bool IsInMainSeat(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return false;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] Checking for bagController: {bagController?.name ?? "null"}, targetObject: {targetObject?.name ?? "null"}");
            }
            // Check tracked main seat first (more reliable for mod logic)
            var trackedMainSeat = BagPatches.GetMainSeatObject(bagController!);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] trackedMainSeat: {trackedMainSeat?.name ?? "null"}");
            }
            if (trackedMainSeat != null && ReferenceEquals(targetObject, trackedMainSeat))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [IsInMainSeat] Returning true from tracked main seat");
                }
                return true;
            }
            // Fallback to vehicle seat check
            var outerSeat = bagController.vehicleSeat;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] outerSeat: {outerSeat?.name ?? "null"}");
            }
            if (outerSeat == null) return false;
            // Use NetworkpassengerBodyObject (works for all objects)
            var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] outerCurrentPassengerBodyObject: {outerCurrentPassengerBodyObject?.name ?? "null"}");
            }
            bool result = outerCurrentPassengerBodyObject != null && ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] Returning {result} from vehicle seat check");
            }
            // If still not in main seat but client has authority, assume it's the main seat (for grabbing client)
            if (!result && bagController.hasAuthority)
            {
                result = true;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info(" [IsInMainSeat] Assuming main seat because client has authority");
                }
            }
            // But if it's in an additional seat, don't consider it main seat
            bool isInAdditional = BagPatches.GetAdditionalSeat(bagController, targetObject) != null;
            if (isInAdditional)
            {
                result = false;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info(" [IsInMainSeat] But target is in additional seat, so not main seat");
                }
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] Final result: {result}");
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverrideUtility] No bagController found, allowing normal execution");
                    }
                    return true; // Allow normal execution
                }
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [TryOverrideUtility] Attempting override for {targetObject?.name ?? "null"}, isMainSeatOccupant: {isMainSeatOccupant}, skill: {skill?.skillName ?? "null"}");
                }
                // Only allow if the object is in the main seat
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverrideUtility] ALLOWING skill override for {targetObject?.name} (main seat: {isMainSeatOccupant})");
                    }
                    return true; // Allow normal execution
                }
                else
                {
                    // If not in main seat, unset the override
                    var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                    var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
                    var overriddenUtility = (GenericSkill)overriddenUtilityField.GetValue(__instance);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverrideUtility] BLOCKING override - unsetting existing override for {targetObject?.name} (not in main seat), overriddenUtility: {overriddenUtility?.skillName ?? "null"}");
                        // Log current state details
                        Log.Info($" [TryOverrideUtility] Current state - tracked main seat: {BagPatches.GetMainSeatObject(bagController)?.name ?? "null"}, vehicle passenger: {bagController?.vehicleSeat?.NetworkpassengerBodyObject?.name ?? "null"}, hasAuthority: {bagController?.hasAuthority ?? false}");
                    }
                    if (overriddenUtility != null)
                    {
                        var utilityOverride = (SkillDef)utilityOverrideField.GetValue(__instance);
                        overriddenUtility.UnsetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                        overriddenUtilityField.SetValue(__instance, null);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [TryOverrideUtility] Unset skill override for {targetObject?.name} (not in main seat)");
                        }
                    }
                    return false; // Skip the original method - no skill override
                }
            }
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, GenericSkill skill)
            {
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverridePrimary] No bagController found, allowing normal execution");
                    }
                    return true; // Allow normal execution
                }
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [TryOverridePrimary] Attempting override for {targetObject?.name ?? "null"}, isMainSeatOccupant: {isMainSeatOccupant}, skill: {skill?.skillName ?? "null"}");
                }
                // Only allow if the object is in the main seat
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverridePrimary] ALLOWING skill override for {targetObject?.name} (main seat: {isMainSeatOccupant})");
                    }
                    return true; // Allow normal execution
                }
                else
                {
                    // If not in main seat, unset the override
                    var overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
                    var primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");
                    var overriddenPrimary = (GenericSkill)overriddenPrimaryField.GetValue(__instance);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverridePrimary] BLOCKING override - unsetting existing override for {targetObject?.name} (not in main seat), overriddenPrimary: {overriddenPrimary?.skillName ?? "null"}");
                        // Log current state details
                        Log.Info($" [TryOverridePrimary] Current state - tracked main seat: {BagPatches.GetMainSeatObject(bagController)?.name ?? "null"}, vehicle passenger: {bagController?.vehicleSeat?.NetworkpassengerBodyObject?.name ?? "null"}, hasAuthority: {bagController?.hasAuthority ?? false}");
                    }
                    if (overriddenPrimary != null)
                    {
                        var primaryOverride = (SkillDef)primaryOverrideField.GetValue(__instance);
                        overriddenPrimary.UnsetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                        overriddenPrimaryField.SetValue(__instance, null);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [TryOverridePrimary] Unset skill override for {targetObject?.name} (not in main seat)");
                        }
                    }
                    return false; // Skip the original method - no skill override
                }
            }
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, GenericSkill skill)
            {
            }
        }
        [HarmonyPatch(typeof(BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter] Called for targetObject: {__instance?.targetObject?.name ?? "null"}");
                }
                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter] No DrifterBagController found, proceeding normally");
                    }
                    return true;
                }
                var targetObject = __instance.targetObject;
                if (targetObject == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter] targetObject is null, proceeding normally");
                    }
                    return true;
                }
                // Check if targetObject is in additional seat
                if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict) && seatDict.TryGetValue(targetObject, out var additionalSeat))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter] targetObject is in additional seat, assigning to additional seat instead");
                    }
                    // Assign to additional seat instead of main
                    additionalSeat.AssignPassenger(targetObject);
                    // Don't call the original OnEnter logic
                    return false;
                }
                // Check if targetObject is already in the main seat
                var outerMainSeat = bagController.vehicleSeat;
                if (outerMainSeat != null && outerMainSeat.hasPassenger && ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter] targetObject already in main seat, skipping original OnEnter");
                    }
                    // Don't call the original OnEnter logic to avoid double assignment
                    return false;
                }
                // Otherwise, proceed normally
                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] Starting for {__instance?.targetObject?.name ?? "null"}");
                }
                // Check if the main seat has the targetObject as passenger
                // If not, remove the UI overlay to prevent incorrect display
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] bagController: {bagController?.name ?? "null"}");
                }
                if (bagController == null) return;
                var targetObject = __instance.targetObject;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] targetObject: {targetObject?.name ?? "null"}");
                }
                if (targetObject == null) return;
                var outerMainSeat = bagController.vehicleSeat;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] outerMainSeat: {outerMainSeat?.name ?? "null"}");
                    if (outerMainSeat != null)
                    {
                        Log.Info($" [BaggedObject_OnEnter_Postfix] outerMainSeat.hasPassenger: {outerMainSeat.hasPassenger}");
                        Log.Info($" [BaggedObject_OnEnter_Postfix] outerMainSeat.NetworkpassengerBodyObject: {outerMainSeat.NetworkpassengerBodyObject?.name ?? "null"}");
                        Log.Info($" [BaggedObject_OnEnter_Postfix] ReferenceEquals check: {!ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject)}");
                    }
                }
                bool seatHasTarget = outerMainSeat != null && outerMainSeat.hasPassenger && ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject);
                bool trackedHasTarget = BagPatches.mainSeatDict.TryGetValue(bagController, out var tracked) && ReferenceEquals(tracked, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] seatHasTarget: {seatHasTarget}, trackedHasTarget: {trackedHasTarget}");
                }
                if (!seatHasTarget && !trackedHasTarget)
                {
                    // Neither seat nor tracked has targetObject, remove the UI
                    // But if the client has authority over the bag controller, keep the UI
                    bool hasAuthority = bagController != null && bagController.hasAuthority;
                    if (!hasAuthority)
                    {
                        var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                        var uiOverlayController = (OverlayController)uiOverlayField.GetValue(__instance);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject_OnEnter_Postfix] uiOverlayController exists: {uiOverlayController != null}");
                        }
                        if (uiOverlayController != null)
                        {
                            HudOverlayManager.RemoveOverlay(uiOverlayController);
                            uiOverlayField.SetValue(__instance, null);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [BaggedObject_OnEnter_Postfix] Removed UI overlay because neither seat nor tracked has targetObject");
                            }
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject_OnEnter_Postfix] Keeping UI overlay because client has authority over bag controller");
                        }
                    }
                }
                // Add to baggedObjectsDict for skill grabs
                if (bagController != null && targetObject != null)
                {
                    if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                    {
                        list = new List<GameObject>();
                        BagPatches.baggedObjectsDict[bagController] = list;
                    }
                    if (!list.Contains(targetObject))
                    {
                        list.Add(targetObject);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject_OnEnter_Postfix] Added {targetObject.name} to baggedObjectsDict");
                        }
                    }
                    BagPatches.UpdateCarousel(bagController);
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter_Postfix] Seat or tracked has targetObject, not removing UI");
                    }
                    // Ensure UI is created/refreshed for main seat objects
                    RefreshUIOverlayForMainSeat(bagController, targetObject);
                }
                // Always remove the overlay to use carousel instead
                var uiOverlayField2 = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                var uiOverlayController2 = (OverlayController)uiOverlayField2.GetValue(__instance);
                if (uiOverlayController2 != null)
                {
                    HudOverlayManager.RemoveOverlay(uiOverlayController2);
                    uiOverlayField2.SetValue(__instance, null);
                }
            }
        }
        [HarmonyPatch(typeof(BaggedObject), "OnUIOverlayInstanceAdded")]
        public class BaggedObject_OnUIOverlayInstanceAdded
        {
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, OverlayController controller, GameObject instance)
            {
                // Get the DrifterBagController
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    return;
                }
                var targetObject = __instance.targetObject;
                // The issue is that when cycling, the vehicle seat state might not be updated yet
                // We need to check both the current state and the tracked state
                bool isMainSeatOccupant = false;
                // Method 1: Check vehicle seat state (current state)
                var outerSeat = bagController.vehicleSeat;
                if (outerSeat != null)
                {
                    var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
                    // Check if targetObject matches the current passenger
                    if (outerCurrentPassengerBodyObject != null)
                    {
                        isMainSeatOccupant = ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                    }
                }
                // Method 2: Check tracked main seat state (cached state)
                // This is more reliable during cycling transitions
                if (!isMainSeatOccupant && BagPatches.mainSeatDict.TryGetValue(bagController, out var trackedMainSeatOccupant))
                {
                    isMainSeatOccupant = ReferenceEquals(targetObject, trackedMainSeatOccupant);
                    if (PluginConfig.Instance.EnableDebugLogs.Value && isMainSeatOccupant)
                    {
                        Log.Info($" [OnUIOverlayInstanceAdded] Using tracked main seat state: {trackedMainSeatOccupant?.name}");
                    }
                }
                // Allow if the client has authority over the bag controller (for grabbing)
                bool hasAuthority = bagController.hasAuthority;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [OnUIOverlayInstanceAdded] isMainSeatOccupant: {isMainSeatOccupant}, hasAuthority: {hasAuthority}");
                }
                if (!isMainSeatOccupant && !hasAuthority)
                {
                    bool isCurrentlyTracked = BagPatches.mainSeatDict.TryGetValue(bagController, out var currentlyTracked) &&
                                            ReferenceEquals(targetObject, currentlyTracked);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [OnUIOverlayInstanceAdded] isCurrentlyTracked: {isCurrentlyTracked}, currentlyTracked: {currentlyTracked?.name ?? "null"}");
                    }
                    if (!isCurrentlyTracked)
                    {
                        // Only remove overlay if this object is not currently tracked as main seat and client doesn't have authority
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [OnUIOverlayInstanceAdded] REMOVING overlay for {targetObject?.name ?? "null"} - not main seat and no authority");
                        }
                        if (controller != null)
                        {
                            HudOverlayManager.RemoveOverlay(controller);
                            // Null out the field to prevent OnExit from trying to remove again
                            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                            uiOverlayField.SetValue(__instance, null);
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [OnUIOverlayInstanceAdded] NOT removing overlay - object is currently tracked as main seat");
                        }
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [OnUIOverlayInstanceAdded] NOT removing overlay - isMainSeatOccupant or hasAuthority");
                    }
                }
            // If using bottomless bag (has additional seats), remove the overlay to prevent overlapping with carousel
            if (bagController != null && BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict) && seatDict.Count > 0)
            {
                HudOverlayManager.RemoveOverlay(controller);
                var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                uiOverlayField.SetValue(__instance, null);
            }
            }
        }
        [HarmonyPatch(typeof(BaggedObject), "OnExit")]
        public class BaggedObject_OnExit
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (__instance == null || __instance.targetObject == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] Skipping original OnExit - __instance or targetObject is null");
                    }
                    return false; // Skip original OnExit to prevent NRE
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnExit] Called for targetObject: {__instance.targetObject.name}");
                    var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        Log.Info($" [BaggedObject_OnExit] bagController: {bagController.name}, hasAuthority: {bagController.hasAuthority}");
                        Log.Info($" [BaggedObject_OnExit] vehicleSeat hasPassenger: {bagController.vehicleSeat?.hasPassenger ?? false}");
                        Log.Info($" [BaggedObject_OnExit] vehicleSeat passenger: {bagController.vehicleSeat?.NetworkpassengerBodyObject?.name ?? "null"}");
                        bool isTracked = BagPatches.mainSeatDict.TryGetValue(bagController, out var tracked) && ReferenceEquals(__instance.targetObject, tracked);
                        Log.Info($" [BaggedObject_OnExit] isTracked as main seat: {isTracked}, tracked: {tracked?.name ?? "null"}");
                        // Check if object is being destroyed
                        bool isBeingDestroyed = __instance.targetObject.GetComponent<HealthComponent>()?.alive == false;
                        Log.Info($" [BaggedObject_OnExit] targetObject dead: {isBeingDestroyed}");
                        // Check if it's in additional seats
                        bool inAdditionalSeat = BagPatches.GetAdditionalSeat(bagController, __instance.targetObject) != null;
                        Log.Info($" [BaggedObject_OnExit] inAdditionalSeat: {inAdditionalSeat}");
                    }
                }
                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                var bagController = __instance?.outer?.GetComponent<DrifterBagController>();
                if (bagController == null || __instance.targetObject == null) return;

                // Check if this object was the main seat occupant and is not in an additional seat
                bool isTrackedAsMain = BagPatches.mainSeatDict.TryGetValue(bagController, out var tracked) && ReferenceEquals(__instance.targetObject, tracked);
                bool inAdditionalSeat = BagPatches.GetAdditionalSeat(bagController, __instance.targetObject) != null;

                // Check if the object is still actually in a seat (main or additional)
                bool stillInMainSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger &&
                                       ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, __instance.targetObject);
                bool stillInAnySeat = stillInMainSeat || inAdditionalSeat;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnExit_Postfix] isTrackedAsMain: {isTrackedAsMain}, inAdditionalSeat: {inAdditionalSeat}, stillInMainSeat: {stillInMainSeat}, stillInAnySeat: {stillInAnySeat}");
                }

                // Only remove from bag if it was the main seat occupant, not moved to additional seat, and not still in any seat
                // But if the client has authority over the bag controller, don't remove
                bool hasAuthority = bagController != null && bagController.hasAuthority;
                if (isTrackedAsMain && !inAdditionalSeat && !stillInAnySeat && !hasAuthority)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Removing {__instance.targetObject.name} from bag due to exit from main seat");
                    }
                    BagPatches.RemoveBaggedObject(bagController, __instance.targetObject);
                }
                else if (hasAuthority)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Not removing {__instance.targetObject.name} from bag because client has authority");
                    }
                }
                else if (stillInAnySeat)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Object {__instance.targetObject.name} is still in a seat, not removing from bag");
                    }
                    // Update carousel since the object is still bagged
                    BagPatches.UpdateCarousel(bagController);
                }
            }
        }
        [HarmonyPatch(typeof(BaggedObject), "FixedUpdate")]
        public class BaggedObject_FixedUpdate
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (__instance == null || __instance.targetObject == null)
                {
                    return false; // Skip original FixedUpdate to prevent NRE
                }
                var isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
                var isBody = (bool?)isBodyField?.GetValue(__instance);
                if (isBody == false)
                {
                    return false;
                }

                return true;
            }
        }
        public static GameObject? GetMainSeatOccupant(DrifterBagController controller)
        {
            if (controller == null || controller.vehicleSeat == null) return null;
            if (!controller.vehicleSeat.hasPassenger) return null;
            return controller.vehicleSeat.currentPassengerBody?.gameObject;
        }
        public static void RefreshUIOverlay(BaggedObject baggedObject)
        {
            if (baggedObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info(" [RefreshUIOverlay] EXIT: baggedObject is null");
                }
                return;
            }
            var targetObject = baggedObject.targetObject;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlay] Called for targetObject: {targetObject?.name ?? "null"}");
            }
            // Get the uiOverlayController field using reflection
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var uiOverlayController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            if (uiOverlayController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info(" [RefreshUIOverlay] EXIT: uiOverlayController is null, cannot instantiate new UI");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info(" [RefreshUIOverlay] uiOverlayController found, updating instances");
            }
            // Update targetBody and vehiclePassengerAttributes for the new target
            var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
            var vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
            var baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
            var onUIOverlayInstanceAddedMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceAdded");
            var instancesListProperty = typeof(OverlayController).GetProperty("instancesList", BindingFlags.Public | BindingFlags.Instance);
            if (instancesListProperty != null)
            {
                try
                {
                    var instancesList = (IReadOnlyList<GameObject>)instancesListProperty.GetValue(uiOverlayController);
                    if (instancesList != null)
                    {
                        if (targetObject != null)
                        {
                            var bagController = baggedObject.outer.GetComponent<DrifterBagController>();
                            bool isCurrentlyTracked = bagController != null && BagPatches.mainSeatDict.TryGetValue(bagController, out var currentlyTracked) &&
                                                    ReferenceEquals(targetObject, currentlyTracked);
                            bool hasAuthority = bagController != null && bagController.hasAuthority;
                            if (isCurrentlyTracked || hasAuthority)
                            {
                                onUIOverlayInstanceAddedMethod.Invoke(baggedObject, new object[] { uiOverlayController, targetObject });
                            }
                            else
                            {
                                RemoveUIOverlay(targetObject);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        public static void LogUIOverlayState(string context, DrifterBagController bagController, GameObject targetObject)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
            Log.Info(" [DEBUG] " + context + " UI Overlay State Analysis");
            Log.Info(" [DEBUG]   context: " + context);
            Log.Info(" [DEBUG]   bagController: " + bagController);
            if (bagController != null)
            {
            }
        }
        // Cleanup method to clear cached BaggedObject instances
        public static void ClearBaggedObjectCache()
        {
            _baggedObjectCache.Clear();
            _lastStateCache.Clear();
        }
        // Method to validate and update state machine status
        public static void ValidateStateMachineStatus(DrifterBagController bagController)
        {
            if (bagController == null) return;
            var stateMachines = bagController.GetComponentsInChildren<EntityStateMachine>(true);
            string currentState = "Unknown";
            foreach (var sm in stateMachines)
            {
                if (sm.state != null)
                {
                    currentState = sm.state.GetType().Name;
                    break;
                }
            }
            // Update last state cache
            if (_lastStateCache.TryGetValue(bagController, out var lastState))
            {
                if (lastState != currentState)
                {
                }
            }
            _lastStateCache[bagController] = currentState;
        }
        [HarmonyPatch(typeof(RoR2.VehicleSeat), "EjectPassenger", new Type[] { typeof(GameObject) })]
        public class VehicleSeat_EjectPassenger
        {
            [HarmonyPostfix]
            public static void Postfix(RoR2.VehicleSeat __instance, GameObject bodyObject)
            {
                if (bodyObject == null) return;
                var bagController = __instance.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                }
            }
        }
        [HarmonyPatch(typeof(RoR2.EntityStateMachine), "SetNextStateToMain")]
        public class EntityStateMachine_SetNextStateToMain
        {
            [HarmonyPrefix]
            public static bool Prefix(RoR2.EntityStateMachine __instance)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string customName = __instance?.customName ?? "null";
                    string gameObjectName = __instance?.gameObject?.name ?? "null";
                    string stateTypeName = __instance?.mainStateType.stateType?.Name ?? "null";
                    Log.Info($" [SetNextStateToMain] Called on {customName}, gameObject: {gameObjectName}, mainStateType: {stateTypeName}");
                    if (__instance != null && __instance.mainStateType.stateType == null)
                    {
                        Log.Info($" [SetNextStateToMain] mainStateType.stateType is null!");
                    }
                }
                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(RoR2.EntityStateMachine __instance)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string customName = __instance?.customName ?? "null";
                    Log.Info($" [SetNextStateToMain] Completed on {customName}");
                }
            }
        }
        // This provides reliable state detection and creation with proper caching
        private static BaggedObject FindOrCreateBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] EXIT: bagController or targetObject is null");
                }
                return null;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                string bagControllerName = "null";
                try { bagControllerName = bagController?.name ?? "null"; } catch { bagControllerName = "destroyed"; }
                string targetObjectName = "null";
                try { targetObjectName = targetObject?.name ?? "null"; } catch { targetObjectName = "destroyed"; }
                Log.Info($" [FindOrCreateBaggedObjectState] Called for bagController: {bagControllerName}, targetObject: {targetObjectName}");
                // Log current cached targetObject if exists
                if (_baggedObjectCache.TryGetValue(bagController, out var existingCached))
                {
                    var cachedTarget = existingCached.targetObject;
                    string cachedTargetName = "null";
                    try { cachedTargetName = cachedTarget?.name ?? "null"; } catch { cachedTargetName = "destroyed"; }
                    Log.Info($" [FindOrCreateBaggedObjectState] Cached BaggedObject targetObject: {cachedTargetName}");
                }
            }
            // 1. Check cache first
            if (_baggedObjectCache.TryGetValue(bagController, out var cachedBaggedObject))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] Cache hit: cachedBaggedObject exists");
                }
                // Check if cached BaggedObject's targetObject matches the requested targetObject
                var cachedTarget = cachedBaggedObject.targetObject;
                bool isCachedTargetValid = false;
                string cachedTargetName = "null";
                try
                {
                    if (cachedTarget != null)
                    {
                        cachedTargetName = cachedTarget.name;
                        isCachedTargetValid = true;
                    }
                }
                catch
                {
                    cachedTargetName = "destroyed";
                    isCachedTargetValid = false;
                }
                string targetObjectName = "null";
                try { targetObjectName = targetObject?.name ?? "null"; } catch { targetObjectName = "destroyed"; }
                if (!isCachedTargetValid || !ReferenceEquals(cachedTarget, targetObject))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] Cached targetObject {cachedTargetName} does not match requested {targetObjectName}, removing from cache");
                    }
                    _baggedObjectCache.Remove(bagController);
                    cachedBaggedObject = null;
                }
                // Validate cached object is still valid and active
                if (cachedBaggedObject != null && cachedBaggedObject.outer != null)
                {
                    // Check if cached object is still the active state
                    bool isActive = false;
                    var stateMachines = bagController.GetComponentsInChildren<EntityStateMachine>(true);
                    foreach (var sm in stateMachines)
                    {
                        if (sm.state == cachedBaggedObject)
                        {
                            isActive = true;
                            break;
                        }
                    }
                    if (isActive)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [FindOrCreateBaggedObjectState] Returning cached active BaggedObject");
                        }
                        return cachedBaggedObject;
                    }
                    else
                    {
                        // Cached object is no longer active, remove from cache
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [FindOrCreateBaggedObjectState] Cached object no longer active, removing from cache");
                        }
                        _baggedObjectCache.Remove(bagController);
                    }
                }
                else if (cachedBaggedObject != null)
                {
                    // Cached object is destroyed, remove from cache
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] Cached object is null or destroyed, removing from cache");
                    }
                    _baggedObjectCache.Remove(bagController);
                }
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] Cache miss: no cached BaggedObject");
                }
            }
            // 3. Try to find via Bag state machine specifically
            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state != null && bagStateMachine.state.GetType() == typeof(BaggedObject))
            {
                var foundBaggedObject = (BaggedObject)bagStateMachine.state;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] Found BaggedObject in Bag state machine, caching and returning");
                }
                // Cache the found object
                _baggedObjectCache[bagController] = foundBaggedObject;
                return foundBaggedObject;
            }
            // 5. Create new BaggedObject state
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [FindOrCreateBaggedObjectState] Attempting to create new BaggedObject state");
                // Log VehicleSeat determination
                var vehicleSeat = bagController.vehicleSeat;
                Log.Info($" [FindOrCreateBaggedObjectState] VehicleSeat determination: bagController.vehicleSeat = {vehicleSeat?.name ?? "null"}");
                if (vehicleSeat != null)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] VehicleSeat hasPassenger: {vehicleSeat.hasPassenger}, NetworkpassengerBodyObject: {vehicleSeat.NetworkpassengerBodyObject?.name ?? "null"}");
                }
            }
            try
            {
                // Comprehensive null checks before creation
                if (bagController == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] bagController is null, cannot create BaggedObject");
                    }
                    return null;
                }
                if (targetObject == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] targetObject is null, cannot create BaggedObject");
                    }
                    return null;
                }
                if (bagController.gameObject == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] bagController.gameObject is null, cannot create BaggedObject");
                    }
                    return null;
                }
                // Find or create Bag state machine
                var targetStateMachine = bagStateMachine;
                if (targetStateMachine == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] Bag state machine not found, creating new one");
                    }
                    // Create new state machine
                    var stateMachineComponent = bagController.gameObject.AddComponent<EntityStateMachine>();
                    if (stateMachineComponent != null)
                    {
                        stateMachineComponent.customName = "Bag";
                        targetStateMachine = stateMachineComponent;
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [FindOrCreateBaggedObjectState] Failed to create EntityStateMachine component");
                        }
                        return null;
                    }
                }
                if (targetStateMachine != null)
                {
                    // Create new BaggedObject instance
                    var baggedObjectConstructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                    if (baggedObjectConstructor != null)
                    {
                        var newBaggedObject = (BaggedObject)baggedObjectConstructor.Invoke(null);
                        if (newBaggedObject != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [FindOrCreateBaggedObjectState] Created new BaggedObject, setting fields and state");
                            }
                            // Set required fields with null checks
                            newBaggedObject.targetObject = targetObject;
                            // Set the state machine to use this instance
                            targetStateMachine.SetState(newBaggedObject);
                            // Cache the new object
                            _baggedObjectCache[bagController] = newBaggedObject;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [FindOrCreateBaggedObjectState] Successfully created and cached new BaggedObject");
                            }
                            return newBaggedObject;
                        }
                        else
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [FindOrCreateBaggedObjectState] Failed to invoke BaggedObject constructor");
                            }
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [FindOrCreateBaggedObjectState] BaggedObject constructor not found");
                        }
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [FindOrCreateBaggedObjectState] Failed to find or create target state machine");
                    }
                }
            }
            catch (Exception ex)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] Exception creating BaggedObject: {ex.Message}");
                }
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [FindOrCreateBaggedObjectState] Returning null - failed to find or create BaggedObject");
            }
            return null;
        }
        // This is used as a last resort when cycling from null state back to main seat
        private static BaggedObject ForceCreateBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                // Log VehicleSeat determination
                var vehicleSeat = bagController.vehicleSeat;
                Log.Info($" [ForceCreateBaggedObjectState] VehicleSeat determination: bagController.vehicleSeat = {vehicleSeat?.name ?? "null"}");
                if (vehicleSeat != null)
                {
                    Log.Info($" [ForceCreateBaggedObjectState] VehicleSeat hasPassenger: {vehicleSeat.hasPassenger}, NetworkpassengerBodyObject: {vehicleSeat.NetworkpassengerBodyObject?.name ?? "null"}");
                }
            }
            if (bagController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [ForceCreateBaggedObjectState] bagController is null");
                }
                return null;
            }
            if (targetObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [ForceCreateBaggedObjectState] targetObject is null");
                }
                return null;
            }
            if (bagController.gameObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [ForceCreateBaggedObjectState] bagController.gameObject is null");
                }
                return null;
            }
            try
            {
                // Try to find any existing state machine that could host BaggedObject
                var stateMachines = bagController.GetComponentsInChildren<EntityStateMachine>(true);
                EntityStateMachine targetStateMachine = null;
                // Look for a Bag state machine first
                targetStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
                // If not found, try to find any state machine that might be suitable
                if (targetStateMachine == null)
                {
                    foreach (var sm in stateMachines)
                    {
                        if (sm.customName.Contains("Bag") || sm.gameObject.name.Contains("Bag"))
                        {
                            targetStateMachine = sm;
                            break;
                        }
                    }
                }
                // If still not found, create a new state machine
                if (targetStateMachine == null)
                {
                    var stateMachineComponent = bagController.gameObject.AddComponent<EntityStateMachine>();
                    if (stateMachineComponent != null)
                    {
                        stateMachineComponent.customName = "Bag";
                        targetStateMachine = stateMachineComponent;
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [ForceCreateBaggedObjectState] Failed to create EntityStateMachine component");
                        }
                        return null;
                    }
                }
                if (targetStateMachine != null)
                {
                    // Create new BaggedObject instance
                    var baggedObjectConstructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                    if (baggedObjectConstructor != null)
                    {
                        var newBaggedObject = (BaggedObject)baggedObjectConstructor.Invoke(null);
                        if (newBaggedObject != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [ForceCreateBaggedObjectState] Created new BaggedObject, setting fields");
                            }
                            // Set required fields with null checks
                            newBaggedObject.targetObject = targetObject;
                            // Set the state machine to use this instance
                            targetStateMachine.SetState(newBaggedObject);
                            // Cache the new object
                            _baggedObjectCache[bagController] = newBaggedObject;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [ForceCreateBaggedObjectState] Successfully created and cached new BaggedObject");
                            }
                            return newBaggedObject;
                        }
                        else
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [ForceCreateBaggedObjectState] Failed to invoke BaggedObject constructor");
                            }
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [ForceCreateBaggedObjectState] BaggedObject constructor not found");
                        }
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [ForceCreateBaggedObjectState] Failed to find or create target state machine");
                    }
                }
            }
            catch (Exception ex)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [ForceCreateBaggedObjectState] Exception: {ex.Message}");
                }
            }
            return null;
        }
    }
}
