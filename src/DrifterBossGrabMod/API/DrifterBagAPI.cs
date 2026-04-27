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
    public enum EncumbranceLevel
    {
        None,
        Light,
        Heavy,
        Over
    }

    public static class DrifterBagAPI
    {
        public static List<GameObject> GetBaggedObjects(DrifterBagController controller)
        {
            if (controller == null) return new List<GameObject>();
            return new List<GameObject>(BagPatches.GetState(controller).BaggedObjects ?? new List<GameObject>());
        }

        public static int GetBagCount(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetCurrentBaggedCount(controller);
        }

        public static int GetBagCapacity(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetUtilityMaxStock(controller);
        }
        public static bool HasRoom(DrifterBagController controller)
        {
            return BagCapacityCalculator.HasRoomForGrab(controller);
        }

        public static float GetTotalMass(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetBaggedObjectMass(controller);
        }

        public static float GetObjectMass(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return 0f;
            return controller.CalculateBaggedObjectMass(obj);
        }

        public static string GetObjectName(GameObject obj)
        {
            if (obj == null) return "Unknown";
            var body = obj.GetComponent<CharacterBody>();
            if (body != null) return body.GetDisplayName();
            return obj.name;
        }

        public static Texture? GetObjectIcon(GameObject obj)
        {
            if (obj == null) return null;
            var body = obj.GetComponent<CharacterBody>();
            if (body != null && body.portraitIcon != null) return body.portraitIcon;

            var attributes = obj.GetComponent<SpecialObjectAttributes>();
            if (attributes != null && attributes.portraitIcon != null) return attributes.portraitIcon;

            return null;
        }

        public static bool IsObjectInBag(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            var list = BagPatches.GetState(controller).BaggedObjects;
            return list != null && list.Contains(obj);
        }

        public static GameObject? GetMainPassenger(DrifterBagController controller)
        {
            return BagPatches.GetMainSeatObject(controller);
        }
        public static bool IsBlacklisted(string objectName)
        {
            return PluginConfig.IsBlacklisted(objectName);
        }

        // Seat swapping is delayed by one frame to allow the previous passenger's state machine to exit cleanly.
        public static bool SetMainPassenger(DrifterBagController controller, GameObject objRef)
        {
            if (controller == null || objRef == null) return false;

            var list = GetBaggedObjects(controller);
            if (!list.Contains(objRef)) return false;

            if (GetMainPassenger(controller) == objRef) return true;

            DelayedAutoPromote.Schedule(controller, objRef, 0f);
            return true;
        }

        public static bool AddBaggedObject(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
            BaggedObjectPatches.SuppressExitForObject(obj);
            controller.AssignPassenger(obj);
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

        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false)
        {
            if (controller == null || obj == null) return;
            BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying);
        }

        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPassengerManager.ForceRecalculateMass(controller);
        }

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

        public static void ScheduleAutoGrab(DrifterBagController controller, GameObject obj, float delay = 0.5f)
        {
            if (controller == null || obj == null) return;
            var coroutineRunner = new GameObject("AutoGrabRunner_" + obj.GetInstanceID());
            var runner = coroutineRunner.AddComponent<AutoGrabCoroutineRunner>();
            runner.StartCoroutine(DelayedAutoGrabCoroutine(controller, obj, delay));
        }

        private static IEnumerator DelayedAutoGrabCoroutine(DrifterBagController controller, GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null && obj.activeInHierarchy)
            {
                AddBaggedObject(controller, obj);
            }
        }

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

        public static float GetMassRatio(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            float totalMass = GetTotalMass(controller);
            float capacity = GetMassCapacity(controller);
            if (capacity == float.MaxValue || capacity <= 0) return 0f;
            return totalMass / capacity;
        }

        public static float GetMassCapacity(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            return Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
        }

        public static EncumbranceLevel GetEncumbranceLevel(DrifterBagController controller)
        {
            float ratio = GetMassRatio(controller);
            if (ratio < 0.5f) return EncumbranceLevel.None;
            if (ratio < 0.75f) return EncumbranceLevel.Light;
            if (ratio < 1.0f) return EncumbranceLevel.Heavy;
            return EncumbranceLevel.Over;
        }

        public static bool IsOverencumbered(DrifterBagController controller)
        {
            return GetMassRatio(controller) > 1.0f;
        }
        public static float GetMoveSpeedPenalty(DrifterBagController controller)
        {
            if (controller == null) return 1.0f;
            return Core.StateCalculator.CalculateMovespeedPenalty(controller, GetTotalMass(controller));
        }

        public static float GetDamageMultiplier(DrifterBagController controller)
        {
            if (controller == null) return 1.0f;
            return Core.SlamDamageCalculator.GetEffectiveCoefficient(controller);
        }

        #endregion

        #region Formula Variable Registry API
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

        public static List<GameObject> GetBaggedCharacterBodies(DrifterBagController controller)
        {
            return GetBaggedObjectsByComponent<CharacterBody>(controller);
        }
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

        public static bool TryGrab(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            if (!HasRoom(controller)) return false;
            return AddBaggedObject(controller, obj);
        }

        public static bool TryReleaseMainPassenger(DrifterBagController controller)
        {
            if (controller == null) return false;
            var mainPassenger = GetMainPassenger(controller);
            if (mainPassenger == null) return false;
            RemoveBaggedObject(controller, mainPassenger, false);
            return true;
        }

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

        // Summaries provide a human-readable snapshot of the bag's state for debugging and diagnostic logs.
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

        // Events allow for a decoupled architecture where external systems can react to bag state changes without tight coupling.
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

        // The plugin system allows the save/load handler to support custom data types.
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
