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

    // ========================================================================================
    // DRIFTER BAG API
    // ========================================================================================
    public static class DrifterBagAPI
    {
        private static readonly List<GameObject> _queryBuffer = new List<GameObject>();
        private static readonly List<string> _detailsBuffer = new List<string>();
        private static readonly Dictionary<string, int> _countsBuffer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static List<GameObject> GetBaggedObjects(DrifterBagController controller)
        {
            if (controller == null) return new List<GameObject>();
            return new List<GameObject>(BagPatches.GetState(controller).BaggedObjects ?? new List<GameObject>());
        }

        public static IReadOnlyList<GameObject> GetBaggedObjectsReadOnly(DrifterBagController controller)
        {
            if (controller == null) return Array.Empty<GameObject>();
            var list = BagPatches.GetState(controller).BaggedObjects;
            return list ?? new List<GameObject>(Array.Empty<GameObject>());
        }

        public static void ForEachBaggedObject(DrifterBagController controller, Action<GameObject> action)
        {
            if (controller == null || action == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null) action(obj);
            }
        }

        public static void GetBaggedObjectsInto(DrifterBagController controller, List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null) buffer.Add(obj);
            }
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
            controller.AssignPassenger(obj);
            if (BagPatches.GetMainSeatObject(controller) == obj)
            {
                var targetBody = controller.GetComponentInParent<CharacterBody>();
                if (targetBody != null)
                {
                    var bagStateMachine = EntityStateMachine.FindByCustomName(targetBody.gameObject, "Bag");
                    if (bagStateMachine != null)
                    {
                        Log.Debug($"[DrifterBagAPI] Setting BaggedObject state on {targetBody.name} for {obj.name}");
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

        // ========================================================================================
        // ENCUMBRANCE & STATUS
        // ========================================================================================
        public static float GetMassRatio(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            float totalMass = GetTotalMass(controller);
            float capacity = GetMaxMassCapacity(controller);
            if (capacity == float.MaxValue || capacity <= 0) return 0f;
            return totalMass / capacity;
        }

        public static float GetMassCapacity(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            return Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
        }

        public static float GetMaxMassCapacity(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            return Balance.CapacityScalingSystem.CalculateMaxMassCapacity(controller);
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

        // ========================================================================================
        // FORMULA VARIABLES
        // ========================================================================================
        public static void RegisterFormulaVariable(string name, float value, string? description = null)
        {
            Balance.FormulaRegistry.RegisterVariable(name, value, description);
        }

        public static void RegisterFormulaVariable(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null)
        {
            Balance.FormulaRegistry.RegisterVariable(name, provider, description, fallbackValue);
        }

        public static bool RegisterFormulaVariableSafe(string name, float value, string? description = null, bool overwrite = false)
        {
            return Balance.FormulaRegistry.RegisterVariableSafe(name, value, description, overwrite);
        }

        public static bool RegisterFormulaVariableSafe(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null, bool overwrite = false)
        {
            return Balance.FormulaRegistry.RegisterVariableSafe(name, provider, description, fallbackValue, overwrite);
        }

        public static IEnumerable<string> GetFormulaVariableNames()
        {
            return Balance.FormulaRegistry.GetRegisteredVariableNames();
        }

        public static bool UnregisterFormulaVariable(string name)
        {
            return Balance.FormulaRegistry.UnregisterVariable(name);
        }

        public static bool IsFormulaVariableRegistered(string name)
        {
            return Balance.FormulaRegistry.IsVariableRegistered(name);
        }

        public static VariableInfo? GetFormulaVariableInfo(string name)
        {
            return Balance.FormulaRegistry.GetVariableInfo(name);
        }

        // ========================================================================================
        // FILTERED QUERIES
        // ========================================================================================
        public static List<GameObject> GetBaggedObjectsByComponent<T>(DrifterBagController controller) where T : Component
        {
            var result = new List<GameObject>();
            TryGetBaggedObjectsByComponent<T>(controller, result);
            return result;
        }

        public static void TryGetBaggedObjectsByComponent<T>(DrifterBagController controller, List<GameObject> buffer) where T : Component
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null && obj.GetComponent<T>() != null)
                {
                    buffer.Add(obj);
                }
            }
        }

        public static List<GameObject> GetBaggedCharacterBodies(DrifterBagController controller)
        {
            return GetBaggedObjectsByComponent<CharacterBody>(controller);
        }

        public static void TryGetBaggedCharacterBodies(DrifterBagController controller, List<GameObject> buffer)
        {
            TryGetBaggedObjectsByComponent<CharacterBody>(controller, buffer);
        }

        public static List<GameObject> GetBaggedObjectsByName(DrifterBagController controller, string nameContains)
        {
            var result = new List<GameObject>();
            TryGetBaggedObjectsByName(controller, nameContains, result);
            return result;
        }

        public static void TryGetBaggedObjectsByName(DrifterBagController controller, string nameContains, List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null || nameContains == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null && obj.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    buffer.Add(obj);
                }
            }
        }

        public static List<GameObject> GetBaggedObjectsByExactName(DrifterBagController controller, string exactName)
        {
            var result = new List<GameObject>();
            TryGetBaggedObjectsByExactName(controller, exactName, result);
            return result;
        }

        public static void TryGetBaggedObjectsByExactName(DrifterBagController controller, string exactName, List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null || exactName == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null && string.Equals(obj.name, exactName, StringComparison.OrdinalIgnoreCase))
                {
                    buffer.Add(obj);
                }
            }
        }

        public static List<GameObject> GetBaggedObjectsByMassRange(DrifterBagController controller, float minMass, float maxMass)
        {
            var result = new List<GameObject>();
            TryGetBaggedObjectsByMassRange(controller, minMass, maxMass, result);
            return result;
        }

        public static void TryGetBaggedObjectsByMassRange(DrifterBagController controller, float minMass, float maxMass, List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null)
                {
                    float mass = GetObjectMass(controller, obj);
                    if (mass >= minMass && mass <= maxMass)
                    {
                        buffer.Add(obj);
                    }
                }
            }
        }

        public static GameObject? GetHeaviestObject(DrifterBagController controller)
        {
            GameObject? heaviest = null;
            float maxMass = 0f;

            ForEachBaggedObject(controller, obj =>
            {
                float mass = GetObjectMass(controller, obj);
                if (mass > maxMass)
                {
                    maxMass = mass;
                    heaviest = obj;
                }
            });

            return heaviest;
        }

        public static GameObject? GetLightestObject(DrifterBagController controller)
        {
            GameObject? lightest = null;
            float minMass = float.MaxValue;

            ForEachBaggedObject(controller, obj =>
            {
                float mass = GetObjectMass(controller, obj);
                if (mass < minMass)
                {
                    minMass = mass;
                    lightest = obj;
                }
            });

            return lightest;
        }

        // ========================================================================================
        // OPERATIONS
        // ========================================================================================
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

        // ========================================================================================
        // SUMMARY HELPERS
        // ========================================================================================
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

        public static List<string> GetBaggedObjectDetails(DrifterBagController controller)
        {
            _detailsBuffer.Clear();
            if (controller == null) return new List<string>(_detailsBuffer);
            int index = 1;
            ForEachBaggedObject(controller, obj =>
            {
                string name = GetObjectName(obj);
                float mass = GetObjectMass(controller, obj);
                _detailsBuffer.Add($"{index}. {name} ({mass:F1}kg)");
                index++;
            });
            return new List<string>(_detailsBuffer);
        }

        public static void TryGetBaggedObjectDetails(DrifterBagController controller, List<string> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            int index = 1;
            ForEachBaggedObject(controller, obj =>
            {
                string name = GetObjectName(obj);
                float mass = GetObjectMass(controller, obj);
                buffer.Add($"{index}. {name} ({mass:F1}kg)");
                index++;
            });
        }

        public static Dictionary<string, int> GetBaggedObjectCounts(DrifterBagController controller)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            GetBaggedObjectCountsInto(controller, counts);
            return counts;
        }

        public static void GetBaggedObjectCountsInto(DrifterBagController controller, Dictionary<string, int> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            ForEachBaggedObject(controller, obj =>
            {
                string name = GetObjectName(obj);
                if (!buffer.ContainsKey(name))
                {
                    buffer[name] = 0;
                }
                buffer[name]++;
            });
        }

        // ========================================================================================
        // EVENTS
        // ========================================================================================
        public static event Action<DrifterBagController, GameObject, int>? OnObjectGrabbed;

        public static event Action<DrifterBagController, GameObject, bool>? OnObjectReleased;

        public static event Action<DrifterBagController>? OnBagFull;

        public static event Action<DrifterBagController, float>? OnOverencumbered;

        public static event Action<DrifterBagController, bool>? OnBagCleared;

        public static event Action<DrifterBagController, GameObject?, GameObject?>? OnMainPassengerChanged;

        public static event Action<DrifterBagController, float, float>? OnMassRecalculated;

        // ========================================================================================
        // EVENT INVOKERS
        // ========================================================================================
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

        // ========================================================================================
        // SERIALIZATION API
        // ========================================================================================
        public static void RegisterSerializerPlugin(ProperSave.Serializers.IObjectSerializerPlugin plugin)
        {
            ProperSave.ProperSaveIntegration.RegisterPlugin(plugin);
        }

        public static List<ProperSave.Serializers.IObjectSerializerPlugin> GetSerializerPlugins()
        {
            return ProperSave.ProperSaveIntegration.GetSerializerPlugins();
        }
    }
}
