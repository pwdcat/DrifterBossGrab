using System;
using System.Collections;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod.API
{
    public static class DrifterBagAPI
    {
        // Returns a list of all objects currently stored in the bag for the given controller.
        public static List<GameObject> GetBaggedObjects(DrifterBagController controller)
        {
            if (controller == null) return new List<GameObject>();
            return new List<GameObject>(BagPatches.GetState(controller).BaggedObjects ?? new List<GameObject>());
        }

        // Returns the number of objects currently in the bag.
        public static int GetBagCount(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetCurrentBaggedCount(controller);
        }

        // Returns the maximum capacity of the bag in slots.
        // Returns int.MaxValue if the bag is effectively bottomless.
        public static int GetBagCapacity(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetUtilityMaxStock(controller);
        }

        // Returns true if the bag has room for at least one more object.
        public static bool HasRoom(DrifterBagController controller)
        {
            return BagCapacityCalculator.HasRoomForGrab(controller);
        }

        // Returns the total mass of all objects currently in the bag.
        public static float GetTotalMass(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetBaggedObjectMass(controller);
        }

        // Returns the mass of a specific object in the bag.
        // Returns 0 if the object is null or not in the bag.
        public static float GetObjectMass(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return 0f;
            return controller.CalculateBaggedObjectMass(obj);
        }

        // Returns the display name of a bagged object (either from CharacterBody or GameObject name).
        public static string GetObjectName(GameObject obj)
        {
            if (obj == null) return "Unknown";
            var body = obj.GetComponent<CharacterBody>();
            if (body != null) return body.GetDisplayName();
            return obj.name;
        }

        // Returns the portrait icon of a bagged object if available.
        public static Texture? GetObjectIcon(GameObject obj)
        {
            if (obj == null) return null;
            var body = obj.GetComponent<CharacterBody>();
            if (body != null && body.portraitIcon != null) return body.portraitIcon;

            var attributes = obj.GetComponent<SpecialObjectAttributes>();
            if (attributes != null && attributes.portraitIcon != null) return attributes.portraitIcon;

            return null;
        }

        // Returns true if the specified object is currently in the bag.
        public static bool IsObjectInBag(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            var list = BagPatches.GetState(controller).BaggedObjects;
            return list != null && list.Contains(obj);
        }

        // Returns the object currently in the main seat of the bag.
        public static GameObject? GetMainPassenger(DrifterBagController controller)
        {
            return BagPatches.GetMainSeatObject(controller);
        }

        // Checks if an object name is blacklisted from being grabbed.
        public static bool IsBlacklisted(string objectName)
        {
            return PluginConfig.IsBlacklisted(objectName);
        }

        // Swaps the current main seat object with an object already in the bag.
        // Returns True if the object was found in the bag and the swap was scheduled.
        public static bool SetMainPassenger(DrifterBagController controller, GameObject objRef)
        {
            if (controller == null || objRef == null) return false;
            
            var list = GetBaggedObjects(controller);
            if (!list.Contains(objRef)) return false;

            if (GetMainPassenger(controller) == objRef) return true;

            DelayedAutoPromote.Schedule(controller, objRef, 0f);
            return true;
        }

        // Programmatically adds an object to the Drifter's bag.
        // This handles setting up necessary components (SpecialObjectAttributes, etc.) before assignment.
        // Returns True if the object was successfully assigned to the bag.
        public static bool AddBaggedObject(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;

            // Ensure the object is prepared for grabbing (adds SpecialObjectAttributes, ESM, etc.)
            GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);

            // Trigger the assignment logic
            controller.AssignPassenger(obj);
            return true;
        }

        // Removes a specific object from the bag.
        // If isDestroying is true, treats the removal as the object being destroyed (e.g. consumed). Otherwise treats it as a release/throw.
        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false)
        {
            if (controller == null || obj == null) return;
            BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying);
        }

        // Forces a recalculation of the bag's mass and updates the Drifter's stats accordingly.
        // Use this after modifying the bag contents manually if not using the Add/Remove methods above.
        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPassengerManager.ForceRecalculateMass(controller);
        }

        // Removes all objects from the bag.
        public static void ClearBag(DrifterBagController controller, bool isDestroying = false)
        {
            if (controller == null) return;
            var list = GetBaggedObjects(controller);
            foreach (var obj in list)
            {
                RemoveBaggedObject(controller, obj, isDestroying);
            }
        }

        // Schedules an object to be auto-grabbed after a delay.
        public static void ScheduleAutoGrab(DrifterBagController controller, GameObject obj, float delay = 0.5f)
        {
            if (controller == null || obj == null) return;
            
            // Create a coroutine runner to execute the delayed grab
            var coroutineRunner = new GameObject("AutoGrabRunner_" + obj.GetInstanceID());
            var runner = coroutineRunner.AddComponent<AutoGrabCoroutineRunner>();
            runner.StartCoroutine(DelayedAutoGrabCoroutine(controller, obj, delay));
        }

        private static IEnumerator DelayedAutoGrabCoroutine(DrifterBagController controller, GameObject obj, float delay)
        {
            // Wait for object to fully initialize
            yield return new WaitForSeconds(delay);

            // Check if object still exists and is valid
            if (obj != null && obj.activeInHierarchy)
            {
                // Use the existing AddBaggedObject method which handles all the setup
                AddBaggedObject(controller, obj);
            }
        }

        // Helper class to run coroutines for auto-grab
        private class AutoGrabCoroutineRunner : MonoBehaviour
        {
            public IEnumerator? runningCoroutine;
            
            public new void StartCoroutine(IEnumerator coroutine)
            {
                runningCoroutine = coroutine;
                base.StartCoroutine(coroutine);
            }

            private void OnDestroy()
            {
                if (runningCoroutine != null)
                {
                    StopCoroutine(runningCoroutine);
                }
            }
        }
    }
}
