using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using RoR2.HudOverlay;
using RoR2.UI;
using UnityEngine;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Patches
{
    // Patches to control skill overrides and UI overlay for bagged objects.
    // Uses TryOverrideUtility and OnUIOverlayInstanceAdded patches
    [HarmonyPatch]
    public static class BaggedObjectPatches
    {
        [HarmonyPrepare]
        public static void Prepare()
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} BaggedObjectPatches prepared - Using TryOverrideUtility and OnUIOverlayInstanceAdded approach");
            }
        }

        // Refresh the UI overlay for an object that has moved to the main seat.
        // This forces the overlay to be recreated with the updated seat status.
        public static void RefreshUIOverlayForMainSeat(GameObject targetObject)
        {
            if (targetObject == null) return;
            
            var baggedObject = targetObject.GetComponent<BaggedObject>();
            if (baggedObject == null) return;
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [RefreshUIOverlay] Refreshing UI overlay for {targetObject.name} (moved to main seat)");
            }
            
            // Get the DrifterBagController to check if this object is now in the main seat
            var bagController = baggedObject.outer.GetComponent<DrifterBagController>();
            if (bagController == null) return;
            
            var vehicleSeat = bagController.vehicleSeat;
            var currentPassengerBody = vehicleSeat?.currentPassengerBody;
            var mainSeatOccupant = currentPassengerBody?.gameObject;
            
            bool isNowMainSeatOccupant = ReferenceEquals(targetObject, mainSeatOccupant);
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [RefreshUIOverlay] isNowMainSeatOccupant={isNowMainSeatOccupant}");
            }
            
            if (!isNowMainSeatOccupant) return;
            
            // Remove any existing overlay controller reference
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var existingController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            
            if (existingController != null)
            {
                HudOverlayManager.RemoveOverlay(existingController);
                uiOverlayField.SetValue(baggedObject, null);
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [RefreshUIOverlay] Removed existing overlay");
                }
            }
            
            // Force a skill override check by calling TryOverrideUtility with the utility skill
            // This will set up the skill override and trigger UI overlay creation
            var skillLocator = targetObject.GetComponent<SkillLocator>();
            if (skillLocator != null && skillLocator.utility != null)
            {
                // Force the utility skill to be reconsidered for override
                var tryOverrideMethod = AccessTools.Method(typeof(BaggedObject), "TryOverrideUtility");
                if (tryOverrideMethod != null)
                {
                    // Call with skip prefix to avoid the main seat check (since we know it's in main seat now)
                    tryOverrideMethod.Invoke(baggedObject, new object[] { skillLocator.utility });
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [RefreshUIOverlay] Called TryOverrideUtility to refresh UI");
                    }
                }
            }
            else
            {
                // Fallback: Try to call UpdateOverlay directly if it exists
                var updateOverlayMethod = AccessTools.Method(typeof(BaggedObject), "UpdateOverlay");
                if (updateOverlayMethod != null)
                {
                    updateOverlayMethod.Invoke(baggedObject, null);
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [RefreshUIOverlay] Called UpdateOverlay");
                    }
                }
            }
        }

        // Remove the UI overlay for an object that has left the main seat.
        public static void RemoveUIOverlay(GameObject targetObject)
        {
            if (targetObject == null) return;
            
            var baggedObject = targetObject.GetComponent<BaggedObject>();
            if (baggedObject == null) return;
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [RemoveUIOverlay] Removing UI overlay for {targetObject.name} (leaving main seat)");
            }
            
            // Remove any existing overlay controller
            var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
            var existingController = (OverlayController)uiOverlayField.GetValue(baggedObject);
            
            if (existingController != null)
            {
                HudOverlayManager.RemoveOverlay(existingController);
                uiOverlayField.SetValue(baggedObject, null);
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [RemoveUIOverlay] Removed overlay for {targetObject.name}");
                }
            }
        }

        // Patch TryOverrideUtility to only allow skill overrides for main vehicle seat objects
        // This controls the skill icon override behavior
        [HarmonyPatch(typeof(BaggedObject), "TryOverrideUtility")]
        public class BaggedObject_TryOverrideUtility
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] ========== CALLED ==========");
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] skill={skill?.skillFamily?.name ?? "null"}, targetObject={__instance.targetObject?.name ?? "null"}");
                }

                // Get the DrifterBagController
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] No DrifterBagController found, allowing");
                    }
                    return true; // Allow normal execution
                }

                // Check vehicle seat state
                var vehicleSeat = bagController.vehicleSeat;
                bool hasPassenger = vehicleSeat?.hasPassenger ?? false;
                var currentPassengerBody = vehicleSeat?.currentPassengerBody;
                var mainSeatOccupant = currentPassengerBody?.gameObject;
                var targetObject = __instance.targetObject;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] targetObject: {targetObject?.name ?? "null"} (inst={targetObject?.GetInstanceID() ?? 0})");
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] mainSeatOccupant: {mainSeatOccupant?.name ?? "null"} (inst={mainSeatOccupant?.GetInstanceID() ?? 0})");
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] vehicleSeat hasPassenger={hasPassenger}");
                }

                // Check if the target object is the main seat occupant
                // First check direct reference (works if currentPassengerBody is already set)
                bool isMainSeatOccupant = ReferenceEquals(targetObject, mainSeatOccupant);

                // If direct check failed and main seat has a passenger but currentPassengerBody is not yet set (timing issue),
                // check if the target object is in our tracked list for the main seat
                if (!isMainSeatOccupant && hasPassenger && mainSeatOccupant == null)
                {
                    // Check if this object is in the tracked bagged objects list (it was just assigned to main seat)
                    if (BagPatches.baggedObjectsDict.TryGetValue(bagController, out var trackedObjects))
                    {
                        isMainSeatOccupant = trackedObjects.Contains(targetObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] Using tracked list fallback: isMainSeatOccupant={isMainSeatOccupant}");
                        }
                    }
                }

                if (isMainSeatOccupant)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] ✓ ALLOWING skill override for {targetObject?.name} (main seat)");
                    }
                    return true; // Allow normal execution
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] ✗ SKIPPING skill override for {targetObject?.name} (NOT main seat)");
                    }
                    return false; // Skip the original method - no skill override
                }
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, GenericSkill skill)
            {
                if (!PluginConfig.EnableDebugLogs.Value) return;

                // Check if override was actually set
                var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                var overriddenUtility = (GenericSkill)overriddenUtilityField.GetValue(__instance);

                if (overriddenUtility != null)
                {
                    var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
                    var utilityOverride = (SkillDef)utilityOverrideField.GetValue(__instance);
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] Postfix: Override SET for {__instance.targetObject?.name}, skillDef={utilityOverride?.name ?? "null"}");
                }
                else
                {
                    Log.Info($"{Constants.LogPrefix} [TryOverrideUtility] Postfix: Override NOT set (prefix skipped it)");
                }
            }
        }

        [HarmonyPatch(typeof(BaggedObject), "OnUIOverlayInstanceAdded")]
        public class BaggedObject_OnUIOverlayInstanceAdded
        {
            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, OverlayController controller, GameObject instance)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] ========== CALLED ==========");
                    Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] targetObject={__instance.targetObject?.name ?? "null"}, controller={controller != null}");
                }

                // Get the DrifterBagController
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] No DrifterBagController found, allowing default behavior");
                    }
                    return;
                }

                // Check vehicle seat state
                var vehicleSeat = bagController.vehicleSeat;
                bool hasPassenger = vehicleSeat?.hasPassenger ?? false;
                var currentPassengerBody = vehicleSeat?.currentPassengerBody;
                var mainSeatOccupant = currentPassengerBody?.gameObject;
                var targetObject = __instance.targetObject;

                bool isMainSeatOccupant = ReferenceEquals(targetObject, mainSeatOccupant);

                // If direct check failed and main seat has a passenger but currentPassengerBody is not yet set (timing issue),
                // check if the target object is in our tracked list for the main seat
                if (!isMainSeatOccupant && hasPassenger && mainSeatOccupant == null)
                {
                    // Check if this object is in the tracked bagged objects list (it was just assigned to main seat)
                    if (BagPatches.baggedObjectsDict.TryGetValue(bagController, out var trackedObjects))
                    {
                        isMainSeatOccupant = trackedObjects.Contains(targetObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] Using tracked list fallback: isMainSeatOccupant={isMainSeatOccupant}");
                        }
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] targetObject: {targetObject?.name ?? "null"} (inst={targetObject?.GetInstanceID() ?? 0})");
                    Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] mainSeatOccupant: {mainSeatOccupant?.name ?? "null"} (inst={mainSeatOccupant?.GetInstanceID() ?? 0})");
                    Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] isMainSeatOccupant={isMainSeatOccupant}");
                }

                // Only allow UI updates for main seat occupant
                if (isMainSeatOccupant)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] ✓ ALLOWING UI update for {targetObject?.name} (main seat)");
                    }
                    // Let the original method run - it will set the UI attributes
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] ✗ SKIPPING UI update for {targetObject?.name} (NOT main seat) - removing overlay");
                    }

                    // Remove the overlay entirely to avoid showing null/X icons
                    // Also null out the uiOverlayController field so OnExit doesn't try to remove it again
                    if (controller != null)
                    {
                        HudOverlayManager.RemoveOverlay(controller);
                        
                        // Null out the field to prevent OnExit from trying to remove again
                        var uiOverlayField = AccessTools.Field(typeof(BaggedObject), "uiOverlayController");
                        uiOverlayField.SetValue(__instance, null);
                        
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [OnUIOverlayInstanceAdded] Removed overlay and nulled uiOverlayController for {targetObject?.name}");
                        }
                    }
                }
            }
        }

        public static GameObject GetMainSeatOccupant(DrifterBagController controller)
        {
            if (controller == null || controller.vehicleSeat == null) return null;
            if (!controller.vehicleSeat.hasPassenger) return null;
            return controller.vehicleSeat.currentPassengerBody?.gameObject;
        }

        [HarmonyPatch(typeof(RoR2.VehicleSeat), "EjectPassenger", new Type[] { typeof(GameObject) })]
        public class VehicleSeat_EjectPassenger
        {
            [HarmonyPostfix]
            public static void Postfix(RoR2.VehicleSeat __instance, GameObject bodyObject)
            {
                if (bodyObject == null || !PluginConfig.EnableDebugLogs.Value) return;

                Log.Info($"{Constants.LogPrefix} [EjectPassenger] Ejected passenger: {bodyObject.name}");
                Log.Info($"{Constants.LogPrefix} [EjectPassenger] seat name: {__instance.gameObject.name}");
            }
        }
    }
}
