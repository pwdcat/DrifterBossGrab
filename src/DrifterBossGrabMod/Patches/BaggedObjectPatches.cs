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
            if (targetObject == null) return;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] Called for targetObject: {targetObject?.name ?? "null"}, bagController: {bagController?.name ?? "null"}");
            }
            DrifterBagController actualBagController = bagController!;
            if (actualBagController == null)
            {
                foreach (var kvp in BagPatches.mainSeatDict)
                {
                    if (kvp.Value != null && kvp.Value.GetInstanceID() == targetObject!.GetInstanceID())
                    {
                        actualBagController = kvp.Key;
                        break;
                    }
                }
            }
            if (actualBagController == null)
            {
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
                    Log.Info($" [RefreshUIOverlayForMainSeat] Target is in additional seat, not creating UI");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] Final isNowMainSeatOccupant: {isNowMainSeatOccupant}");
            }
            if (!isNowMainSeatOccupant)
            {
                return;
            }
            // Ensures clean state before updating the new main seat
            if (BagPatches.mainSeatDict.TryGetValue(actualBagController, out var currentMainSeat) &&
                currentMainSeat != null && currentMainSeat != targetObject)
            {
                RemoveUIOverlay(currentMainSeat);
            }
            BaggedObject? baggedObject = null;
            // Ensures BaggedObject state exists when cycling from null back to main seat
            if (baggedObject == null)
            {
                baggedObject = FindOrCreateBaggedObjectState(actualBagController, targetObject);
            }
            var stateMachines = actualBagController.GetComponentsInChildren<EntityStateMachine>(true);
            if (_baggedObjectCache.TryGetValue(actualBagController, out var cachedBaggedObject))
            {
                if (cachedBaggedObject != null && cachedBaggedObject.outer != null)
                {
                    bool isStillActive = false;
                    foreach (var sm in stateMachines)
                    {
                        if (sm.state == cachedBaggedObject)
                        {
                            isStillActive = true;
                            break;
                        }
                    }
                    if (isStillActive)
                    {
                        baggedObject = cachedBaggedObject;
                        Log.Info(" [FALLBACK] Using cached BaggedObject state");
                    }
                    else
                    {
                        _baggedObjectCache.Remove(actualBagController);
                    }
                }
                else
                {
                    _baggedObjectCache.Remove(actualBagController);
                }
            }
            if (baggedObject == null)
            {
                foreach (var sm in stateMachines)
                {
                    if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                    {
                        baggedObject = (BaggedObject)sm.state;
                        _baggedObjectCache[actualBagController] = baggedObject;
                        Log.Info(" [FALLBACK] Found BaggedObject state in state machines");
                        break;
                    }
                }
            }
            if (baggedObject == null)
            {
                var bagStateMachine = EntityStateMachine.FindByCustomName(actualBagController.gameObject, "Bag");
                if (bagStateMachine != null && bagStateMachine.state != null && bagStateMachine.state.GetType() == typeof(BaggedObject))
                {
                    baggedObject = (BaggedObject)bagStateMachine.state;
                    _baggedObjectCache[actualBagController] = baggedObject;
                    Log.Info(" [FALLBACK] Found BaggedObject state via FindByCustomName");
                }
            }
            if (baggedObject == null)
            {
                foreach (var sm in stateMachines)
                {
                    if (sm.GetType().Name.Contains("Bag") || sm.customName.Contains("Bag"))
                    {
                        try
                        {
                            var stateField = typeof(EntityStateMachine).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (stateField != null)
                            {
                                var currentState = stateField.GetValue(sm);
                                if (currentState != null && currentState.GetType() == typeof(BaggedObject))
                                {
                                    baggedObject = (BaggedObject)currentState;
                                    _baggedObjectCache[actualBagController] = baggedObject;
                                    Log.Info(" [FALLBACK] Found BaggedObject via reflection on state machine: " + sm.gameObject.name);
                                    break;
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            if (baggedObject == null)
            {
                foreach (var sm in stateMachines)
                {
                    if (sm.state != null)
                    {
                        var nextStateField = sm.state.GetType().GetField("nextState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (nextStateField != null)
                        {
                            var nextState = nextStateField.GetValue(sm.state);
                            if (nextState != null && nextState.GetType() == typeof(BaggedObject))
                            {
                                baggedObject = (BaggedObject)nextState;
                                Log.Info(" [FALLBACK] Found BaggedObject as nextState");
                                break;
                            }
                        }
                        var previousStateField = sm.state.GetType().GetField("previousState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (previousStateField != null)
                        {
                            var previousState = previousStateField.GetValue(sm.state);
                            if (previousState != null && previousState.GetType() == typeof(BaggedObject))
                            {
                                baggedObject = (BaggedObject)previousState;
                                Log.Info(" [FALLBACK] Found BaggedObject as previousState");
                                break;
                            }
                        }
                    }
                }
            }
            if (baggedObject == null)
            {
                try
                {
                    var baggedObjectField = typeof(DrifterBagController).GetField("baggedObject", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (baggedObjectField != null)
                    {
                        var baggedObjectFromController = baggedObjectField.GetValue(actualBagController) as BaggedObject;
                        if (baggedObjectFromController != null)
                        {
                            baggedObject = baggedObjectFromController;
                            Log.Info(" [FALLBACK] Found BaggedObject via DrifterBagController reflection");
                        }
                        else
                        {
                            var tryOverrideUtilityMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
                            if (tryOverrideUtilityMethod != null)
                            {
                                Log.Info(" [FALLBACK] Calling TryOverrideUtility on BaggedObject to force UI overlay creation");
                                BaggedObject? baggedObjectInstance = null;
                                if (_baggedObjectCache.TryGetValue(bagController!, out var cachedBaggedObjectInstance))
                                {
                                    baggedObjectInstance = cachedBaggedObjectInstance;
                                }
                                if (baggedObjectInstance == null)
                                {
                                    var stateMachinesForFallback = actualBagController.GetComponentsInChildren<EntityStateMachine>(true);
                                    foreach (var sm in stateMachinesForFallback)
                                    {
                                        if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                                        {
                                            baggedObjectInstance = (BaggedObject)sm.state;
                                            break;
                                        }
                                    }
                                }
                                if (baggedObjectInstance == null)
                                {
                                    var stateMachinesForFallback = actualBagController.GetComponentsInChildren<EntityStateMachine>(true);
                                    foreach (var sm in stateMachinesForFallback)
                                    {
                                        if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                                        {
                                            baggedObjectInstance = (BaggedObject)sm.state;
                                            break;
                                        }
                                    }
                                }
                                if (baggedObjectInstance != null)
                                {
                                    tryOverrideUtilityMethod.Invoke(baggedObjectInstance, new object[] { null! });
                                }
                                else
                                {
                                    try
                                    {
                                        var bagStateMachine = EntityStateMachine.FindByCustomName(actualBagController.gameObject, "Bag");
                                        if (bagStateMachine != null)
                                        {
                                            var baggedObjectConstructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                                            if (baggedObjectConstructor != null)
                                            {
                                                var newBaggedObject = (BaggedObject)baggedObjectConstructor.Invoke(null);
                                                if (newBaggedObject != null)
                                                {
                                                    var outerField = typeof(BaggedObject).GetField("outer", BindingFlags.NonPublic | BindingFlags.Instance);
                                                    if (outerField != null)
                                                    {
                                                        outerField.SetValue(newBaggedObject, actualBagController.gameObject);
                                                    }
                                                    var targetObjectField = typeof(BaggedObject).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                                                    if (targetObjectField != null)
                                                    {
                                                        targetObjectField.SetValue(newBaggedObject, targetObject);
                                                    }
                                                    bagStateMachine.SetState(newBaggedObject);
                                                    baggedObject = newBaggedObject;
                                                    _baggedObjectCache[actualBagController] = baggedObject;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            var stateMachinesForCreation = actualBagController.GetComponentsInChildren<EntityStateMachine>(true);
                                            foreach (var sm in stateMachinesForCreation)
                                            {
                                                if (sm.customName.Contains("Bag") || sm.gameObject.name.Contains("Bag"))
                                                {
                                                    var baggedObjectConstructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                                                    if (baggedObjectConstructor != null)
                                                    {
                                                        var newBaggedObject = (BaggedObject)baggedObjectConstructor.Invoke(null);
                                                        if (newBaggedObject != null)
                                                        {
                                                            var outerField = typeof(BaggedObject).GetField("outer", BindingFlags.NonPublic | BindingFlags.Instance);
                                                            if (outerField != null)
                                                            {
                                                                outerField.SetValue(newBaggedObject, actualBagController.gameObject);
                                                            }
                                                            var targetObjectField = typeof(BaggedObject).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                                                            if (targetObjectField != null)
                                                            {
                                                                targetObjectField.SetValue(newBaggedObject, targetObject);
                                                            }
                                                            sm.SetState(newBaggedObject);
                                                            baggedObject = newBaggedObject;
                                                            _baggedObjectCache[actualBagController] = baggedObject;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (baggedObject == null)
                                        {
                                            Log.Info(" [FALLBACK] No BaggedObject state found, attempting to create new state machine");
                                            var stateMachineComponent = actualBagController.gameObject.AddComponent<EntityStateMachine>();
                                            if (stateMachineComponent != null)
                                            {
                                                stateMachineComponent.customName = "Bag";
                                                var baggedObjectConstructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                                                if (baggedObjectConstructor != null)
                                                {
                                                    var newBaggedObject = (BaggedObject)baggedObjectConstructor.Invoke(null);
                                                    if (newBaggedObject != null)
                                                    {
                                                        var outerField = typeof(BaggedObject).GetField("outer", BindingFlags.NonPublic | BindingFlags.Instance);
                                                        if (outerField != null)
                                                        {
                                                            outerField.SetValue(newBaggedObject, actualBagController.gameObject);
                                                        }
                                                        var targetObjectField = typeof(BaggedObject).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                                                        if (targetObjectField != null)
                                                        {
                                                            targetObjectField.SetValue(newBaggedObject, targetObject);
                                                        }
                                                        stateMachineComponent.SetState(newBaggedObject);
                                                        baggedObject = newBaggedObject;
                                                        _baggedObjectCache[actualBagController] = baggedObject;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Info(" [FALLBACK] Exception creating new BaggedObject state: " + ex.Message);
                                    }
                                    if (baggedObjectInstance == null && baggedObject == null)
                                    {
                                        Log.Info(" [FALLBACK] No BaggedObject instance found for TryOverrideUtility fallback");
                                    }
                                }
                            }
                            else
                            {
                                Log.Info(" [FALLBACK] TryOverrideUtility method not found on BaggedObject");
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            // This is the final fallback when all other methods fail
            if (baggedObject == null)
            {
                // Try to find an existing BaggedObject instance in the state machines
                var stateMachinesForFallback = bagController.GetComponentsInChildren<EntityStateMachine>(true);
                foreach (var sm in stateMachinesForFallback)
                {
                    if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                    {
                        baggedObject = (BaggedObject)sm.state;
                        Log.Info(" [FALLBACK] Found BaggedObject state in final fallback state machines search");
                        break;
                    }
                }
            }
            // This is the final fallback when the state machine has changed but we still need UI functionality
            if (baggedObject == null)
            {
                try
                {
                    var tryOverrideUtilityMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
                    if (tryOverrideUtilityMethod != null)
                    {
                        BaggedObject? baggedObjectInstance = null;
                        // Try to get from cache first
                        if (_baggedObjectCache.TryGetValue(bagController!, out var cachedBaggedObjectInstance))
                        {
                            baggedObjectInstance = cachedBaggedObjectInstance;
                        }
                        // If not in cache, try to find one in the state machines
                        if (baggedObjectInstance == null)
                        {
                            var stateMachinesForFallback = bagController!.GetComponentsInChildren<EntityStateMachine>(true);
                            foreach (var sm in stateMachinesForFallback)
                            {
                                if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                                {
                                    baggedObjectInstance = (BaggedObject)sm.state;
                                    break;
                                }
                            }
                        }
                        // If we found an instance, call the method on it
                        if (baggedObjectInstance != null)
                        {
                            tryOverrideUtilityMethod.Invoke(baggedObjectInstance, new object[] { null! });
                        }
                        else
                        {
                            // Last resort: skip this fallback
                        }
                    }
                    else
                    {
                    }
                }
                catch (Exception)
                {
                }
                try
                {
                    var bagSkillLocator = bagController!.GetComponent<SkillLocator>();
                    if (bagSkillLocator != null && bagSkillLocator.utility != null)
                    {
                        var tryOverrideMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
                        if (tryOverrideMethod != null)
                        {
                            Log.Info(" [FALLBACK] Calling TryOverrideUtility via skill locator to force UI overlay creation");
                            // Try to find an existing BaggedObject instance to call the method on
                            BaggedObject baggedObjectInstance = null;
                            // Try to get from cache first
                            if (_baggedObjectCache.TryGetValue(actualBagController!, out var cachedBaggedObjectInstance))
                            {
                                baggedObjectInstance = cachedBaggedObjectInstance;
                            }
                            // If not in cache, try to find one in the state machines
                            if (baggedObjectInstance == null)
                            {
                                var stateMachinesForFallback = actualBagController.GetComponentsInChildren<EntityStateMachine>(true);
                                foreach (var sm in stateMachinesForFallback)
                                {
                                    if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                                    {
                                        baggedObjectInstance = (BaggedObject)sm.state;
                                        break;
                                    }
                                }
                            }
                            // If we found an instance, call the method on it
                            if (baggedObjectInstance != null)
                            {
                                tryOverrideMethod.Invoke(baggedObjectInstance, new object[] { bagSkillLocator.utility });
                            }
                            else
                            {
                                // No BaggedObject instance found - skip this fallback
                            }
                        }
                    }
                    // If that doesn't work, try to manually create an overlay using the bag controller
                    // This is a last resort fallback
                    var uiOverlayPrefab = Resources.Load<GameObject>("Prefabs/HudOverlays/DrifterBaggedObjectOverlay");
                    if (uiOverlayPrefab != null)
                    {
                        OverlayCreationParams overlayCreationParams = new OverlayCreationParams
                        {
                            prefab = uiOverlayPrefab,
                            childLocatorEntry = "BaggedObjectOverlay"
                        };
                        var fallbackOverlayController = HudOverlayManager.AddOverlay(bagController.gameObject, overlayCreationParams);
                        return; // Success - we've created the fallback overlay
                    }
                }
                catch (Exception)
                {
                }
                // If all fallbacks fail, skip the overlay refresh for this cycle
                return;
            }
            if (baggedObject == null)
            {
                return;
            }
            DumpBaggedObjectFields(baggedObject, "RefreshUIOverlayForMainSeat (scroll switch)");
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                var debugSeat = actualBagController.vehicleSeat;
                var debugCurrentPassengerBodyObject = debugSeat?.NetworkpassengerBodyObject;
                Log.Info(" [DEBUG] RefreshUIOverlayForMainSeat: currentPassengerBodyObject=" + (debugCurrentPassengerBodyObject?.name ?? "null") + ", targetObject=" + (targetObject?.name ?? "null"));
            }
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var overlayPrefabField = AccessTools.Field(typeof(BaggedObject), "uiOverlayPrefab");
            var overlayChildLocatorEntryField = AccessTools.Field(typeof(BaggedObject), "uiOverlayChildLocatorEntry");
            var existingController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            if (existingController != null)
            {
                // This ensures clean state when cycling between different objects
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info(" [DEBUG] RefreshUIOverlayForMainSeat: destroying existing overlay and creating fresh one for " + targetObject!.name);
                }
                // Remove the existing overlay controller
                HudOverlayManager.RemoveOverlay(existingController);
                uiOverlayField.SetValue(baggedObject, null);
                // Clear the target fields first
                UpdateTargetFields(baggedObject!, targetObject!, actualBagController!);
                // Now create a fresh overlay
                // Get the overlay parameters
                GameObject overlayPrefab = (GameObject)overlayPrefabField.GetValue(baggedObject);
                string overlayChildLocatorEntry = (string)overlayChildLocatorEntryField.GetValue(baggedObject);
                if (overlayPrefab != null)
                {
                    // Create new overlay
                    OverlayCreationParams overlayCreationParams = new OverlayCreationParams
                    {
                        prefab = overlayPrefab,
                        childLocatorEntry = overlayChildLocatorEntry
                    };
                    var newOverlayController = HudOverlayManager.AddOverlay(baggedObject.outer.gameObject, overlayCreationParams);
                    uiOverlayField.SetValue(baggedObject, newOverlayController);
                    // Register event handlers
                    var onUIOverlayInstanceAddedMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceAdded");
                    var onUIOverlayInstanceRemoveMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceRemove");
                    newOverlayController.onInstanceAdded += (OverlayController controller, GameObject instance) =>
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [DEBUG] onInstanceAdded callback called for {targetObject?.name}");
                        }
                        onUIOverlayInstanceAddedMethod.Invoke(baggedObject, new object[] { controller, instance });
                    };
                    newOverlayController.onInstanceRemove += (OverlayController controller, GameObject instance) =>
                    {
                        onUIOverlayInstanceRemoveMethod.Invoke(baggedObject, new object[] { controller, instance });
                    };
                }
                else
                {
                    Log.Info(" [DEBUG] uiOverlayPrefab is null, cannot create overlay");
                }
            }
            // Update the targetObject field of BaggedObject to point to the new target
            baggedObject!.targetObject = targetObject!;
            // Update targetBody and vehiclePassengerAttributes for the new target first
            // This must be done BEFORE calling RefreshUIOverlay
            var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
            var isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
            var vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
            var baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
            var drifterBagControllerField = AccessTools.Field(typeof(BaggedObject), "drifterBagController");
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
                baggedMassField.SetValue(baggedObject, newMass);
            }
            // Check if we need to recreate the overlay
            // This happens when uiOverlayController is null or when target has changed
            var uiOverlayController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            if (uiOverlayController == null)
            {
                // Need to create a new overlay controller
                // Get the overlay parameters
                GameObject overlayPrefab = (GameObject)overlayPrefabField.GetValue(baggedObject);
                string overlayChildLocatorEntry = (string)overlayChildLocatorEntryField.GetValue(baggedObject);
                if (overlayPrefab != null)
                {
                    // Create new overlay
                    OverlayCreationParams overlayCreationParams = new OverlayCreationParams
                    {
                        prefab = overlayPrefab,
                        childLocatorEntry = overlayChildLocatorEntry
                    };
                    uiOverlayController = HudOverlayManager.AddOverlay(baggedObject.outer.gameObject, overlayCreationParams);
                    uiOverlayField.SetValue(baggedObject, uiOverlayController);
                    // Register event handlers
                    var onUIOverlayInstanceAddedMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceAdded");
                    var onUIOverlayInstanceRemoveMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceRemove");
                    uiOverlayController.onInstanceAdded += (OverlayController controller, GameObject instance) =>
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [DEBUG] onInstanceAdded callback called for {targetObject?.name}");
                        }
                        onUIOverlayInstanceAddedMethod.Invoke(baggedObject, new object[] { controller, instance });
                    };
                    uiOverlayController.onInstanceRemove += (OverlayController controller, GameObject instance) =>
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [DEBUG] onInstanceRemove callback called for {targetObject?.name}");
                        }
                        onUIOverlayInstanceRemoveMethod.Invoke(baggedObject, new object[] { controller, instance });
                    };
                }
                else
                {
                }
            }
            // Refresh existing overlay instances with new target info
            var correctBaggedObject = FindOrCreateBaggedObjectState(actualBagController, targetObject);
            if (correctBaggedObject != null)
            {
                RefreshUIOverlay(correctBaggedObject);
            }
            else
            {
                RefreshUIOverlay(baggedObject);
            }
            // Force a skill override check by calling TryOverrideUtility and TryOverridePrimary with the respective skills
            var skillLocator = baggedObject.outer.GetComponent<SkillLocator>();
            if (skillLocator != null)
            {
                if (skillLocator.utility != null)
                {
                    // Force the utility skill to be reconsidered for override
                    var tryOverrideUtilityMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
                    if (tryOverrideUtilityMethod != null)
                    {
                        // Call with skip prefix to avoid the main seat check (since we know it's in main seat now)
                        tryOverrideUtilityMethod.Invoke(baggedObject, new object[] { skillLocator.utility });
                    }
                }
                if (skillLocator.primary != null)
                {
                    // Force the primary skill to be reconsidered for override
                    var tryOverridePrimaryMethod = AccessTools.Method(typeof(BaggedObject), "TryOverridePrimary");
                    if (tryOverridePrimaryMethod != null)
                    {
                        // Call with skip prefix to avoid the main seat check (since we know it's in main seat now)
                        tryOverridePrimaryMethod.Invoke(baggedObject, new object[] { skillLocator.primary });
                    }
                }
            }
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
            if (targetObject == null) return;
            var baggedObject = targetObject.GetComponent<BaggedObject>();
            if (baggedObject == null) return;
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
                // Only remove overlay if object is neither actually in main seat nor tracked as main seat
                if (isActuallyInMainSeat || isTrackedAsMainSeat)
                {
                    return; // Don't remove overlay if still in main seat
                }
            }
            // Remove any existing overlay controller
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var existingController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            if (existingController != null)
            {
                HudOverlayManager.RemoveOverlay(existingController);
                uiOverlayField.SetValue(baggedObject, null);
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
                // Only allow if the object is in the main seat
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverrideUtility] allowing skill override for {targetObject?.name} (main seat: {isMainSeatOccupant})");
                    }
                    return true; // Allow normal execution
                }
                else
                {
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
                    return true; // Allow normal execution
                }
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);
                // Only allow if the object is in the main seat
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverridePrimary] allowing skill override for {targetObject?.name} (main seat: {isMainSeatOccupant})");
                    }
                    return true; // Allow normal execution
                }
                else
                {
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
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
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
                        Log.Info($" [BaggedObject_OnEnter_Postfix] Seat or tracked has targetObject, not removing UI");
                    }
                    // Ensure UI is created/refreshed for main seat objects
                    RefreshUIOverlayForMainSeat(bagController, targetObject);
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
                if (!isMainSeatOccupant && !hasAuthority)
                {
                    bool isCurrentlyTracked = BagPatches.mainSeatDict.TryGetValue(bagController, out var currentlyTracked) &&
                                            ReferenceEquals(targetObject, currentlyTracked);
                    if (!isCurrentlyTracked)
                    {
                        // Only remove overlay if this object is not currently tracked as main seat and client doesn't have authority
                        if (controller != null)
                        {
                            HudOverlayManager.RemoveOverlay(controller);
                            // Null out the field to prevent OnExit from trying to remove again
                            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                            uiOverlayField.SetValue(__instance, null);
                        }
                    }
                }
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
            if (baggedObject == null) return;
            var targetObject = baggedObject.targetObject;
            // Get the uiOverlayController field using reflection
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var uiOverlayController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            if (uiOverlayController == null)
            {
                Log.Info(" [DEBUG] RefreshUIOverlay EXIT: uiOverlayController is null");
                return;
            }
            Log.Info(" [DEBUG] RefreshUIOverlay: uiOverlayController found, updating instances");
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
        // This provides reliable state detection and creation with proper caching
        private static BaggedObject FindOrCreateBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return null;
            // 1. Check cache first
            if (_baggedObjectCache.TryGetValue(bagController, out var cachedBaggedObject))
            {
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
                        return cachedBaggedObject;
                    }
                    else
                    {
                        // Cached object is no longer active, remove from cache
                        _baggedObjectCache.Remove(bagController);
                    }
                }
                else
                {
                    // Cached object is destroyed, remove from cache
                    _baggedObjectCache.Remove(bagController);
                }
            }
            // 2. Search for active BaggedObject state in all state machines
            var stateMachinesForSearch = bagController.GetComponentsInChildren<EntityStateMachine>(true);
            foreach (var sm in stateMachinesForSearch)
            {
                if (sm.state != null && sm.state.GetType() == typeof(BaggedObject))
                {
                    var foundBaggedObject = (BaggedObject)sm.state;
                    // Cache the found object for future use
                    _baggedObjectCache[bagController] = foundBaggedObject;
                    return foundBaggedObject;
                }
            }
            // 3. Try to find via Bag state machine specifically
            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state != null && bagStateMachine.state.GetType() == typeof(BaggedObject))
            {
                var foundBaggedObject = (BaggedObject)bagStateMachine.state;
                // Cache the found object
                _baggedObjectCache[bagController] = foundBaggedObject;
                return foundBaggedObject;
            }
            // 4. Try reflection on DrifterBagController
            try
            {
                var baggedObjectField = typeof(DrifterBagController).GetField("baggedObject", BindingFlags.NonPublic | BindingFlags.Instance);
                if (baggedObjectField != null)
                {
                    var baggedObjectFromController = baggedObjectField.GetValue(bagController) as BaggedObject;
                    if (baggedObjectFromController != null)
                    {
                        // Validate this object is still active
                        bool isActive = false;
                        foreach (var sm in stateMachinesForSearch)
                        {
                            if (sm.state == baggedObjectFromController)
                            {
                                isActive = true;
                                break;
                            }
                        }
                        if (isActive)
                        {
                            // Cache it
                            _baggedObjectCache[bagController] = baggedObjectFromController;
                            return baggedObjectFromController;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            // 5. Create new BaggedObject state
            try
            {
                // Find or create Bag state machine
                var targetStateMachine = bagStateMachine;
                if (targetStateMachine == null)
                {
                    // Create new state machine
                    var stateMachineComponent = bagController.gameObject.AddComponent<EntityStateMachine>();
                    if (stateMachineComponent != null)
                    {
                        stateMachineComponent.customName = "Bag";
                        targetStateMachine = stateMachineComponent;
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
                            // Set required fields
                            var outerField = typeof(BaggedObject).GetField("outer", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (outerField != null)
                            {
                                outerField.SetValue(newBaggedObject, bagController.gameObject);
                            }
                            var targetObjectField = typeof(BaggedObject).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (targetObjectField != null)
                            {
                                targetObjectField.SetValue(newBaggedObject, targetObject);
                            }
                            // Set the state machine to use this instance
                            targetStateMachine.SetState(newBaggedObject);
                            // Cache the new object
                            _baggedObjectCache[bagController] = newBaggedObject;
                            return newBaggedObject;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
        // This is used as a last resort when cycling from null state back to main seat
        private static BaggedObject ForceCreateBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return null;
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
                            // Set required fields
                            var outerField = typeof(BaggedObject).GetField("outer", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (outerField != null)
                            {
                                outerField.SetValue(newBaggedObject, bagController.gameObject);
                            }
                            var targetObjectField = typeof(BaggedObject).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (targetObjectField != null)
                            {
                                targetObjectField.SetValue(newBaggedObject, targetObject);
                            }
                            // Set the state machine to use this instance
                            targetStateMachine.SetState(newBaggedObject);
                            // Cache the new object
                            _baggedObjectCache[bagController] = newBaggedObject;
                            return newBaggedObject;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
