#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
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
        public static IEnumerable<DrifterBagController> GetAllControllers()
        {
            return BagPatches.GetAllControllers();
        }

        public static List<GameObject> GetBaggedObjects(DrifterBagController controller)
        {
            if (controller == null) return new List<GameObject>();
            var state = BagPatches.GetState(controller);
            lock (state.BagLock)
            {
                return new List<GameObject>(state.BaggedObjects ?? new List<GameObject>());
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
        public static bool HasRoom(DrifterBagController controller, GameObject? incomingObject = null)
        {
            return BagCapacityCalculator.HasRoomForGrab(controller, incomingObject);
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
            var state = BagPatches.GetState(controller);
            lock (state.BagLock)
            {
                return state.BaggedObjects != null && state.BaggedObjects.Contains(obj);
            }
        }

        public static GameObject? GetMainPassenger(DrifterBagController controller)
        {
            return BagPatches.GetMainSeatObject(controller);
        }

        public static ConcurrentDictionary<GameObject, VehicleSeat> GetAdditionalSeats(DrifterBagController controller)
        {
            if (controller == null) return new ConcurrentDictionary<GameObject, VehicleSeat>();
            return BagPatches.GetState(controller).AdditionalSeats;
        }

        public static GameObject? GetIncomingObject(DrifterBagController controller)
        {
            if (controller == null) return null;
            return BagPatches.GetState(controller).IncomingObject;
        }

        public static void SetIncomingObject(DrifterBagController controller, GameObject? obj)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).IncomingObject = obj;
        }

        public static void MarkMassDirty(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).MarkMassDirty();
        }

        public static bool IsMassDirty(DrifterBagController controller)
        {
            if (controller == null) return false;
            return BagPatches.GetState(controller).IsMassDirty;
        }

        public static void ClearMassDirty(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).ClearMassDirty();
        }

        public static void RemoveInstanceId(DrifterBagController controller, int instanceId)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).RemoveInstanceId(instanceId);
        }

        public static void AddInstanceId(DrifterBagController controller, int instanceId)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).AddInstanceId(instanceId);
        }

        public static bool ContainsInstanceId(DrifterBagController controller, int instanceId)
        {
            if (controller == null) return false;
            return BagPatches.GetState(controller).ContainsInstanceId(instanceId);
        }

        public static int GetIntendedSelectedIndex(DrifterBagController controller)
        {
            if (controller == null) return -1;
            return BagPatches.GetState(controller).IntendedSelectedIndex;
        }

        public static void SetIntendedSelectedIndex(DrifterBagController controller, int index)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).IntendedSelectedIndex = index;
        }

        public static bool AreCollidersDisabled(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            var state = BagPatches.GetState(controller);
            return state.DisabledCollidersByObject.TryGetValue(obj, out var d) && d.Count > 0;
        }

        public static void SetCollidersDisabled(DrifterBagController controller, GameObject obj, Dictionary<Collider, bool> disabledStates)
        {
            if (controller == null || obj == null) return;
            var state = BagPatches.GetState(controller);
            state.DisabledCollidersByObject[obj] = disabledStates;
        }

        public static Dictionary<Collider, bool> GetOrCreateDisabledColliders(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return new Dictionary<Collider, bool>();
            var state = BagPatches.GetState(controller);
            if (!state.DisabledCollidersByObject.ContainsKey(obj))
            {
                state.DisabledCollidersByObject[obj] = new Dictionary<Collider, bool>();
            }
            return state.DisabledCollidersByObject[obj];
        }

        public static void RestoreColliders(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return;
            var state = BagPatches.GetState(controller);
            if (state.DisabledCollidersByObject.TryGetValue(obj, out var states))
            {
                BodyColliderCache.RestoreMovementColliders(states);
                state.DisabledCollidersByObject.Remove(obj, out _);
            }
        }
        public static bool IsBlacklisted(string objectName)
        {
            return PluginConfig.IsBlacklisted(objectName);
        }

        public static UncappedBagScaleComponent? GetUncappedBagScale(DrifterBagController controller)
        {
            if (controller == null) return null;
            return BagPatches.GetState(controller).UncappedBagScale;
        }

        public static void SetUncappedBagScale(DrifterBagController controller, UncappedBagScaleComponent? component)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).UncappedBagScale = component;
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
                        Log.DebugIfEnabled("[DrifterBagAPI] Setting BaggedObject state on {0} for {1}", targetBody.name, obj.name);
                        var baggedObjectState = new BaggedObject();
                        baggedObjectState.targetObject = obj;
                        bagStateMachine.SetNextState(baggedObjectState);
                    }
                }
            }

            return true;
        }

        public static void SetAdditionalSeats(DrifterBagController controller, ConcurrentDictionary<GameObject, VehicleSeat> seats)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).AdditionalSeats = seats;
        }

        public static bool RemoveAdditionalSeat(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            return BagPatches.GetState(controller).AdditionalSeats.TryRemove(obj, out _);
        }

        public static void SetMainSeatObject(DrifterBagController controller, GameObject? obj)
        {
            if (controller == null) return;
            BagPatches.SetMainSeatObject(controller, obj);
        }

        public static void SetBaggedObjects(DrifterBagController controller, List<GameObject> objects)
        {
            if (controller == null) return;
            var state = BagPatches.GetState(controller);
            lock (state.BagLock)
            {
                state.BaggedObjects = objects;
            }
        }

        public static void SaveObjectState(DrifterBagController controller, GameObject obj, BaggedObjectStateData state)
        {
            BaggedObjectPatches.SaveObjectState(controller, obj, state);
        }

        public static BaggedObjectStateData? LoadObjectState(DrifterBagController controller, GameObject obj)
        {
            return BaggedObjectPatches.LoadObjectState(controller, obj);
        }

        public static BaggedObjectStateData? FindStateForObject(GameObject obj)
        {
            return BaggedObjectPatches.FindStateForObject(obj);
        }

        public static void UpdateBagScale(BaggedObject instance, float mass)
        {
            BaggedObjectPatches.UpdateBagScale(instance, mass);
        }

        public static void HandlePassengerExit(VehicleSeat seat, GameObject passenger)
        {
            BaggedObjectPatches.HandlePassengerExit(seat, passenger);
        }

        public static void RefreshUIOverlayForMainSeat(DrifterBagController controller, GameObject target)
        {
            BaggedObjectPatches.RefreshUIOverlayForMainSeat(controller, target);
        }

        public static void SynchronizeBaggedObjectState(DrifterBagController controller, GameObject target)
        {
            BaggedObjectPatches.SynchronizeBaggedObjectState(controller, target);
        }

        public static BaggedObject? FindOrCreateBaggedObjectState(DrifterBagController controller, GameObject target)
        {
            return BaggedObjectPatches.FindOrCreateBaggedObjectState(controller, target);
        }

        public static BaggedObject? FindExistingBaggedObjectState(DrifterBagController controller, GameObject target)
        {
            return BaggedObjectPatches.FindExistingBaggedObjectState(controller, target);
        }

        public static void UpdateTargetFields(BaggedObject instance)
        {
            BaggedObjectPatches.UpdateTargetFields(instance);
        }

        public static bool IsPassengerDeadOrDestroyed(GameObject passenger)
        {
            return BaggedObjectPatches.IsPassengerDeadOrDestroyed(passenger);
        }

        public static bool IsObjectExitSuppressed(GameObject passenger)
        {
            return BaggedObjectPatches.IsObjectExitSuppressed(passenger);
        }

        public static void RemoveUIOverlay(GameObject passenger, DrifterBagController controller)
        {
            BaggedObjectPatches.RemoveUIOverlay(passenger, controller);
        }

        public static void RemoveUIOverlayForNullState(DrifterBagController controller)
        {
            BaggedObjectPatches.RemoveUIOverlayForNullState(controller);
        }

        public static void RestorePreservedState(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectPatches.RestorePreservedState(controller, obj);
        }

        public static void ClearAllTemporaryPreservation(DrifterBagController controller)
        {
            BaggedObjectPatches.ClearAllTemporaryPreservation(controller);
        }

        public static void UnsetAllOverrides(EntityStates.Drifter.Bag.BaggedObject? state, GameObject drifterObject)
        {
            BaggedObjectStatePatches.UnsetAllOverrides(state, drifterObject);
        }

        public static void RegisterTrackedESM(EntityStateMachine esm, Patches.BaggedObjectTracker tracker)
        {
            BaggedObjectStatePatches.RegisterTrackedESM(esm, tracker);
        }

        public static void UnregisterTrackedESM(EntityStateMachine esm)
        {
            BaggedObjectStatePatches.UnregisterTrackedESM(esm);
        }

        public static void CleanupObjectState(DrifterBagController controller, GameObject obj, bool preserveForThrow = false)
        {
            BaggedObjectPatches.CleanupObjectState(controller, obj, preserveForThrow);
        }

        public static void PreserveStateForThrow(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectPatches.PreserveStateForThrow(controller, obj);
        }

        public static void ForceCleanupOverrides(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStatePatches.ForceCleanupOverrides(controller, obj);
        }

        public static GameObject? GetMainSeatOccupant(DrifterBagController controller)
        {
            return BaggedObjectPatches.GetMainSeatOccupant(controller);
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

        public static void ScheduleAutoGrab(GameObject obj, string? ownerPlayerId = null, float delay = 0.5f)
        {
            if (obj == null) return;
            var coroutineRunner = new GameObject("AutoGrabRunner_" + obj.GetInstanceID());
            var runner = coroutineRunner.AddComponent<AutoGrabCoroutineRunner>();
            runner.StartCoroutine(DelayedOwnerAutoGrabCoroutine(obj, ownerPlayerId, delay, runner));
        }

        private static IEnumerator DelayedAutoGrabCoroutine(DrifterBagController controller, GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null && obj.activeInHierarchy)
            {
                TryAutoGrab(obj);
            }
        }

        private static IEnumerator DelayedOwnerAutoGrabCoroutine(GameObject obj, string? ownerPlayerId, float delay, AutoGrabCoroutineRunner runner)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null)
            {
                TryAutoGrab(obj, ownerPlayerId);
            }

            if (runner != null && runner.gameObject != null) UnityEngine.Object.Destroy(runner.gameObject);
        }

        public static void TryAutoGrab(GameObject obj, string? ownerPlayerId = null)
        {
            if (!UnityEngine.Networking.NetworkServer.active) return;
            if (obj == null) return;

            // Skip CharacterMaster objects
            if (obj.GetComponent<CharacterMaster>() != null) return;

            // Skip dead objects
            var healthComp = obj.GetComponent<RoR2.HealthComponent>();
            if (healthComp != null && !healthComp.alive) return;

            // Resolve owner body
            CharacterBody? targetBody = null;
            if (!string.IsNullOrEmpty(ownerPlayerId))
            {
                var ownerUser = FindNetworkUserById(ownerPlayerId);
                if (ownerUser != null && ownerUser.master != null)
                {
                    targetBody = ownerUser.master.GetBody();
                }
            }
            else
            {
                // Fallback for single player
                var users = NetworkUser.readOnlyInstancesList;
                if (users.Count == 1)
                {
                    targetBody = users[0].master?.GetBody();
                }
            }

            if (targetBody == null) return;

            var bagController = targetBody.GetComponent<DrifterBagController>();
            if (bagController == null) return;

            if (HasRoom(bagController))
            {
                try
                {
                    bool isCharacterBody = obj.GetComponent<CharacterBody>() != null;
                    if (isCharacterBody)
                    {
                        bool bagIsEmpty = GetBagCount(bagController) == 0;
                        if (bagIsEmpty)
                        {
                            var bagStateMachine = EntityStateMachine.FindByCustomName(targetBody.gameObject, "Bag");
                            if (bagStateMachine != null)
                            {
                                BaggedObjectPatches.SuppressExitForObject(obj);
                                var baggedObject = new BaggedObject();
                                baggedObject.targetObject = obj;
                                bagStateMachine.SetNextState(baggedObject);
                                return;
                            }
                        }

                        // If main seat is full or no state machine, use additional seat
                        Log.DebugIfEnabled($"[DrifterBagAPI] Assigning {obj.name} to additional seat on {targetBody.name}");
                        BaggedObjectPatches.SuppressExitForObject(obj);
                        bagController.AssignPassenger(obj);
                    }
                    else
                    {
                        BaggedObjectPatches.SuppressExitForObject(obj);
                        bagController.AssignPassenger(obj);

                        if (GetMainPassenger(bagController) == obj)
                        {
                            var bagStateMachine = EntityStateMachine.FindByCustomName(targetBody.gameObject, "Bag");
                            if (bagStateMachine != null)
                            {
                                var baggedObject = new BaggedObject();
                                baggedObject.targetObject = obj;
                                bagStateMachine.SetNextState(baggedObject);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[DrifterBagAPI.TryAutoGrab] Error: {ex}");
                }
            }
        }

        public static NetworkUser? FindNetworkUserById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var user in NetworkUser.readOnlyInstancesList)
            {
                if (user == null) continue;
                string userIdStr = user.id.strValue ?? $"{user.id.value}_{user.id.subId}";
                if (userIdStr == id) return user;
            }
            return null;
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


        // ========================================================================================
        // FORMULA VARIABLES
        // ========================================================================================

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


        // ========================================================================================
        // FILTERED QUERIES
        // ========================================================================================

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


        // ========================================================================================
        // EVENTS
        // ========================================================================================

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

        // Returns a list of all currently registered object serializer plugins.
        public static List<ProperSave.Serializers.IObjectSerializerPlugin> GetSerializerPlugins()
        {
            return ProperSave.ProperSaveIntegration.GetSerializerPlugins();
        }

    }
}
