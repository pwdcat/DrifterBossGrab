using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    // Helper class for managing per-object state storage for bagged objects
    public static class BaggedObjectStateStorage
    {
        // Store per-controller, per-object state data
        private static Dictionary<DrifterBagController, Dictionary<int, BaggedObjectStateData>> _perObjectStateStorage
            = new Dictionary<DrifterBagController, Dictionary<int, BaggedObjectStateData>>();

        // Saves the state data for a specific object in a bag controller
        // controller: The bag controller storing the state
        // obj: The game object to save state for
        // state: The state data to save
        public static void SaveObjectState(DrifterBagController controller, GameObject obj, BaggedObjectStateData state)
        {
            if (controller == null || obj == null || state == null)
            {
                return;
            }

            try
            {
                if (!_perObjectStateStorage.TryGetValue(controller, out var objectStates))
                {
                    objectStates = new Dictionary<int, BaggedObjectStateData>();
                    _perObjectStateStorage[controller] = objectStates;
                }

                int instanceId = obj.GetInstanceID();
                objectStates[instanceId] = state;
            }
            catch (Exception ex)
            {
                Log.Error($" [SaveObjectState] Error saving state: {ex.Message}");
            }
        }

        // Loads the state data for a specific object from a bag controller
        // controller: The bag controller to load state from
        // obj: The game object to load state for
        // Returns: The saved state data, or null if not found
        public static BaggedObjectStateData? LoadObjectState(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null)
            {
                return null;
            }

            try
            {
                if (_perObjectStateStorage.TryGetValue(controller, out var objectStates))
                {
                    int instanceId = obj.GetInstanceID();
                    if (objectStates.TryGetValue(instanceId, out var state))
                    {
                        return state;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error($" [LoadObjectState] Error loading state: {ex.Message}");
                return null;
            }
        }

        // Removes the state data for a specific object from a bag controller
        // controller: The bag controller to cleanup state from
        // obj: The game object to remove state for
        public static void CleanupObjectState(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null)
            {
                return;
            }

            try
            {
                if (_perObjectStateStorage.TryGetValue(controller, out var objectStates))
                {
                    int instanceId = obj.GetInstanceID();
                    if (objectStates.Remove(instanceId))
                    {
                        Log.Debug($" [CleanupObjectState] Removed state for object {instanceId}");
                    }

                }
            }
            catch (Exception ex)
            {
                Log.Error($" [CleanupObjectState] Error cleaning up state: {ex.Message}");
            }
        }
    }
}
