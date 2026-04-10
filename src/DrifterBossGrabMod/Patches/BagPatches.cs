#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using System.Reflection;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.UI;
using EntityStates;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Patches
{
    public class BaggedObjectTracker : MonoBehaviour
    {
        public DrifterBagController? controller;
        public GameObject? obj;
        public bool isRemovingManual = false;
        private int _cachedInstanceId;

        private void Start()
        {
            if (obj != null) _cachedInstanceId = obj.GetInstanceID();
        }

        private void OnDestroy()
        {
            if (isRemovingManual) return;

            if (!ReferenceEquals(obj, null))
            {
                PersistenceObjectsTracker.UntrackBaggedObject(obj, true);
            }

            if (!ReferenceEquals(controller, null) && !ReferenceEquals(obj, null))
            {

                if (controller != null && obj != null)
                {
                    BagPassengerManager.RemoveBaggedObject(controller, obj, true);
                }
            }
        }
    }

    // Helper class for delayed auto-promotion (avoids race conditions).
    public class DelayedAutoPromote : MonoBehaviour
    {
        private DrifterBagController? _controller;
        private GameObject? _newMain;
        private float _delayTime = 0f;
        private float _elapsedTime = 0f;
        private bool _skipStateReset = false;

        public static void Schedule(DrifterBagController controller, GameObject? newMain, float delay = 0.0f, bool skipStateReset = false)
        {
            if (delay <= 0f)
            {
                // Execute immediately without creating a GameObject
                ExecutePromotionImmediate(controller, newMain, skipStateReset);
                return;
            }

            var go = new GameObject($"DelayedAutoPromote_{newMain?.name ?? "null"}");
            var delayed = go.AddComponent<DelayedAutoPromote>();
            delayed._controller = controller;
            delayed._newMain = newMain;
            delayed._delayTime = delay;
            delayed._skipStateReset = skipStateReset;
        }

        private static void ExecutePromotionImmediate(DrifterBagController controller, GameObject? newMain, bool skipStateReset)
        {
            if (controller == null || newMain == null || ProjectileRecoveryPatches.IsInProjectileState(newMain))
                return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[ExecutePromotionImmediate] START: Promoting {newMain.name}");

            var state = BagPatches.GetState(controller);
            lock (state.BagLock)
            {
                if (!state.BaggedObjects.Contains(newMain))
                    return;
            }

            // Check current state machine state
            var bagStateMachine = GetBagStateMachine(controller);
            var currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
            var currentTarget = GetBaggedObjectTarget(bagStateMachine);
            var isInAdditionalSeat = state.AdditionalSeats.TryGetValue(newMain, out var _);
            var isInMainSeat = controller.vehicleSeat?.NetworkpassengerBodyObject == newMain;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ExecutePromotionImmediate] BEFORE CLEAR: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
            }

            // Clear any existing BaggedObject state before promoting
            // This prevents race condition where destroyed object's state exits while we're trying to enter new state
            if (NetworkServer.active)
            {
                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag")
                    {
                        if (esm.state is BaggedObject)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[ExecutePromotionImmediate] Clearing BaggedObject state (current target: {(GetBaggedObjectTarget(esm) ? GetBaggedObjectTarget(esm).name : "null")})");
                            // Transition to Main state to clear the old BaggedObject state
                            esm.SetNextStateToMain();
                        }
                        else
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[ExecutePromotionImmediate] Current state is NOT BaggedObject, skipping state clear");
                        }
                        break;
                    }
                }
            }

            // Eject from additional seat if needed
            if (state.AdditionalSeats.TryGetValue(newMain, out var existingSeat) && existingSeat != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ExecutePromotionImmediate] Ejecting from additional seat");

                if (NetworkServer.active)
                    existingSeat.EjectPassenger(newMain);
                state.AdditionalSeats.TryRemove(newMain, out _);
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                bagStateMachine = GetBagStateMachine(controller);
                currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                currentTarget = GetBaggedObjectTarget(bagStateMachine);
                isInAdditionalSeat = state.AdditionalSeats.TryGetValue(newMain, out var _);
                isInMainSeat = controller.vehicleSeat?.NetworkpassengerBodyObject == newMain;

                Log.Info($"[ExecutePromotionImmediate] AFTER CLEAR: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
            }

            if (NetworkServer.active)
            {
                // Force-clear dead/destroyed passengers from main seat before autopromote
                if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
                {
                    var currentPassenger = controller.vehicleSeat.NetworkpassengerBodyObject;
                    bool isDeadOrDestroyed = currentPassenger == null ||
                        (currentPassenger.GetComponent<HealthComponent>()?.alive == false) ||
                        (currentPassenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0);
                    if (isDeadOrDestroyed)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[DelayedAutoPromote] Force-clearing dead passenger {currentPassenger?.name ?? "null"} from main seat before autopromote");
                        controller.vehicleSeat.EjectPassenger();
                    }
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ExecutePromotionImmediate] Calling controller.AssignPassenger({newMain.name})");

                controller.AssignPassenger(newMain);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    bagStateMachine = GetBagStateMachine(controller);
                    currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                    currentTarget = GetBaggedObjectTarget(bagStateMachine);
                    isInAdditionalSeat = state.AdditionalSeats.TryGetValue(newMain, out var _);
                    isInMainSeat = controller.vehicleSeat?.NetworkpassengerBodyObject == newMain;

                    Log.Info($"[ExecutePromotionImmediate] AFTER ASSIGN: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
                }

                if (controller.vehicleSeat != null)
                {
                    if (controller.vehicleSeat.NetworkpassengerBodyObject != newMain)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[ExecutePromotionImmediate] Calling vehicleSeat.AssignPassenger({newMain.name}) - NOT in main seat yet");

                        controller.vehicleSeat.AssignPassenger(newMain);

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            bagStateMachine = GetBagStateMachine(controller);
                            currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                            currentTarget = GetBaggedObjectTarget(bagStateMachine);
                            isInAdditionalSeat = state.AdditionalSeats.TryGetValue(newMain, out var _);
                            isInMainSeat = controller.vehicleSeat?.NetworkpassengerBodyObject == newMain;

                            Log.Info($"[ExecutePromotionImmediate] AFTER VEHICLE_SEAT: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[ExecutePromotionImmediate] SKIPPING vehicleSeat.AssignPassenger - Already in main seat");
                    }

                    // Force transition to BaggedObject state (reset was skipped).
                    var stateMachines = controller.GetComponents<EntityStateMachine>();
                    foreach (var esm in stateMachines)
                    {
                        if (esm.customName == "Bag")
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[ExecutePromotionImmediate] Setting new BaggedObject state with target={newMain.name}");

                            var newState = new BaggedObject();
                            newState.targetObject = newMain;
                            esm.SetNextState(newState);

                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                bagStateMachine = GetBagStateMachine(controller);
                                currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                                currentTarget = GetBaggedObjectTarget(bagStateMachine);

                                Log.Info($"[ExecutePromotionImmediate] FINAL STATE: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}");
                            }
                            break;
                        }
                    }
                }
            }
            else if (controller.hasAuthority)
            {
                GameObject? previousMain = BagPatches.GetMainSeatObject(controller);
                BagPatches.SetMainSeatObject(controller, newMain);
                API.DrifterBagAPI.InvokeOnMainPassengerChanged(controller, previousMain, newMain);
            }
        }

        // Helper methods for debug logging
        private static EntityStateMachine GetBagStateMachine(DrifterBagController controller)
        {
            var stateMachines = controller.GetComponents<EntityStateMachine>();
            foreach (var esm in stateMachines)
            {
                if (esm.customName == "Bag")
                    return esm;
            }
            return null;
        }

        private static GameObject? GetBaggedObjectTarget(EntityStateMachine? esm)
        {
            if (esm?.state is BaggedObject bagged)
                return bagged.targetObject;
            return null;
        }

        private void Update()
        {
            _elapsedTime += Time.deltaTime;

            if (_elapsedTime >= _delayTime)
            {
                ExecutePromotion();
            }
        }

        private void ExecutePromotion()
        {
            if (_controller != null && _newMain != null && !ProjectileRecoveryPatches.IsInProjectileState(_newMain))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ExecutePromotion] START: Promoting {_newMain.name}");

                var state = BagPatches.GetState(_controller);
                bool stillInBag;
                lock (state.BagLock)
                {
                    stillInBag = state.BaggedObjects.Contains(_newMain);
                }
                if (!stillInBag)
                {
                    Destroy(gameObject);
                    return;
                }

                // Check current state machine state
                var bagStateMachine = GetBagStateMachine(_controller);
                var currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                var currentTarget = GetBaggedObjectTarget(bagStateMachine);
                var isInAdditionalSeat = state.AdditionalSeats.TryGetValue(_newMain, out var _);
                var isInMainSeat = _controller.vehicleSeat?.NetworkpassengerBodyObject == _newMain;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ExecutePromotion] BEFORE CLEAR: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
                }

                // Clear any existing BaggedObject state before promoting
                if (NetworkServer.active)
                {
                    var stateMachines = _controller.GetComponents<EntityStateMachine>();
                    foreach (var esm in stateMachines)
                    {
                        if (esm.customName == "Bag")
                        {
                            if (esm.state is BaggedObject)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[ExecutePromotion] Clearing BaggedObject state (current target: {(GetBaggedObjectTarget(esm) ? GetBaggedObjectTarget(esm).name : "null")})");
                                // Transition to Main state to clear the old BaggedObject state
                                esm.SetNextStateToMain();
                            }
                            else
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[ExecutePromotion] Current state is NOT BaggedObject, skipping state clear");
                            }
                            break;
                        }
                    }
                }

                // Eject from additional seat if needed
                if (state.AdditionalSeats.TryGetValue(_newMain, out var existingSeat) && existingSeat != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[ExecutePromotion] Ejecting from additional seat");

                    if (NetworkServer.active)
                        existingSeat.EjectPassenger(_newMain);
                    state.AdditionalSeats.TryRemove(_newMain, out _);
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    bagStateMachine = GetBagStateMachine(_controller);
                    currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                    currentTarget = GetBaggedObjectTarget(bagStateMachine);
                    isInAdditionalSeat = state.AdditionalSeats.TryGetValue(_newMain, out var _);
                    isInMainSeat = _controller.vehicleSeat?.NetworkpassengerBodyObject == _newMain;

                    Log.Info($"[ExecutePromotion] AFTER CLEAR: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
                }

                if (NetworkServer.active)
                {
                    // Force-clear dead/destroyed passengers from main seat before autopromote
                    if (_controller.vehicleSeat != null && _controller.vehicleSeat.hasPassenger)
                    {
                        var currentPassenger = _controller.vehicleSeat.NetworkpassengerBodyObject;
                        bool isDeadOrDestroyed = currentPassenger == null ||
                            (currentPassenger.GetComponent<HealthComponent>()?.alive == false) ||
                            (currentPassenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0);
                        if (isDeadOrDestroyed)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[DelayedAutoPromote] Force-clearing dead passenger {currentPassenger?.name ?? "null"} from main seat before autopromote");
                            _controller.vehicleSeat.EjectPassenger();
                        }
                    }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[ExecutePromotion] Calling controller.AssignPassenger({_newMain.name})");

                    _controller.AssignPassenger(_newMain);

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        bagStateMachine = GetBagStateMachine(_controller);
                        currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                        currentTarget = GetBaggedObjectTarget(bagStateMachine);
                        isInAdditionalSeat = state.AdditionalSeats.TryGetValue(_newMain, out var _);
                        isInMainSeat = _controller.vehicleSeat?.NetworkpassengerBodyObject == _newMain;

                        Log.Info($"[ExecutePromotion] AFTER ASSIGN: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
                    }

                    if (_controller.vehicleSeat != null)
                    {
                        if (_controller.vehicleSeat.NetworkpassengerBodyObject != _newMain)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[ExecutePromotion] Calling vehicleSeat.AssignPassenger({_newMain.name}) - NOT in main seat yet");

                            _controller.vehicleSeat.AssignPassenger(_newMain);

                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                bagStateMachine = GetBagStateMachine(_controller);
                                currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                                currentTarget = GetBaggedObjectTarget(bagStateMachine);
                                isInAdditionalSeat = state.AdditionalSeats.TryGetValue(_newMain, out var _);
                                isInMainSeat = _controller.vehicleSeat?.NetworkpassengerBodyObject == _newMain;

                                Log.Info($"[ExecutePromotion] AFTER VEHICLE_SEAT: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}, InAdditional={isInAdditionalSeat}, InMain={isInMainSeat}");
                            }
                        }
                        else
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[ExecutePromotion] SKIPPING vehicleSeat.AssignPassenger - Already in main seat");
                        }

                        // Force transition to BaggedObject state (reset was skipped).
                        var stateMachines = _controller.GetComponents<EntityStateMachine>();
                        foreach (var esm in stateMachines)
                        {
                            if (esm.customName == "Bag")
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[ExecutePromotion] Setting new BaggedObject state with target={_newMain.name}");

                                var newState = new BaggedObject();
                                newState.targetObject = _newMain;
                                esm.SetNextState(newState);

                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    bagStateMachine = GetBagStateMachine(_controller);
                                    currentStateName = bagStateMachine?.state?.GetType().Name ?? "null";
                                    currentTarget = GetBaggedObjectTarget(bagStateMachine);

                                    Log.Info($"[ExecutePromotion] FINAL STATE: State={currentStateName}, Target={(currentTarget ? currentTarget.name : "null")}");
                                }
                                break;
                            }
                        }
                    }
                }
                else if (_controller.hasAuthority)
                {
                    GameObject? previousMain = BagPatches.GetMainSeatObject(_controller);
                    BagPatches.SetMainSeatObject(_controller, _newMain);
                    API.DrifterBagAPI.InvokeOnMainPassengerChanged(_controller, previousMain, _newMain);
                }
            }

            Destroy(gameObject);
        }
    }

    public static class BagPatches
    {
        // Consolidated state dictionary
        private static readonly ConcurrentDictionary<DrifterBagController, Core.BagState> _states = new ConcurrentDictionary<DrifterBagController, Core.BagState>();

        public static Core.BagState GetState(DrifterBagController controller)
        {
            if (ReferenceEquals(controller, null)) return null!;
            return _states.GetOrAdd(controller, _ => new Core.BagState());
        }

        public static ICollection<DrifterBagController> GetAllControllers() => _states.Keys;

        public static void ClearCaches()
        {
            _states.Clear();

        }

        [HarmonyPatch(typeof(Run), "Start")]
        public class Run_Start_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                ClearCaches();
            }
        }

        private static bool HasActiveTeleporterInScene(GameObject excludeTeleporter)
        {
            var allTeleporters = UnityEngine.Object.FindObjectsByType<RoR2.TeleporterInteraction>(FindObjectsSortMode.None);
            foreach (var teleporter in allTeleporters)
            {
                if (teleporter.gameObject != excludeTeleporter && teleporter.enabled && !PersistenceManager.ShouldDisableTeleporter(teleporter.gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            private static bool _usingAdditionalSeat = false;
            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value && passengerObject != null)
                {
                    Log.Info($"[AssignPassenger] DEBUG: Dumping components for {passengerObject.name}");
                    Log.Info($"[AssignPassenger] DEBUG: Position: {passengerObject.transform.position}");
                    Log.Info($"[AssignPassenger] DEBUG: Rotation: {passengerObject.transform.rotation}");
                    Log.Info($"[AssignPassenger] DEBUG: Parent: {passengerObject.transform.parent?.name ?? "null"}");

                    var debugModelLocator = passengerObject.GetComponent<ModelLocator>();
                    if (debugModelLocator != null)
                    {
                        Log.Info($"[AssignPassenger] DEBUG: ModelLocator.modelTransform: {debugModelLocator.modelTransform?.name ?? "null"}");
                        Log.Info($"[AssignPassenger] DEBUG: ModelLocator.dontDetatchFromParent: {debugModelLocator.dontDetatchFromParent}");
                        Log.Info($"[AssignPassenger] DEBUG: ModelLocator.autoUpdateModelTransform: {debugModelLocator.autoUpdateModelTransform}");
                        if (debugModelLocator.modelTransform != null)
                        {
                            Log.Info($"[AssignPassenger] DEBUG: Model position: {debugModelLocator.modelTransform.position}");
                            Log.Info($"[AssignPassenger] DEBUG: Model parent: {debugModelLocator.modelTransform.parent?.name ?? "null"}");
                        }
                    }

                    var debugRigidbody = passengerObject.GetComponent<Rigidbody>();
                    if (debugRigidbody != null)
                    {
                        Log.Info($"[AssignPassenger] DEBUG: Rigidbody.isKinematic: {debugRigidbody.isKinematic}");
                        Log.Info($"[AssignPassenger] DEBUG: Rigidbody.detectCollisions: {debugRigidbody.detectCollisions}");
                    }
                }

                _usingAdditionalSeat = false;
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject!.name))
                {
                    return false;
                }
                if (passengerObject == null) return true;

                // If the object was previously thrown and grabbed mid-air, it is no longer a projectile
                    if (ProjectileRecoveryPatches.IsInProjectileState(passengerObject))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AssignPassenger] Object {passengerObject.name} grabbed mid-air. Removing from projectile tracking.");
                    }
                    ProjectileRecoveryPatches.RemoveFromProjectileState(passengerObject);
                }

                CharacterBody? body = null;
                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null)
                {
                    bool isCycling = DrifterBossGrabPlugin.IsSwappingPassengers;
                    var existingPreserver = passengerObject.GetComponent<ModelStatePreserver>();

                    if (!isCycling && PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator.modelTransform != null && existingPreserver == null)
                    {

                        passengerObject.AddComponent<ModelStatePreserver>();
                    }


                }
                body = passengerObject.GetComponent<CharacterBody>();
                if (body)
                {
                    if (body.baseMaxHealth <= 0 || body.levelMaxHealth < 0 ||
                        body.teamComponent == null || body.teamComponent.teamIndex < 0)
                    {
                        return false;
                    }
                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                    }
                }
                if (body != null && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable))
                {
                    // Get or create the state dictionary for this object
                    var bagState = GetState(__instance);
                    if (!bagState.DisabledCollidersByObject.ContainsKey(passengerObject))
                    {
                        bagState.DisabledCollidersByObject[passengerObject] = new Dictionary<Collider, bool>();
                    }
                    var objectDisabledStates = bagState.DisabledCollidersByObject[passengerObject];
                    
                    // Disable colliders and store states persistently
                    BodyColliderCache.DisableMovementColliders(passengerObject, objectDisabledStates);
                    
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AssignPassenger] Disabled {objectDisabledStates.Count} colliders for ungrabbable enemy {passengerObject.name}");
                    }
                }
                var teleporterInteraction = passengerObject.GetComponent<RoR2.TeleporterInteraction>();
                if (teleporterInteraction != null)
                {
                    bool hasActiveTeleporter = HasActiveTeleporterInScene(passengerObject);
                    if (hasActiveTeleporter)
                    {
                        teleporterInteraction.enabled = false;
                        PersistenceManager.MarkTeleporterForDisabling(passengerObject);
                    }
                }
                // Remove from persistence before bagging to fix hierarchy issues
                PersistenceManager.RemovePersistedObject(passengerObject);
                PersistenceObjectsTracker.TrackBaggedObject(passengerObject);

                // Track incoming object for predictive capacity calculation in carousel
                if (__instance != null)
                {
                    GetState(__instance).IncomingObject = passengerObject;
                }

                int effectiveCapacity = __instance != null ? BagCapacityCalculator.GetUtilityMaxStock(__instance!, passengerObject) : 1;
                var list = (__instance != null) ? GetState(__instance!).BaggedObjects : null;
                if (list == null) return true;
                int objectsInBag = BagCapacityCalculator.GetCurrentBaggedCount(__instance!);
                int passengerInstanceId = passengerObject.GetInstanceID();
                bool isAlreadyTrackedByThisController = GetState(__instance!).ContainsInstanceId(passengerInstanceId);

                // DEBUG: Log capacity check details
                int listCount = list.Count;
                int nullCount = 0;
                int projectileCount = 0;
                foreach (var o in list)
                {
                    if (o == null) nullCount++;
                    else if (ProjectileRecoveryPatches.IsInProjectileState(o)) projectileCount++;
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value || objectsInBag < listCount)
                    Log.Info($"[AssignPassenger] CAPACITY CHECK: {passengerObject.name} listCount={listCount}, nullCount={nullCount}, projectileCount={projectileCount}, objectsInBag={objectsInBag}, effectiveCapacity={effectiveCapacity}, isAlreadyTracked={isAlreadyTrackedByThisController}");

                // When AddedCapacity is INF AND EnableBalance is true, check mass capacity instead of slot capacity
                if (PluginConfig.Instance.BottomlessBagEnabled.Value && PluginConfig.Instance.IsAddedCapacityInfinite && !isAlreadyTrackedByThisController)
                {
                    // Calculate total mass including the incoming passenger
                    float totalMass = BagCapacityCalculator.CalculateTotalBagMass(__instance!, passengerObject);

                    // Calculate mass capacity with overencumbrance limit
                    float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(__instance!);
                    float overencumbranceMultiplier = Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor);
                    float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                    // Check if we would exceed mass capacity
                    if (totalMass > maxMassCapacity)
                    {

                        return false;
                    }
                }

                if (!isAlreadyTrackedByThisController && objectsInBag >= effectiveCapacity)
                {
                    Log.Warning($"[AssignPassenger] BLOCKING {passengerObject.name} - bag full ({objectsInBag}/{effectiveCapacity}), alreadyTracked={isAlreadyTrackedByThisController}");
                    return false;
                }

                if (effectiveCapacity == 1 && isAlreadyTrackedByThisController)
                {
                    bool isAlreadyInMainSeat = __instance!.vehicleSeat != null &&
                        __instance.vehicleSeat.hasPassenger &&
                        ReferenceEquals(__instance.vehicleSeat.NetworkpassengerBodyObject, passengerObject);

                    if (isAlreadyInMainSeat)
                    {

                        return false;
                    }
                }

                // When PrioritizeMainSeat is enabled, skip additional seat assignment
                // and let the object go straight to the main seat
                bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;
                if (NetworkServer.active && __instance != null)
                {
                    var netController = __instance.GetComponent<Networking.BottomlessBagNetworkController>();
                    if (netController != null && !__instance.hasAuthority)
                    {
                        prioritize = netController.prioritizeMainSeat;
                    }
                }

                if (!prioritize &&
                    TryAssignToAdditionalSeat(__instance!, passengerObject, effectiveCapacity, isAlreadyTrackedByThisController))
                {
                    return false;
                }

                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (passengerObject != null)
                {
                if (ProjectileRecoveryPatches.IsInProjectileState(passengerObject))
                    {
                        return;
                    }

                    BagHelpers.AddTracker(__instance, passengerObject);

                    if (!_usingAdditionalSeat && __instance.vehicleSeat != null && NetworkServer.active)
                    {
                        if (__instance.vehicleSeat.NetworkpassengerBodyObject != passengerObject)
                        {
                            __instance.vehicleSeat.AssignPassenger(passengerObject);
                        }
                    }
                    if (!_usingAdditionalSeat)
                    {
                        // Get previous main passenger before changing
                        GameObject? previousMain = GetMainSeatObject(__instance);
                        SetMainSeatObject(__instance, passengerObject);

                        // Fire OnMainPassengerChanged event
                        API.DrifterBagAPI.InvokeOnMainPassengerChanged(__instance, previousMain, passengerObject);

                        var seatDict = GetState(__instance).AdditionalSeats;
                        if (seatDict != null)
                        {
                            seatDict.TryRemove(passengerObject, out _);
                        }
                    }

                    var list = GetState(__instance).BaggedObjects;
                    var state = GetState(__instance);
                    bool alreadyTracked = state.ContainsInstanceId(passengerObject.GetInstanceID());
                    if (!alreadyTracked)
                    {
                        list.Add(passengerObject);
                        state.AddInstanceId(passengerObject.GetInstanceID());

                        // Fire OnObjectGrabbed event
                        int slotIndex = _usingAdditionalSeat ? -1 : list.Count - 1;
                        API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, slotIndex);
                    }

                    // Always ensure state exists for the info panel UI (BaggedObjectInfoUIController)
                    // Uses ensure-if-null to avoid overwriting preserved breakout timer state
                    if (BaggedObjectPatches.LoadObjectState(__instance, passengerObject) == null)
                    {
                        var newState = new BaggedObjectStateData();
                        newState.CalculateFromObject(passengerObject, __instance);
                        BaggedObjectPatches.SaveObjectState(__instance, passengerObject, newState);
                    }
                    if (UnityEngine.Networking.NetworkServer.active)
                    {
                        PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                    }
                    BagCarouselUpdater.UpdateCarousel(__instance);

                    // Clear incoming object tracking.
                    GetState(__instance).IncomingObject = null;

                    if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                    {
                        BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                    }
                    BagPassengerManager.ForceRecalculateMass(__instance);

                    // Invalidate damage preview cache.
                    DamagePreviewOverlay.InvalidateAllCaches();
                }
            }

        private static bool TryAssignToAdditionalSeat(DrifterBagController __instance, GameObject passengerObject, int effectiveCapacity, bool isAlreadyTrackedByThisController)
        {
             if (isAlreadyTrackedByThisController || effectiveCapacity <= 1) return false;

              var seatDict = GetState(__instance).AdditionalSeats;

              var newSeat = AdditionalSeatManager.FindOrCreateEmptySeat(__instance, ref seatDict);

             var list = GetState(__instance).BaggedObjects;

            int passengerInstanceId = passengerObject.GetInstanceID();

             if (newSeat != null)
             {
                 _usingAdditionalSeat = true;
                 BagHelpers.AddTracker(__instance, passengerObject);

                 if (GetMainSeatObject(__instance) == passengerObject)
                 {
                     SetMainSeatObject(__instance, null);
                 }

                 // Attach breakout timer if network active and object can break out
                 if (NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(passengerObject) && !passengerObject.GetComponent<AdditionalSeatBreakoutTimer>())
                 {
                     var timer = passengerObject.AddComponent<AdditionalSeatBreakoutTimer>();
                     timer.controller = __instance;
                     
                     // Calculate breakout time logic analogous to vanilla BaggedObject.OnEnter
                     float mass = __instance.CalculateBaggedObjectMass(passengerObject);
                     float baseBreakoutTime = 10f; // Vanilla base
                     
                     // DrifterBossGrab config multiplier is applied here 
                     float breakoutMultiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                     
                     float finalTime = Mathf.Max(baseBreakoutTime - 0.005f * mass, 1f);
                     
                     var hc = passengerObject.GetComponent<CharacterBody>();
                     if (hc && hc.isElite)
                     {
                         finalTime *= 0.8f; // Vanilla elite coefficient
                     }
                     
                     timer.breakoutTime = finalTime * breakoutMultiplier;

                     // Restore previous timer state if available
                     var storedState = BaggedObjectPatches.LoadObjectState(__instance, passengerObject);
                     if (storedState != null)
                     {
                         if (storedState.breakoutTime > 0f)
                         {
                             timer.breakoutTime = storedState.breakoutTime;
                         }
                         timer.SetElapsedBreakoutTime(storedState.elapsedBreakoutTime);
                         timer.breakoutAttempts = storedState.breakoutAttempts;
                         
                         if (PluginConfig.Instance.EnableDebugLogs.Value)
                             Log.Info($"[DEBUG] [TryAssignToAdditionalSeat] Restored timer for {passengerObject.name}: age={storedState.elapsedBreakoutTime}, attempts={storedState.breakoutAttempts}, breakoutTime={timer.breakoutTime}");
                     }
                 }

                   seatDict[passengerObject] = newSeat;

                   if (NetworkServer.active)
                   {
                       newSeat.AssignPassenger(passengerObject);
                   }

                    bool alreadyExists = GetState(__instance).ContainsInstanceId(passengerInstanceId);
                    if (!alreadyExists)
                    {
                        list.Add(passengerObject);
                        GetState(__instance).AddInstanceId(passengerInstanceId);

                        // Fire OnObjectGrabbed event
                        int slotIndex = list.Count - 1;
                        API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, slotIndex);
                    }

                   // Ensure state exists for the info panel UI
                   if (BaggedObjectPatches.LoadObjectState(__instance, passengerObject) == null)
                  {
                      var infoState = new BaggedObjectStateData();
                      infoState.CalculateFromObject(passengerObject, __instance);
                      BaggedObjectPatches.SaveObjectState(__instance, passengerObject, infoState);
                  }

                  if (NetworkServer.active)
                  {
                      PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                  }

                    // Update carousel (final state after mass cap).
                   BagCarouselUpdater.UpdateCarousel(__instance);

                  // Clear incoming object tracking after carousel is updated
                  GetState(__instance).IncomingObject = null;

                  if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                  {
                      BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                  }
                  BagPassengerManager.ForceRecalculateMass(__instance);
                  return true;
              }
               else if (!UnityEngine.Networking.NetworkServer.active)
               {
                    _usingAdditionalSeat = true;
                    BagHelpers.AddTracker(__instance, passengerObject);

                    // Trust server to auto-grab and create seat

                    bool alreadyExists = GetState(__instance).ContainsInstanceId(passengerInstanceId);
                    if (!alreadyExists)
                    {
                        list.Add(passengerObject);
                        GetState(__instance).AddInstanceId(passengerInstanceId);

                       // Fire OnObjectGrabbed event
                       int slotIndex = list.Count - 1;
                       API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, slotIndex);
                   }

                  // Ensure state exists for the info panel UI
                  if (BaggedObjectPatches.LoadObjectState(__instance, passengerObject) == null)
                 {
                     var infoState = new BaggedObjectStateData();
                     infoState.CalculateFromObject(passengerObject, __instance);
                     BaggedObjectPatches.SaveObjectState(__instance, passengerObject, infoState);
                 }

                 BagCarouselUpdater.UpdateCarousel(__instance);

                 // Clear incoming object tracking after carousel is updated
                 GetState(__instance).IncomingObject = null;

                 if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                 {
                     BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                 }

                 BagPassengerManager.ForceRecalculateMass(__instance);

                 return true;
             }
             return false;
        }
    }

        public static void SetMainSeatObject(DrifterBagController controller, GameObject? obj)
        {
            if (controller == null) return;
            GetState(controller).MainSeatObject = obj;
        }

        public static GameObject? GetMainSeatObject(DrifterBagController controller)
        {
             if (controller == null) return null;
             var obj = GetState(controller).MainSeatObject;
             if (obj == null || (obj is UnityEngine.Object uo && !uo))
             {
                if (controller != null)
                {
                    GetState(controller).MainSeatObject = null;
                }
                 return null;
             }
             return obj;
        }
    }
    [HarmonyPatch(typeof(RoR2.VehicleSeat), nameof(RoR2.VehicleSeat.AssignPassenger))]
    public static class VehicleSeat_AssignPassenger_Postfix
    {
        [HarmonyPrefix]
        public static bool Prefix(RoR2.VehicleSeat __instance, GameObject bodyObject)
        {
            // Block assignment during client-side BaggedObject initialization.
            if (BaggedObjectStatePatches.BaggedObject_OnEnter.InitializingPassenger != null &&
                bodyObject == BaggedObjectStatePatches.BaggedObject_OnEnter.InitializingPassenger)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[VehicleSeat.AssignPassenger] BLOCKED assignment for {bodyObject.name} during BaggedObject initialization (Client Side override)");

                // Manually replicate vanilla collider/hurtbox deactivation since we're blocking AssignPassenger
                if (__instance.disableAllCollidersAndHurtboxes)
                {
                    // Disable all colliders (replicates SetPassengerCollisions(false))
                    var allColliders = bodyObject.GetComponentsInChildren<Collider>();
                    foreach (var collider in allColliders)
                    {
                        if (collider != null)
                            collider.enabled = false;
                    }

                    // Increment hurtbox deactivator counter (replicates vanilla logic)
                    var characterBody = bodyObject.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.modelLocator != null)
                    {
                        var modelTransform = characterBody.modelLocator.modelTransform;
                        if (modelTransform != null)
                        {
                            var hurtBoxGroup = modelTransform.GetComponent<RoR2.HurtBoxGroup>();
                            if (hurtBoxGroup != null)
                            {
                                hurtBoxGroup.hurtBoxesDeactivatorCounter++;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[VehicleSeat.AssignPassenger] Manually disabled colliders and incremented hurtBox counter for {bodyObject.name}");
                            }
                        }
                    }
                }

                return false; // Skip original AssignPassenger
            }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(RoR2.VehicleSeat __instance, GameObject bodyObject)
        {
            if (bodyObject == null || !NetworkServer.active) return;
            var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
            if (drifterBagController == null) return;
            if (__instance == drifterBagController.vehicleSeat)
            {
                return;
            }

             var seatDict = BagPatches.GetState(drifterBagController).AdditionalSeats;
             // Remove stale entries pointing to this seat, then assign the new mapping
             foreach (var kvp in seatDict)
             {
                 if (kvp.Value == __instance && kvp.Key != bodyObject)
                 {
                     seatDict.TryRemove(kvp.Key, out _);
                 }
             }
             seatDict[bodyObject] = __instance;
        }
    }

    [HarmonyPatch(typeof(GlobalEventManager), nameof(GlobalEventManager.OnCharacterDeath))]
    public static class GlobalEventManager_OnCharacterDeath
    {
        [HarmonyPostfix]
        public static void Postfix(DamageReport damageReport)
        {
            if (damageReport == null || damageReport.victimBody == null) return;
            GameObject victim = damageReport.victimBody.gameObject;

            foreach (var controller in BagPatches.GetAllControllers())
            {
                var list = BagPatches.GetState(controller).BaggedObjects;

                if (list != null && victim != null && list.Contains(victim))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value && controller != null)
                    {
                        Log.Info($"[GlobalEventManager_OnCharacterDeath] Bagged object {victim.name} died. Removing from bag of {controller.name}");
                    }
                    if (controller != null && victim != null)
                    {
                        BagPassengerManager.RemoveBaggedObject(controller, victim);
                    }
                }
            }
        }
    }
}
