#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Config;
using DrifterBossGrabMod.Balance;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.API
{
    // drifter bag encumbrance level
    public enum EncumbranceLevel
    {
        None,       // < 50% capacity
        Light,      // 50-75% capacity
        Heavy,      // 75-100% capacity
        Over        // > 100% capacity
    }

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

        // Programmatically adds an object to the Drifter's bag
        public static bool AddBaggedObject(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;

            // Ensure the object is prepared for grabbing (adds SpecialObjectAttributes, ESM, etc.)
            GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);

            // Suppress the accidental throw during automated assignments
            BaggedObjectPatches.SuppressExitForObject(obj);

            // Trigger the assignment logic
            controller.AssignPassenger(obj);

            // If this object is now in the main seat, we must transition the state machine
            // to BaggedObject so skill overrides and UI updates are applied.
            if (BagPatches.GetMainSeatObject(controller) == obj)
            {
                var targetBody = controller.GetComponentInParent<CharacterBody>();
                if (targetBody != null)
                {
                    var bagStateMachine = EntityStateMachine.FindByCustomName(targetBody.gameObject, "Bag");
                    if (bagStateMachine != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[DrifterBagAPI] Setting BaggedObject state on {targetBody.name} for {obj.name}");
                        }
                        var baggedObjectState = new BaggedObject();
                        baggedObjectState.targetObject = obj;
                        bagStateMachine.SetNextState(baggedObjectState);
                    }
                }
            }

            return true;
        }

        // Removes a specific object from the bag.
        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false)
        {
            if (controller == null || obj == null) return;
            BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying);
        }

        // Forces a recalculation of the bag's mass and updates the Drifter's stats accordingly.
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
            InvokeOnBagCleared(controller, isDestroying);
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

        #region Encumbrance and Status Queries

        // get mass ratio (total mass / capacity)
        // returns 0 if infinite capacity or null controller
        // values > 1 indicate overencumbrance
        public static float GetMassRatio(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            float totalMass = GetTotalMass(controller);
            float capacity = GetMassCapacity(controller);
            if (capacity == float.MaxValue || capacity <= 0) return 0f;
            return totalMass / capacity;
        }

        // get mass capacity for the given controller
        // maximum total mass the bag can hold
        public static float GetMassCapacity(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            // Use the mass cap formula result
            return Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
        }

        // get encumbrance level for the given controller
        public static EncumbranceLevel GetEncumbranceLevel(DrifterBagController controller)
        {
            float ratio = GetMassRatio(controller);
            if (ratio < 0.5f) return EncumbranceLevel.None;
            if (ratio < 0.75f) return EncumbranceLevel.Light;
            if (ratio < 1.0f) return EncumbranceLevel.Heavy;
            return EncumbranceLevel.Over;
        }

        // check if controller is overencumbered
        public static bool IsOverencumbered(DrifterBagController controller)
        {
            return GetMassRatio(controller) > 1.0f;
        }

        // get current move speed penalty multiplier
        // returns 1.0 (no penalty) if controller is null or balance disabled
        public static float GetMoveSpeedPenalty(DrifterBagController controller)
        {
            if (controller == null) return 1.0f;
            // Access the current penalty calculation
            return Core.StateCalculator.CalculateMovespeedPenalty(controller, GetTotalMass(controller));
        }

        // get current damage multiplier
        // returns 1.0 (no change) if controller is null or balance disabled
        public static float GetDamageMultiplier(DrifterBagController controller)
        {
            if (controller == null) return 1.0f;
            // Access the current damage calculation
            return Core.SlamDamageCalculator.GetEffectiveCoefficient(controller);
        }

        #endregion

        #region Formula Variable Registry API

        // register static formula variable for balance formulas
        // name: variable name case-insensitive
        // value: constant value
        // description: optional info
        public static void RegisterFormulaVariable(string name, float value, string? description = null)
        {
            Balance.FormulaRegistry.RegisterVariable(name, value, description);
        }

        // register dynamic formula variable evaluated when needed
        // name: variable name case-insensitive
        // provider: function returning value given CharacterBody
        // description: optional info
        // fallbackValue: value if provider throws
        public static void RegisterFormulaVariable(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null)
        {
            Balance.FormulaRegistry.RegisterVariable(name, provider, description, fallbackValue);
        }

        // get names of all registered formula variables
        public static IEnumerable<string> GetFormulaVariableNames()
        {
            return Balance.FormulaRegistry.GetRegisteredVariableNames();
        }

        // unregister formula variable by name
        // name: variable name case-insensitive
        // returns true if found and removed
        public static bool UnregisterFormulaVariable(string name)
        {
            return Balance.FormulaRegistry.UnregisterVariable(name);
        }

        // check if formula variable is registered
        // name: variable name case-insensitive
        // returns true if registered
        public static bool IsFormulaVariableRegistered(string name)
        {
            return Balance.FormulaRegistry.IsVariableRegistered(name);
        }

        // get info about registered formula variable
        // name: variable name case-insensitive
        // returns VariableInfo or null
        public static VariableInfo? GetFormulaVariableInfo(string name)
        {
            return Balance.FormulaRegistry.GetVariableInfo(name);
        }

        #endregion

        #region Filtered Queries

        // get all bagged objects with specific component type
        public static List<GameObject> GetBaggedObjectsByComponent<T>(DrifterBagController controller) where T : Component
        {
            var result = new List<GameObject>();
            foreach (var obj in GetBaggedObjects(controller))
            {
                if (obj.GetComponent<T>() != null)
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        // get all bagged objects with CharacterBody
        public static List<GameObject> GetBaggedCharacterBodies(DrifterBagController controller)
        {
            return GetBaggedObjectsByComponent<CharacterBody>(controller);
        }

        // get all bagged objects whose names contain substring case-insensitive
        public static List<GameObject> GetBaggedObjectsByName(DrifterBagController controller, string nameContains)
        {
            var result = new List<GameObject>();
            foreach (var obj in GetBaggedObjects(controller))
            {
                if (obj.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        // get all bagged objects with exact name case-insensitive
        public static List<GameObject> GetBaggedObjectsByExactName(DrifterBagController controller, string exactName)
        {
            var result = new List<GameObject>();
            foreach (var obj in GetBaggedObjects(controller))
            {
                if (string.Equals(obj.name, exactName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        // get all bagged objects within mass range
        public static List<GameObject> GetBaggedObjectsByMassRange(DrifterBagController controller, float minMass, float maxMass)
        {
            var result = new List<GameObject>();
            foreach (var obj in GetBaggedObjects(controller))
            {
                float mass = GetObjectMass(controller, obj);
                if (mass >= minMass && mass <= maxMass)
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        // get heaviest object in bag or null if empty
        public static GameObject? GetHeaviestObject(DrifterBagController controller)
        {
            GameObject? heaviest = null;
            float maxMass = 0f;
            
            foreach (var obj in GetBaggedObjects(controller))
            {
                float mass = GetObjectMass(controller, obj);
                if (mass > maxMass)
                {
                    maxMass = mass;
                    heaviest = obj;
                }
            }
            
            return heaviest;
        }

        // get lightest object in bag or null if empty
        public static GameObject? GetLightestObject(DrifterBagController controller)
        {
            GameObject? lightest = null;
            float minMass = float.MaxValue;
            
            foreach (var obj in GetBaggedObjects(controller))
            {
                float mass = GetObjectMass(controller, obj);
                if (mass < minMass)
                {
                    minMass = mass;
                    lightest = obj;
                }
            }
            
            return lightest;
        }

        #endregion

        #region Utility Methods - Atomic Operations

        // try to grab object checking for room first
        // returns true if successful
        public static bool TryGrab(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            if (!HasRoom(controller)) return false;
            return AddBaggedObject(controller, obj);
        }

        // try to release main passenger
        // returns true if object released
        public static bool TryReleaseMainPassenger(DrifterBagController controller)
        {
            if (controller == null) return false;
            var mainPassenger = GetMainPassenger(controller);
            if (mainPassenger == null) return false;
            RemoveBaggedObject(controller, mainPassenger, false);
            return true;
        }

        // try to release all objects of specific type
        // returns number of objects released
        public static int ReleaseObjectsByType<T>(DrifterBagController controller) where T : Component
        {
            if (controller == null) return 0;
            var objects = GetBaggedObjectsByComponent<T>(controller);
            int count = 0;
            foreach (var obj in objects)
            {
                RemoveBaggedObject(controller, obj, false);
                count++;
            }
            return count;
        }

        #endregion

        #region Utility Methods - Summary Methods

        // get formatted string summary of bag contents
        // format: "Bag: [Count]/[Capacity] | Mass: [Total]/[Max] ([Ratio]%)"
        // example: "Bag: 5/10 | Mass: 250/500 (50%)"
        public static string GetFormattedBagSummary(DrifterBagController controller)
        {
            if (controller == null) return "Bag: N/A";
            
            int count = GetBagCount(controller);
            int capacity = GetBagCapacity(controller);
            float totalMass = GetTotalMass(controller);
            float massCap = GetMassCapacity(controller);
            float ratio = GetMassRatio(controller);
            
            string countStr = capacity == int.MaxValue ? $"{count}/∞" : $"{count}/{capacity}";
            string massCapStr = massCap == float.MaxValue ? "∞" : massCap.ToString("F0");
            
            return $"Bag: {countStr} | Mass: {totalMass:F0}/{massCapStr} ({ratio:P0})";
        }

        // get detailed summary of bagged objects with names and masses
        // format: "1. [Name] ([Mass]kg)"
        public static List<string> GetBaggedObjectDetails(DrifterBagController controller)
        {
            var details = new List<string>();
            int index = 1;
            foreach (var obj in GetBaggedObjects(controller))
            {
                string name = GetObjectName(obj);
                float mass = GetObjectMass(controller, obj);
                details.Add($"{index}. {name} ({mass:F1}kg)");
                index++;
            }
            return details;
        }

        // get dictionary mapping object names to counts
        // useful for displaying "3 Beetles, 2 Lemurians, etc"
        public static Dictionary<string, int> GetBaggedObjectCounts(DrifterBagController controller)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in GetBaggedObjects(controller))
            {
                string name = GetObjectName(obj);
                if (!counts.ContainsKey(name))
                {
                    counts[name] = 0;
                }
                counts[name]++;
            }
            return counts;
        }

        #endregion

        #region Events

        // fired when object is grabbed and added to bag
        // controller: bag controller that grabbed object
        // obj: object that was grabbed
        // slotIndex: assigned slot index (-1 if not applicable)
        public static event Action<DrifterBagController, GameObject, int>? OnObjectGrabbed;

        // fired when object is released or ejected
        // controller: bag controller that released object
        // obj: object that was released
        // wasDestroyed: true if destroyed/consumed else false
        public static event Action<DrifterBagController, GameObject, bool>? OnObjectReleased;

        // fired when bag reaches capacity
        // controller: full bag controller
        public static event Action<DrifterBagController>? OnBagFull;

        // fired when bag becomes overencumbered
        // controller: overencumbered bag controller
        // massRatio: current mass ratio > 1.0
        public static event Action<DrifterBagController, float>? OnOverencumbered;

        // fired when bag is cleared
        // controller: cleared bag controller
        // wasDestroyed: true if objects destroyed false if released
        public static event Action<DrifterBagController, bool>? OnBagCleared;

        // fired when main passenger changes
        // controller: bag controller
        // previousObj: previous active object or null
        // newObj: new active object or null
        public static event Action<DrifterBagController, GameObject?, GameObject?>? OnMainPassengerChanged;

        // fired when mass is recalculated
        // controller: bag controller
        // newTotalMass: new mass
        // previousTotalMass: old mass
        public static event Action<DrifterBagController, float, float>? OnMassRecalculated;

        #endregion

        #region Internal Event Invokers

        // Internal helper methods to invoke events from other classes
        internal static void InvokeOnObjectGrabbed(DrifterBagController controller, GameObject obj, int slotIndex)
        {
            OnObjectGrabbed?.Invoke(controller, obj, slotIndex);
        }

        internal static void InvokeOnObjectReleased(DrifterBagController controller, GameObject obj, bool wasDestroyed)
        {
            OnObjectReleased?.Invoke(controller, obj, wasDestroyed);
        }

        internal static void InvokeOnBagFull(DrifterBagController controller)
        {
            OnBagFull?.Invoke(controller);
        }

        internal static void InvokeOnOverencumbered(DrifterBagController controller, float massRatio)
        {
            OnOverencumbered?.Invoke(controller, massRatio);
        }

        internal static void InvokeOnBagCleared(DrifterBagController controller, bool wasDestroyed)
        {
            OnBagCleared?.Invoke(controller, wasDestroyed);
        }

        internal static void InvokeOnMainPassengerChanged(DrifterBagController controller, GameObject? previousObj, GameObject? newObj)
        {
            OnMainPassengerChanged?.Invoke(controller, previousObj, newObj);
        }

        internal static void InvokeOnMassRecalculated(DrifterBagController controller, float newTotalMass, float previousTotalMass)
        {
            OnMassRecalculated?.Invoke(controller, newTotalMass, previousTotalMass);
        }

        #endregion

        #region Serialization and Save/Load API

        // Registers a custom object serializer plugin with the save/load system
        public static void RegisterSerializerPlugin(ProperSave.Serializers.IObjectSerializerPlugin plugin)
        {
            ProperSave.ProperSaveIntegration.RegisterPlugin(plugin);
        }

        // Returns a list of all currently registered object serializer plugins.
        public static List<ProperSave.Serializers.IObjectSerializerPlugin> GetSerializerPlugins()
        {
            return ProperSave.ProperSaveIntegration.GetSerializerPlugins();
        }

        #endregion
    }
}
