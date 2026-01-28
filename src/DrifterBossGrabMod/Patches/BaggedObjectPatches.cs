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
namespace DrifterBossGrabMod.Patches
{
    public static class BaggedObjectPatches
    {
        private static string GetSafeName(UnityEngine.Object obj) => obj ? obj.name : "null";
        private static readonly HashSet<GameObject> _suppressedExitObjects = new HashSet<GameObject>();
        public static void SuppressExitForObject(GameObject obj)
        {
            if (obj == null) return;
            lock (_suppressedExitObjects)
            {
                _suppressedExitObjects.Add(obj);
            }
            // Reset after 2 seconds to be safe
            DrifterBossGrabPlugin.Instance.StartCoroutine(ResetSuppressionForObject(obj, 2f));
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
                Log.Info($" [RefreshUIOverlayForMainSeat] Called for targetObject: {GetSafeName(targetObject)}, bagController: {GetSafeName(bagController)}");
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
            if (actualBagController == null && targetObject != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] bagController is null, searching in mainSeatDict for targetObject");
                }
                foreach (var kvp in BagPatches.mainSeatDict)
                {
                    if (kvp.Value != null && kvp.Value.GetInstanceID() == targetObject.GetInstanceID())
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


            if (targetObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] targetObject is null, removing UI overlay for null state");
                }
                RemoveUIOverlayForNullState(actualBagController);
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] actualBagController: {GetSafeName(actualBagController)}");
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RefreshUIOverlayForMainSeat] Checking seat state for targetObject: {GetSafeName(targetObject)}");
            }
            bool isNowMainSeatOccupant = false;
            // Method 1: Check vehicle seat state
            var outerSeat = actualBagController.vehicleSeat;
            if (outerSeat != null)
            {
                var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RefreshUIOverlayForMainSeat] outerSeat: {GetSafeName(outerSeat)}");
                    Log.Info($" [RefreshUIOverlayForMainSeat] outerCurrentPassengerBodyObject: {GetSafeName(outerCurrentPassengerBodyObject)}");
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

            if (!isNowMainSeatOccupant && BagPatches.mainSeatDict.TryGetValue(actualBagController, out var trackedMainSeatOccupant))
            {
                isNowMainSeatOccupant = ReferenceEquals(targetObject, trackedMainSeatOccupant);
                if (PluginConfig.Instance.EnableDebugLogs.Value && isNowMainSeatOccupant)
                {
                    Log.Info(" [RefreshUIOverlayForMainSeat] Using tracked main seat state");
                }
            }


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

            // Update BaggedObject state
            // Logic extracted to SynchronizeBaggedObjectState to ensure it can be called independently of UI checks
            SynchronizeBaggedObjectState(actualBagController, targetObject);
            return;
        }

        public static void SynchronizeBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null) return;
             // Update BaggedObject state
            BaggedObject? baggedObject = FindOrCreateBaggedObjectState(bagController, targetObject);
            if (baggedObject != null)
            {
                // Check if targetObject has changed
                var currentTargetObject = baggedObject.targetObject;
                bool targetObjectChanged = !ReferenceEquals(currentTargetObject, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value && targetObjectChanged)
                {
                    Log.Info($" [SynchronizeBaggedObjectState] targetObject changed from {currentTargetObject?.name ?? "null"} to {targetObject?.name ?? "null"}");
                }
                
                // Update the targetObject field of BaggedObject to point to the new target
                baggedObject.targetObject = targetObject;
                
                // Update targetBody and vehiclePassengerAttributes for the new target first
                var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
                var isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
                var vehiclePassengerAttributesField = AccessTools.Field(typeof(BaggedObject), "vehiclePassengerAttributes");
                var baggedMassField = AccessTools.Field(typeof(BaggedObject), "baggedMass");
                
                HealthComponent healthComponent;
                if (targetObject != null && targetObject.TryGetComponent<HealthComponent>(out healthComponent))
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
                
                if (targetObject != null)
                {
                    var specialObjectAttributes = targetObject.GetComponent<SpecialObjectAttributes>();
                    if (specialObjectAttributes != null)
                    {
                        vehiclePassengerAttributesField.SetValue(baggedObject, specialObjectAttributes);
                    }
                }

                // Recalculate bagged mass for the new target
                if (bagController != null && targetObject != null)
                {
                    float newMass;
                    if (bagController.gameObject == targetObject)
                    {
                        newMass = bagController.baggedMass;
                    }
                    else
                    {
                        newMass = bagController.CalculateBaggedObjectMass(targetObject);
                    }
                    baggedMassField.SetValue(baggedObject, newMass);
                    UpdateBagScale(baggedObject, newMass);
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
                        var tryOverridePrimaryMethod = AccessTools.Method(typeof(BaggedObject), "TryOverridePrimary", new Type[] { typeof(GenericSkill) });
                        tryOverridePrimaryMethod?.Invoke(baggedObject, new object[] { skillLocator.primary });
                    }
                }


                if (bagController != null)
                {
                    if (NetworkServer.active)
                    {
                        // On server, setting this property will trigger the sync var hook on clients and update local state
                        if (bagController.NetworkbaggedObject != targetObject)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [SynchronizeBaggedObjectState] Updating DrifterBagController.NetworkbaggedObject to {targetObject?.name ?? "null"}");
                            }
                            bagController.NetworkbaggedObject = targetObject;
                        }
                    }
                    else if (bagController.hasAuthority)
                    {                         
                         // Check if we need to update to avoid redundant calls
                         var currentBaggedObj = bagController.baggedObject; 
                         if (currentBaggedObj != targetObject)
                         {
                             if (PluginConfig.Instance.EnableDebugLogs.Value)
                             {
                                 Log.Info($" [SynchronizeBaggedObjectState] Manually invoking OnSyncBaggedObject on Client Authority for {targetObject?.name ?? "null"}");
                             }
                             // Use reflection to call private OnSyncBaggedObject
                             var onSyncMethod = AccessTools.Method(typeof(DrifterBagController), "OnSyncBaggedObject", new Type[] { typeof(GameObject) });
                             if (onSyncMethod != null)
                             {
                                 onSyncMethod.Invoke(bagController, new object[] { targetObject });
                             }
                         }
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
                UpdateBagScale(baggedObject!, newMass);
            }
        }

        public static void UpdateBagScale(BaggedObject baggedObject, float mass)
        {
            if (baggedObject == null) return;

            var bagScale01Field = AccessTools.Field(typeof(BaggedObject), "bagScale01");
            var setScaleMethod = AccessTools.Method(typeof(BaggedObject), "SetScale", new Type[] { typeof(float) });

            float value = mass;
            if (!PluginConfig.Instance.UncapBagScale.Value)
            {
                value = Mathf.Clamp(mass, 1f, DrifterBagController.maxMass);
            }
            else
            {
                value = Mathf.Max(mass, 1f);
            }

            float t = (value - 1f) / (DrifterBagController.maxMass - 1f);
            float bagScale01 = 0.5f + 0.5f * t;

            bagScale01Field.SetValue(baggedObject, bagScale01);
            setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
        }
        // Remove the UI overlay for an object that has left the main seat.
        public static void RemoveUIOverlay(GameObject targetObject, DrifterBagController? bagController = null)
        {
            if (targetObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] EXIT: targetObject is null");
                }
                return;
            }

            // If bagController is not provided, try to find it
            if (bagController == null)
            {
                foreach (var kvp in BagPatches.mainSeatDict)
                {
                    if (ReferenceEquals(kvp.Value, targetObject))
                    {
                        bagController = kvp.Key;
                        break;
                    }
                }
            }
            
            // Also check if we can find it via BaggedObject state machine (if valid)
             if (bagController == null)
            {

                // In the issue case, we don't know the controller easily if not tracked.
                // However, we can try to find the controller from the object if it was spawned by it or something, but likely not.
            }

            BaggedObject baggedObject = null;
            if (bagController != null)
            {
                 baggedObject = FindOrCreateBaggedObjectState(bagController, targetObject);
            }

            if (baggedObject == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [RemoveUIOverlay] EXIT: Could not find BaggedObject state for {targetObject.name} (Controller: {bagController?.name ?? "null"})");
                }
                return;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [RemoveUIOverlay] Called for {targetObject.name}");
            }


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

            bool hasTrackedMainSeat = BagPatches.mainSeatDict.TryGetValue(bagController, out var trackedMainSeat) && trackedMainSeat != null;

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

                    var onUIOverlayInstanceRemoveMethod = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceRemove");
                    if (onUIOverlayInstanceRemoveMethod != null)
                    {

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

                    HudOverlayManager.RemoveOverlay(uiOverlayController);
                    uiOverlayField.SetValue(baggedObject, null);
                }
                catch (Exception e)
                {
                    Log.Info($" [RemoveUIOverlayForNullState] Exception removing overlay: {e.Message}");
                }
            }

            baggedObject.targetObject = null;
            targetBodyField.SetValue(baggedObject, null);
            vehiclePassengerAttributesField.SetValue(baggedObject, null);
            baggedMassField.SetValue(baggedObject, 0f);

            _baggedObjectCache.Remove(bagController);
        }

        {
            if (bagController == null || targetObject == null) return false;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] Checking for bagController: {GetSafeName(bagController)} (ID:{bagController?.GetInstanceID()}), targetObject: {GetSafeName(targetObject)}");
            }

            var trackedMainSeat = BagPatches.GetMainSeatObject(bagController!);
            

            if (BagPatches.mainSeatDict.ContainsKey(bagController))
            {
                bool isTrackedAsMain = trackedMainSeat != null && ReferenceEquals(targetObject, trackedMainSeat);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [IsInMainSeat] Using logical state: isTrackedAsMain={isTrackedAsMain}, trackedMainSeat={GetSafeName(trackedMainSeat)}");
                }
                return isTrackedAsMain;
            }


            var outerSeat = bagController.vehicleSeat;
            if (outerSeat == null) return false;

            var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
            bool result = outerCurrentPassengerBodyObject != null && ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
            
            // Also check if it's in an additional seat - if so, it's definitely not the main seat
            if (result && BagPatches.GetAdditionalSeat(bagController, targetObject) != null)
            {
                result = false;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [IsInMainSeat] Fallback result: {result} (outerCurrentPassengerBodyObject={GetSafeName(outerCurrentPassengerBodyObject)})");
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
                    string targetName = (targetObject ? targetObject.name : "null");
                    Log.Info($" [TryOverrideUtility] Attempting override for {targetName}, isMainSeatOccupant: {isMainSeatOccupant}, skill: {(skill ? skill.skillName : "null")}");
                }
                // Only allow if the object is in the main seat
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverrideUtility] ALLOWING skill override for {(targetObject ? targetObject.name : "null")} (main seat: {isMainSeatOccupant})");
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
                        string targetName = (targetObject ? targetObject.name : "null");
                        string trackedName = (BagPatches.GetMainSeatObject(bagController) ? BagPatches.GetMainSeatObject(bagController).name : "null");
                        string passengerName = (bagController?.vehicleSeat?.NetworkpassengerBodyObject ? bagController.vehicleSeat.NetworkpassengerBodyObject.name : "null");
                        
                        Log.Info($" [TryOverrideUtility] BLOCKING override - unsetting existing override for {targetName} (not in main seat), overriddenUtility: {(overriddenUtility ? overriddenUtility.skillName : "null")}");
                        // Log current state details
                        Log.Info($" [TryOverrideUtility] Current state - tracked main seat: {trackedName}, vehicle passenger: {passengerName}, hasAuthority: {bagController?.hasAuthority ?? false}");
                    }
                    if (overriddenUtility != null)
                    {
                        var utilityOverride = (SkillDef)utilityOverrideField.GetValue(__instance);
                        overriddenUtility.UnsetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                        overriddenUtilityField.SetValue(__instance, null);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [TryOverrideUtility] Unset skill override for {(targetObject ? targetObject.name : "null")} (not in main seat)");
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
                    string targetName = (targetObject ? targetObject.name : "null");
                    Log.Info($" [TryOverridePrimary] Attempting override for {targetName}, isMainSeatOccupant: {isMainSeatOccupant}, skill: {(skill ? skill.skillName : "null")}");
                }
                // Only allow if the object is in the main seat
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [TryOverridePrimary] ALLOWING skill override for {(targetObject ? targetObject.name : "null")} (main seat: {isMainSeatOccupant})");
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
                        string targetName = (targetObject ? targetObject.name : "null");
                        string trackedName = (BagPatches.GetMainSeatObject(bagController) ? BagPatches.GetMainSeatObject(bagController).name : "null");
                        string passengerName = (bagController?.vehicleSeat?.NetworkpassengerBodyObject ? bagController.vehicleSeat.NetworkpassengerBodyObject.name : "null");

                        Log.Info($" [TryOverridePrimary] BLOCKING override - unsetting existing override for {targetName} (not in main seat), overriddenPrimary: {(overriddenPrimary ? overriddenPrimary.skillName : "null")}");
                        // Log current state details
                        Log.Info($" [TryOverridePrimary] Current state - tracked main seat: {trackedName}, vehicle passenger: {passengerName}, hasAuthority: {bagController?.hasAuthority ?? false}");
                    }
                    if (overriddenPrimary != null)
                    {
                        var primaryOverride = (SkillDef)primaryOverrideField.GetValue(__instance);
                        overriddenPrimary.UnsetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                        overriddenPrimaryField.SetValue(__instance, null);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [TryOverridePrimary] Unset skill override for {(targetObject ? targetObject.name : "null")} (not in main seat)");
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
                        Log.Info($" [BaggedObject_OnEnter] targetObject is null, skipping original OnEnter to prevent crash");
                    }
                    return false; // Skip the original OnEnter to prevent NRE
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

                // Only populate if the network controller hasn't synced a null state (selectedIndex=-1)
                if (bagController.hasAuthority && !NetworkServer.active)
                {
                    // Check if network controller has synced state
                    var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                    bool shouldPopulateMainSeat = true;
                    
                    if (netController != null && netController.selectedIndex < 0 && netController.GetBaggedObjects().Count > 0)
                    {
                        // Network says we're in null state AND we have objects - don't set main seat
                        shouldPopulateMainSeat = false;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [BaggedObject_OnEnter_Postfix] Network selectedIndex={netController.selectedIndex}, existing objects={netController.GetBaggedObjects().Count}, NOT populating mainSeatDict");
                        }
                    }
                    
                    // Set this object as the main seat object in the local dictionary (only for capacity=1)
                    if (shouldPopulateMainSeat && !BagPatches.mainSeatDict.ContainsKey(bagController))
                    {
                        // Double check it's not in an additional seat
                        if (BagPatches.GetAdditionalSeat(bagController, targetObject) == null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [BaggedObject_OnEnter_Postfix] Populating mainSeatDict for client authority: {targetObject.name}");
                            }
                            BagPatches.SetMainSeatObject(bagController, targetObject);
                        }
                        else if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                             Log.Info($" [BaggedObject_OnEnter_Postfix] Skipping mainSeatDict population because {targetObject.name} is in an additional seat");
                        }
                    }
                    
                    // Also ensure it's in baggedObjectsDict (always do this, regardless of main seat state)
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
                            Log.Info($" [BaggedObject_OnEnter_Postfix] Added {GetSafeName(targetObject)} to baggedObjectsDict for client authority");
                        }
                    }
                }

                var outerMainSeat = bagController.vehicleSeat;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] outerMainSeat: {GetSafeName(outerMainSeat)}");
                    if (outerMainSeat != null)
                    {
                        Log.Info($" [BaggedObject_OnEnter_Postfix] outerMainSeat.hasPassenger: {outerMainSeat.hasPassenger}");
                        Log.Info($" [BaggedObject_OnEnter_Postfix] outerMainSeat.NetworkpassengerBodyObject: {GetSafeName(outerMainSeat.NetworkpassengerBodyObject)}");
                        Log.Info($" [BaggedObject_OnEnter_Postfix] ReferenceEquals check: {!ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject)}");
                    }
                }
                bool seatHasTarget = outerMainSeat != null && outerMainSeat.hasPassenger && ReferenceEquals(outerMainSeat.NetworkpassengerBodyObject, targetObject);
                bool trackedHasTarget = BagPatches.mainSeatDict.TryGetValue(bagController, out var tracked) && ReferenceEquals(tracked, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnEnter_Postfix] seatHasTarget: {seatHasTarget}, trackedHasTarget: {trackedHasTarget}");
                }
                
                if (bagController.hasAuthority)
                {
                     // Do nothing eager here. Wait for sync.
                }
                else if (!seatHasTarget && !trackedHasTarget)
                {
                    // Neither seat nor tracked has targetObject, remove the UI
                    // (But only if it's not in an additional seat either)
                    bool isAdditionalSeat = BagPatches.GetAdditionalSeat(bagController, targetObject) != null;
                    if (!isAdditionalSeat)
                    {
                        var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                        var uiOverlayController = (OverlayController)uiOverlayField.GetValue(__instance);
                        if (uiOverlayController != null)
                        {
                            HudOverlayManager.RemoveOverlay(uiOverlayController);
                            uiOverlayField.SetValue(__instance, null);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [BaggedObject_OnEnter_Postfix] Removed UI overlay because neither seat nor tracked has targetObject and no authority");
                            }
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
                            Log.Info($" [BaggedObject_OnEnter_Postfix] Added {GetSafeName(targetObject)} to baggedObjectsDict");
                        }
                    }
                    BagPatches.UpdateCarousel(bagController);
                    // Sync to network so server knows about client grabs
                    BagPatches.UpdateNetworkBagState(bagController);
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter_Postfix] Seat or tracked has targetObject, not removing UI");
                    }
                    // Ensure UI is created/refreshed for main seat objects
                    if (BagPatches.GetAdditionalSeat(bagController, targetObject) == null)
                    {
                        RefreshUIOverlayForMainSeat(bagController, targetObject);
                    }
                }
                // Always remove the overlay to use carousel instead
                var uiOverlayField2 = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                var uiOverlayController2 = (OverlayController)uiOverlayField2.GetValue(__instance);
                if (uiOverlayController2 != null)
                {
                    HudOverlayManager.RemoveOverlay(uiOverlayController2);
                    uiOverlayField2.SetValue(__instance, null);
                }

                // Fix for "Hands Free" soft-lock/glitch
                // If the object is physically stashed (in additional seat), we must exit BaggedObject state on the Bag machine.
                // This ensures the Bag machine goes back to Idle, matching the "Empty Main Seat" state.
                bool isStashed = BagPatches.GetAdditionalSeat(bagController, targetObject) != null;
                // Also check if it's NOT in main seat (hands free)
                bool isInMain = (bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger && ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, targetObject));
                
                if (isStashed && !isInMain)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnEnter_Postfix] Object {targetObject.name} is stashed. Exiting BaggedObject state to Idle.");
                    }
                    __instance.outer.SetNextStateToMain();
                }

                // Uncap Bag Scale logic
                if (PluginConfig.Instance.UncapBagScale.Value)
                {
                    try
                    {
                        float baggedMass = bagController != null ? bagController.baggedMass : (float)AccessTools.Field(typeof(BaggedObject), "baggedMass").GetValue(__instance);
                        UpdateBagScale(__instance, baggedMass);

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            var bagScale01Field = AccessTools.Field(typeof(BaggedObject), "bagScale01");
                            var bagScale01 = (float)bagScale01Field.GetValue(__instance);
                            Log.Info($" [BaggedObject_OnEnter_Postfix] Uncapped Bag Scale: {bagScale01} (Mass: {baggedMass})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($" [BaggedObject_OnEnter_Postfix] Error uncapping bag scale: {ex}");
                    }
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
                if (!isMainSeatOccupant)
                {
                    bool isCurrentlyTracked = BagPatches.mainSeatDict.TryGetValue(bagController, out var currentlyTracked) &&
                                            ReferenceEquals(targetObject, currentlyTracked);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [OnUIOverlayInstanceAdded] isCurrentlyTracked: {isCurrentlyTracked}, currentlyTracked: {currentlyTracked?.name ?? "null"}");
                    }
                    if (!isCurrentlyTracked)
                    {
                        // Only remove overlay if this object is not currently tracked as main seat
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [OnUIOverlayInstanceAdded] REMOVING overlay for {targetObject?.name ?? "null"} - not main seat");
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
                        Log.Info($" [OnUIOverlayInstanceAdded] NOT removing overlay - isMainSeatOccupant");
                    }
                }
            // If using bottomless bag (has additional seats), remove the overlay to prevent overlapping with carousel
            // Only try to remove if controller is still valid (wasn't already removed above)
            if (controller != null && bagController != null && BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict) && seatDict.Count > 0)
            {
                var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                var currentOverlay = (OverlayController)uiOverlayField.GetValue(__instance);
                if (currentOverlay != null)
                {
                    try
                    {
                        HudOverlayManager.RemoveOverlay(currentOverlay);
                    }
                    catch (ArgumentOutOfRangeException) { /* Overlay was already removed */ }
                    uiOverlayField.SetValue(__instance, null);
                }
            }
            }
        }
        [HarmonyPatch(typeof(BaggedObject), "OnExit")]
        public class BaggedObject_OnExit
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (__instance == null) return true;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" [BaggedObject_OnExit] Prefix start for targetObject: {GetSafeName(__instance.targetObject)}");
                }

                // Check if we should keep the overrides (i.e. object is still being held/tracked)
                var bagController = __instance.outer?.GetComponent<DrifterBagController>();
                bool shouldKeepOverrides = false;
                
                if (bagController != null && __instance.targetObject != null)
                {
                    // Check if object is still tracked as main seat
                    bool isTrackedAsMain = BagPatches.mainSeatDict.TryGetValue(bagController, out var tracked) && ReferenceEquals(__instance.targetObject, tracked);
                    
                    // Check if object is physically in seat
                    bool isPhysicallyInSeat = bagController.vehicleSeat != null && bagController.vehicleSeat.hasPassenger && 
                                              ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, __instance.targetObject);
                                              
                    // We keep overrides if it's tracked or physically present, AND not dead/destroyed
                    bool isDeadCheck = false;
                    try { isDeadCheck = __instance.targetObject.GetComponent<HealthComponent>()?.alive == false; } catch { isDeadCheck = true; }
                    
                    if ((isTrackedAsMain || isPhysicallyInSeat) && !isDeadCheck && __instance.targetObject.activeInHierarchy)
                    {
                        shouldKeepOverrides = true;
                    }
                }

                if (shouldKeepOverrides)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                         Log.Info($" [BaggedObject_OnExit] Skipping UnsetAllOverrides - object {GetSafeName(__instance.targetObject)} is still tracked or in seat.");
                    }
                }
                else
                {
                    // ALWAYS attempt cleanup first if we are not persisting. 
                    // This ensures that even if we skip the original OnExit or it fails, the overrides are gone.
                    UnsetAllOverrides(__instance);
                }

                bool isSuppressed = false;
                if (__instance.targetObject)
                {
                    lock (_suppressedExitObjects)
                    {
                        if (_suppressedExitObjects.Contains(__instance.targetObject))
                        {
                            isSuppressed = true;
                            _suppressedExitObjects.Remove(__instance.targetObject);
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

                if (!__instance.targetObject)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] targetObject is null/destroyed, skipping original OnExit to prevent NRE (cleanup already attempted).");
                    }
                    RemoveWalkSpeedPenalty(__instance);
                    return false; 
                }

                bool isDead = false;
                try
                {
                    var hc = __instance.targetObject.GetComponent<HealthComponent>();
                    isDead = hc != null && !hc.alive;
                }
                catch
                {
                    isDead = true;
                }
                
                if (isDead)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit] targetObject is dead/dying ({GetSafeName(__instance.targetObject)}), skipping original OnExit to avoid crashes (cleanup already attempted).");
                    }
                    RemoveWalkSpeedPenalty(__instance);
                    return false; 
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    if (bagController != null)
                    {
                        Log.Info($" [BaggedObject_OnExit] bagController: {GetSafeName(bagController)}, hasAuthority: {bagController.hasAuthority}");
                        Log.Info($" [BaggedObject_OnExit] vehicleSeat hasPassenger: {bagController.vehicleSeat?.hasPassenger ?? false}");
                        Log.Info($" [BaggedObject_OnExit] vehicleSeat passenger: {GetSafeName(bagController.vehicleSeat?.NetworkpassengerBodyObject)}");
                        bool isTracked = BagPatches.mainSeatDict.TryGetValue(bagController, out var tracked) && ReferenceEquals(__instance.targetObject, tracked);
                        Log.Info($" [BaggedObject_OnExit] isTracked as main seat: {isTracked}, tracked: {GetSafeName(tracked)}");
                        bool inAdditionalSeat = BagPatches.GetAdditionalSeat(bagController, __instance.targetObject) != null;
                        Log.Info($" [BaggedObject_OnExit] inAdditionalSeat: {inAdditionalSeat}");
                    }
                }
                return true;
            }

            private static void UnsetAllOverrides(BaggedObject instance)
            {
                try
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($" [UnsetAllOverrides] Starting cleanup for instance {instance.GetHashCode()}");

                    // Method 1: Field-based cleanup (the standard way)
                    // Unset Utility
                    var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                    var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
                    
                    if (overriddenUtilityField != null && utilityOverrideField != null)
                    {
                        var overriddenUtility = (GenericSkill)overriddenUtilityField.GetValue(instance);
                        var utilityOverride = (SkillDef)utilityOverrideField.GetValue(instance);
                        
                        if (overriddenUtility != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) 
                                Log.Info($" [UnsetAllOverrides] Unsetting Utility override: {(utilityOverride ? utilityOverride.skillName : "null")} on skill: {(overriddenUtility ? overriddenUtility.skillName : "null")}");
                            
                            overriddenUtility.UnsetSkillOverride(instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                            overriddenUtilityField.SetValue(instance, null);
                        }
                    }

                    // Unset Primary
                    var overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
                    var primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");
                    
                    if (overriddenPrimaryField != null && primaryOverrideField != null)
                    {
                        var overriddenPrimary = (GenericSkill)overriddenPrimaryField.GetValue(instance);
                        var primaryOverride = (SkillDef)primaryOverrideField.GetValue(instance);
                        
                        if (overriddenPrimary != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) 
                                Log.Info($" [UnsetAllOverrides] Unsetting Primary override: {(primaryOverride ? primaryOverride.skillName : "null")} on skill: {(overriddenPrimary ? overriddenPrimary.skillName : "null")}");
                            
                            overriddenPrimary.UnsetSkillOverride(instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                            overriddenPrimaryField.SetValue(instance, null);
                        }
                    }

                    // Method 2: Nuclear Option - Scan the character's skills directly for any override sourced by this instance
                    var body = instance.outer?.GetComponent<CharacterBody>();
                    if (body && body.skillLocator)
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

                    var modifierField = AccessTools.Field(typeof(BaggedObject), "walkSpeedModifier");
                    if (modifierField != null)
                    {
                        var modifier = modifierField.GetValue(instance) as CharacterMotor.WalkSpeedPenaltyModifier;
                        if (modifier != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" [RemoveWalkSpeedPenalty] Manually removing walk speed penalty from {instance.outer.name}");
                            }
                            motor.RemoveWalkSpeedPenalty(modifier);
                            modifierField.SetValue(instance, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in RemoveWalkSpeedPenalty: {ex.Message}");
                }
            }

            private static void CleanupSkillFromLocator(BaggedObject instance, GenericSkill skill)
            {
                if (!skill) return;
                try
                {
                    // skill.skillOverrides is private List<GenericSkill.SkillOverride>
                    var overridesField = AccessTools.Field(typeof(GenericSkill), "skillOverrides");
                    var overridesList = (System.Collections.IList)overridesField.GetValue(skill);
                    if (overridesList == null) return;

                    // Iterate backwards to safely remove
                    for (int i = overridesList.Count - 1; i >= 0; i--)
                    {
                        var skillOverride = overridesList[i];
                        // skillOverride is a private struct GenericSkill.SkillOverride
                        var sourceField = skillOverride.GetType().GetField("source", BindingFlags.Public | BindingFlags.Instance);
                        var source = sourceField?.GetValue(skillOverride);
                        
                        if (ReferenceEquals(source, instance))
                        {
                            var skillDefField = skillOverride.GetType().GetField("skillDef", BindingFlags.Public | BindingFlags.Instance);
                            var skillDef = (SkillDef)skillDefField?.GetValue(skillOverride);
                            var priorityField = skillOverride.GetType().GetField("priority", BindingFlags.Public | BindingFlags.Instance);
                            var priority = (GenericSkill.SkillOverridePriority)(priorityField?.GetValue(skillOverride) ?? GenericSkill.SkillOverridePriority.Contextual);
                            
                            if (PluginConfig.Instance.EnableDebugLogs.Value) 
                                Log.Info($" [UnsetAllOverrides] NUCLEAR: Force removing override {(skillDef ? skillDef.skillName : "null")} from {skill.skillName} (sourced from {instance.GetHashCode()})");
                            
                            skill.UnsetSkillOverride(instance, skillDef, priority);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($" [UnsetAllOverrides] Nuclear cleanup failed for {skill.skillName}: {ex.Message}");
                }
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
                // But if the client has authority over the bag controller, don't remove UNLESS it is dead or destroyed
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
                        var holdsDeadBodyMethod = AccessTools.Method(typeof(BaggedObject), "HoldsDeadBody");
                        if (holdsDeadBodyMethod != null)
                        {
                            isDead = (bool)holdsDeadBodyMethod.Invoke(__instance, null);
                        }
                    }
                }
                catch (Exception)
                {
                }

                bool shouldRemove = isDead || isDestroyed;
                bool hasAuthority = bagController != null && bagController.hasAuthority;
                
                // Don't remove during swapping or auto-grab operations
                bool inSwapOrAutoGrab = DrifterBossGrabPlugin.IsSwappingPassengers || 
                                         Networking.CycleNetworkHandler.SuppressBroadcasts;
                if (inSwapOrAutoGrab && !shouldRemove)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Skipping removal for {GetSafeName(__instance.targetObject)} during swap/auto-grab operation");
                    }
                    return;
                }

                if (isTrackedAsMain && !inAdditionalSeat && !stillInAnySeat && (!hasAuthority || shouldRemove))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Removing {GetSafeName(__instance.targetObject)} from bag due to {(isDead ? "death" : (isDestroyed ? "destruction" : "exit from main seat"))}");
                    }
                    BagPatches.RemoveBaggedObject(bagController, __instance.targetObject);
                }
                else if (hasAuthority)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Not removing {GetSafeName(__instance.targetObject)} from bag because client has authority and not dead/destroyed (isDead: {isDead}, isDestroyed: {isDestroyed})");
                    }
                }
                else if (stillInAnySeat)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [BaggedObject_OnExit_Postfix] Object {GetSafeName(__instance.targetObject)} is still in a seat, not removing from bag");
                    }
                    // Update carousel since the object is still bagged
                    BagPatches.UpdateCarousel(bagController);
                }
            }
        }
        [HarmonyPatch(typeof(BaggedObject), "FixedUpdate")]
        public class BaggedObject_FixedUpdate
        {
            private static float _lastLogTime;
            private static System.Reflection.FieldInfo? _vehiclePassengerAttributesField;

            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance)
            {
                if (__instance == null || __instance.targetObject == null)
                {
                    return false; // Skip original FixedUpdate to prevent NRE
                }
                
                var isBodyField = AccessTools.Field(typeof(BaggedObject), "isBody");
                var isBody = (bool?)isBodyField?.GetValue(__instance);
                
                if (isBody == true)
                {
                    // If marked as body, verify the body object is still valid
                    var targetBodyField = AccessTools.Field(typeof(BaggedObject), "targetBody");
                    var targetBody = targetBodyField?.GetValue(__instance);
                    
                    if (targetBody == null || (targetBody is UnityEngine.Object obj && obj == null))
                    {
                        return false; // Skip if targetBody is lost/destroyed
                    }
                }
                else if (isBody == false)
                {
                    return false;
                }
                
                // Extra check for health
                try {
                     if (__instance.targetObject.GetComponent<HealthComponent>()?.alive == false) return false;
                } catch { return false; }

                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
                
                if (Time.time - _lastLogTime > 1.0f) // Log every second
                {
                    _lastLogTime = Time.time;
                    if (_vehiclePassengerAttributesField == null)
                    {
                        _vehiclePassengerAttributesField = typeof(BaggedObject).GetField("vehiclePassengerAttributes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    }
                    var vpa = _vehiclePassengerAttributesField?.GetValue(__instance) as SpecialObjectAttributes;
                    
                    Log.Info($" [BaggedObject_FixedUpdate] targetObject: {GetSafeName(__instance.targetObject)}, vehiclePassengerAttributes: {GetSafeName(vpa)}");
                    
                    var bagController = __instance.outer.GetComponent<DrifterBagController>();
                    if (bagController != null && bagController.vehicleSeat != null)
                    {
                         Log.Info($" [BaggedObject_FixedUpdate] Physical Seat Passenger: {GetSafeName(bagController.vehicleSeat.NetworkpassengerBodyObject)}");
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
        public static void LogUIOverlayState(string context, DrifterBagController bagController, GameObject targetObject)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
            Log.Info(" [DEBUG] " + context + " UI Overlay State Analysis");
            Log.Info(" [DEBUG]   context: " + context);
            Log.Info(" [DEBUG]   bagController: " + bagController);
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
        [HarmonyPatch(typeof(RoR2.VehicleSeat), "FixedUpdate")]
        public class VehicleSeat_FixedUpdate
        {
            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                // Suppress NRE from orphaned additional seats or invalid instances
                if (__exception is NullReferenceException) return null;
                return __exception;
            }
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
                            bool isTracked = false;
                            if (BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                            {
                                isTracked = list.Contains(passenger);
                            }

                            // Block reset if we have a passenger that is still considered "bagged"
                            // or if it is explicitly suppressed
                            // If it's not tracked, allow reset to Idle
                            if (isTracked || IsObjectExitSuppressed(passenger))
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" [SetNextStateToMain] BLOCKING reset for Bag state machine - passenger {GetSafeName(passenger)} present (Tracked: {isTracked}, Suppressed: {IsObjectExitSuppressed(passenger)})");
                                }
                                return false; // Prevent reset to Idle
                            }
                        }
                    }
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string customName = __instance?.customName ?? "null";
                    string gameObjectName = __instance?.gameObject?.name ?? "null";
                    string stateTypeName = __instance?.mainStateType.stateType?.Name ?? "null";
                    Log.Info($" [SetNextStateToMain] Called on {customName}, gameObject: {gameObjectName}, mainStateType: {stateTypeName}");
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
        private static BaggedObject? FindOrCreateBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
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
                Log.Info($" [FindOrCreateBaggedObjectState] Called for bagController: {GetSafeName(bagController)}, targetObject: {GetSafeName(targetObject)}");
                // Log current cached targetObject if exists
                if (_baggedObjectCache.TryGetValue(bagController, out var existingCached))
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] Cached BaggedObject targetObject: {GetSafeName(existingCached.targetObject)}");
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
                string cachedTargetName = GetSafeName(cachedTarget);
                bool isCachedTargetValid = cachedTarget != null;
                
                string targetObjectName = GetSafeName(targetObject);
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
                Log.Info($" [FindOrCreateBaggedObjectState] VehicleSeat determination: bagController.vehicleSeat = {GetSafeName(vehicleSeat)}");
                if (vehicleSeat != null)
                {
                    Log.Info($" [FindOrCreateBaggedObjectState] VehicleSeat hasPassenger: {vehicleSeat.hasPassenger}, NetworkpassengerBodyObject: {GetSafeName(vehicleSeat.NetworkpassengerBodyObject)}");
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
        private static BaggedObject? ForceCreateBaggedObjectState(DrifterBagController bagController, GameObject targetObject)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                // Log VehicleSeat determination
                var vehicleSeat = bagController.vehicleSeat;
                Log.Info($" [ForceCreateBaggedObjectState] VehicleSeat determination: bagController.vehicleSeat = {GetSafeName(vehicleSeat)}");
                if (vehicleSeat != null)
                {
                    Log.Info($" [ForceCreateBaggedObjectState] VehicleSeat hasPassenger: {vehicleSeat.hasPassenger}, NetworkpassengerBodyObject: {GetSafeName(vehicleSeat.NetworkpassengerBodyObject)}");
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




        [HarmonyPatch(typeof(EntityState), "OnEnter")]
        public class EntityState_OnEnter_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(EntityState __instance)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
                
                // Filter for likely relevant states to avoid spam
                string stateName = __instance.GetType().Name;
                if (stateName.Contains("Slam") || stateName.Contains("Bag") || stateName.Contains("Drifter"))
                {
                    Log.Info($" [EntityState_OnEnter] Entering State: {stateName} (Outer: {__instance.outer?.customName ?? "null"})");
                    
                    // Try to inspect fields if it's a Drifter state
                    if (stateName.Contains("Suffocate") || stateName.Contains("Slam"))
                    {
                        var controller = __instance.outer.GetComponent<DrifterBagController>();
                        if (controller)
                        {
                            var currentPassenger = controller.vehicleSeat?.NetworkpassengerBodyObject;
                             Log.Info($"    [State Context] Controller found. Current Seat Passenger: {GetSafeName(currentPassenger)}");
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(GenericSkill), "ExecuteIfReady")]
        public class GenericSkill_ExecuteIfReady_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(GenericSkill __instance)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;

                if (__instance.characterBody && __instance.characterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
                {
                     Log.Info($" [GenericSkill_ExecuteIfReady] Skill: {(__instance.skillDef ? __instance.skillDef.skillName : "null")} (Stock: {__instance.stock})");
                }
            }
        }

    }
}
