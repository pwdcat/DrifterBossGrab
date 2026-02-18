using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using EntityStates.Drifter.Bag;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    // Harmony patches for controlling skill overrides on bagged objects
    public static class BaggedObjectSkillPatches
    {
        // Helper method to check if an object is in the main seat
        // bagController: The bag controller to check
        // targetObject: The target object to check
        // Returns: True if the object is in the main seat, false otherwise
        private static bool IsInMainSeat(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return false;

            // Check tracked main seat first (authoritative logical state)
            var trackedMainSeat = BagPatches.GetMainSeatObject(bagController);

            // If the controller is tracked in mainSeatDict, the entry (even if null) is logically authoritative.
            if (BagPatches.GetMainSeatObject(bagController) != null)
            {
                bool isTrackedAsMain = trackedMainSeat != null && ReferenceEquals(targetObject, trackedMainSeat);

                return isTrackedAsMain;
            }

            // Fallback to vehicle seat check only if not logically tracked (e.g. initial grab or vanilla behavior)
            var outerSeat = bagController!.vehicleSeat;
            if (outerSeat == null) return false;

            var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
            bool result = outerCurrentPassengerBodyObject != null && ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);

            // Also check if it's in an additional seat - if so, it's definitely not the main seat
            if (result && BagHelpers.GetAdditionalSeat(bagController, targetObject) != null)
            {
                result = false;
            }

            return result;
        }

        // Harmony patch for BaggedObject.TryOverrideUtility
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

                // Also allow if object is being cycled to main seat
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
                    var overriddenUtilityField = AccessTools.Field(typeof(BaggedObject), "overriddenUtility");
                    var utilityOverrideField = AccessTools.Field(typeof(BaggedObject), "utilityOverride");
                    var overriddenUtility = (GenericSkill)overriddenUtilityField.GetValue(__instance);

                    if (overriddenUtility != null)
                    {
                        var utilityOverride = (SkillDef)utilityOverrideField.GetValue(__instance);
                        overriddenUtility.UnsetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                        overriddenUtilityField.SetValue(__instance, null);

                    }
                    return false; // Skip the original method
                }
            }

        }

        // Harmony patch for BaggedObject.TryOverridePrimary
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

                // Also allow if object is being cycled to main seat
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
                    var overriddenPrimaryField = AccessTools.Field(typeof(BaggedObject), "overriddenPrimary");
                    var primaryOverrideField = AccessTools.Field(typeof(BaggedObject), "primaryOverride");
                    var overriddenPrimary = (GenericSkill)overriddenPrimaryField.GetValue(__instance);

                    if (overriddenPrimary != null)
                    {
                        var primaryOverride = (SkillDef)primaryOverrideField.GetValue(__instance);
                        overriddenPrimary.UnsetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                        overriddenPrimaryField.SetValue(__instance, null);

                    }
                    return false; // Skip the original method
                }
            }

        }
    }
}
