#nullable enable
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
                Log.DebugIfEnabled("[SaveObjectState] Saving state for {0}: baseMaxHealth={1}, mass={2}", obj.name, state.baseMaxHealth, state.baggedMass);

                // Prevent saving stub states that have default invalid values
                if (state.targetObject == null && state.baggedMass == 0f && state.baseMaxHealth == 0f)
                {
                    Log.DebugIfEnabled("[SaveObjectState] blocked saving stub state for {0} - has default invalid values", obj.name);
                    return;
                }

                if (!_perObjectStateStorage.TryGetValue(controller, out var objectStates))
                {
                    objectStates = new Dictionary<int, BaggedObjectStateData>();
                    _perObjectStateStorage[controller] = objectStates;
                }

                int instanceId = obj.GetInstanceID();
                if (objectStates.ContainsKey(instanceId))
                {
                    Log.DebugIfEnabled("[SaveObjectState] Overwriting existing state for {0}", obj.name);
                }

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
                        Log.DebugIfEnabled("[LoadObjectState] Loaded state for {0}: baseMaxHealth={1}, mass={2}", obj.name, state.baseMaxHealth, state.baggedMass);

                        // Warn if loading a stub state
                        if (state.targetObject == null && state.baggedMass == 0f && state.baseMaxHealth == 0f)
                        {
                            Log.Error($"[LoadObjectState] critical: Loaded stub state for {obj.name}!");
                            Log.Error($"[LoadObjectState] Stub state details: baseMaxHealth={state.baseMaxHealth}, mass={state.baggedMass}, targetObject={(!state.targetObject ? "null" : state.targetObject!.name)}");
                        }

                        return state;
                    }
                }

                Log.DebugIfEnabled("[LoadObjectState] No state found for {0}", obj.name);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($" [LoadObjectState] Error loading state: {ex.Message}");
                return null;
            }
        }

        // Finds the state data for a specific object across all bag controllers
        // obj: The game object to find state for
        // Returns: The saved state data, or null if not found
        public static BaggedObjectStateData? FindStateForObject(GameObject obj)
        {
            if (obj == null) return null;

            try
            {
                int instanceId = obj.GetInstanceID();
                foreach (var objectStates in _perObjectStateStorage.Values)
                {
                    if (objectStates.TryGetValue(instanceId, out var state))
                    {
                        return state;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($" [FindStateForObject] Error finding state: {ex.Message}");
                return null;
            }
        }

        // Removes the state data for a specific object from a bag controller
        // controller: The bag controller to cleanup state from
        // obj: The game object to remove state for
        // preserveForThrow: If true, preserves state for throw operations (prevents client-side state loss)
        public static void CleanupObjectState(DrifterBagController controller, GameObject obj, bool preserveForThrow = false)
        {
            if (controller == null || obj == null)
            {
                return;
            }

            if (preserveForThrow)
            {
                PreserveStateForThrow(controller, obj);
                return;
            }

            try
            {
                if (_perObjectStateStorage.TryGetValue(controller, out var objectStates))
                {
                    int instanceId = obj.GetInstanceID();
                    if (objectStates.Remove(instanceId))
                    {
                        Log.DebugIfEnabled("[CleanupObjectState] Removed state for object {0}", instanceId);
                    }

                }
            }
            catch (Exception ex)
            {
                Log.Error($" [CleanupObjectState] Error cleaning up state: {ex.Message}");
            }
        }

        // Temporary storage to preserve state during throw operations (prevents client-side state loss)
        private static Dictionary<DrifterBagController, Dictionary<int, BaggedObjectStateData>> _temporaryPreservedStates
            = new Dictionary<DrifterBagController, Dictionary<int, BaggedObjectStateData>>();

        // Preserve state before throw cleanup to prevent client-side state loss
        public static void PreserveStateForThrow(DrifterBagController controller, GameObject obj)
        {
            var state = LoadObjectState(controller, obj);
            if (state != null)
            {
                if (!_temporaryPreservedStates.TryGetValue(controller, out var tempStates))
                {
                    tempStates = new Dictionary<int, BaggedObjectStateData>();
                    _temporaryPreservedStates[controller] = tempStates;
                }
                int instanceId = obj.GetInstanceID();
                tempStates[instanceId] = state;
                Log.DebugIfEnabled("[PreserveStateForThrow] Preserved state for {0}", obj.name);
            }
        }

        // Restore preserved state during bag state update
        public static void RestorePreservedState(DrifterBagController controller, GameObject obj)
        {
            int instanceId = obj.GetInstanceID();
            if (_temporaryPreservedStates.TryGetValue(controller, out var tempStates) &&
                tempStates.TryGetValue(instanceId, out var preservedState))
            {
                if (!_perObjectStateStorage.TryGetValue(controller, out var objectStates))
                {
                    objectStates = new Dictionary<int, BaggedObjectStateData>();
                    _perObjectStateStorage[controller] = objectStates;
                }
                objectStates[instanceId] = preservedState;
                tempStates.Remove(instanceId);
                Log.DebugIfEnabled("[RestorePreservedState] Restored preserved state for {0}", obj.name);
            }
        }

        // Clear temporary preservation for specific object
        public static void ClearTemporaryPreservation(DrifterBagController controller, GameObject obj)
        {
            int instanceId = obj.GetInstanceID();
            if (_temporaryPreservedStates.TryGetValue(controller, out var tempStates))
            {
                tempStates.Remove(instanceId);
            }
        }

        // Clear all temporary preserved states for a controller
        public static void ClearAllTemporaryPreservation(DrifterBagController controller)
        {
            if (_temporaryPreservedStates.TryGetValue(controller, out var tempStates))
            {
                tempStates.Clear();
                _temporaryPreservedStates.Remove(controller);
            }
        }
    }
}
