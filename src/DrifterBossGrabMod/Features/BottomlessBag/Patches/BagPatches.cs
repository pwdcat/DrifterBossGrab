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
                ExecutePromotionImmediate(controller, newMain, skipStateReset);
                return;
            }

            var go = new GameObject($"DelayedAutoPromote_{BagHelpers.GetSafeName(newMain)}");
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
                Log.Debug($"[ExecutePromotionImmediate] START: Promoting {BagHelpers.GetSafeName(newMain)}");

            var state = BagPatches.GetState(controller);
            lock (state.BagLock)
            {
                if (!state.BaggedObjects.Contains(newMain))
                    return;
            }

            var bagStateMachine = GetBagStateMachine(controller);
            if (NetworkServer.active)
            {
                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag")
                    {
                        if (esm.state is BaggedObject)
                        {
                            esm.SetNextStateToMain();
                        }
                        break;
                    }
                }
            }

            if (state.AdditionalSeats.TryGetValue(newMain, out var existingSeat) && existingSeat != null)
            {
                if (NetworkServer.active)
                    existingSeat.EjectPassenger(newMain);
                state.AdditionalSeats.TryRemove(newMain, out _);
            }

            if (NetworkServer.active)
            {
                if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
                {
                    var currentPassenger = controller.vehicleSeat.NetworkpassengerBodyObject;
                    bool isDeadOrDestroyed = currentPassenger == null ||
                        (currentPassenger.GetComponent<HealthComponent>()?.alive == false) ||
                        (currentPassenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0);
                    if (isDeadOrDestroyed)
                    {
                        controller.vehicleSeat.EjectPassenger();
                    }
                }

                controller.AssignPassenger(newMain);

                if (controller.vehicleSeat != null)
                {
                    if (controller.vehicleSeat.NetworkpassengerBodyObject != newMain)
                    {
                        controller.vehicleSeat.AssignPassenger(newMain);
                    }

                    var stateMachines = controller.GetComponents<EntityStateMachine>();
                    foreach (var esm in stateMachines)
                    {
                        if (esm.customName == "Bag")
                        {
                            var newState = new BaggedObject();
                            newState.targetObject = newMain;
                            esm.SetNextState(newState);
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

        private static EntityStateMachine? GetBagStateMachine(DrifterBagController controller)
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
            if (esm != null && esm.state is BaggedObject bagged)
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

                if (NetworkServer.active)
                {
                    var stateMachines = _controller.GetComponents<EntityStateMachine>();
                    foreach (var esm in stateMachines)
                    {
                        if (esm.customName == "Bag")
                        {
                            if (esm.state is BaggedObject)
                            {
                                esm.SetNextStateToMain();
                            }
                            break;
                        }
                    }
                }

                if (state.AdditionalSeats.TryGetValue(_newMain, out var existingSeat) && existingSeat != null)
                {
                    if (NetworkServer.active)
                        existingSeat.EjectPassenger(_newMain);
                    state.AdditionalSeats.TryRemove(_newMain, out _);
                }

                if (NetworkServer.active)
                {
                    if (_controller.vehicleSeat != null && _controller.vehicleSeat.hasPassenger)
                    {
                        var currentPassenger = _controller.vehicleSeat.NetworkpassengerBodyObject;
                        bool isDeadOrDestroyed = currentPassenger == null ||
                            (currentPassenger.GetComponent<HealthComponent>()?.alive == false) ||
                            (currentPassenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0);
                        if (isDeadOrDestroyed)
                        {
                            _controller.vehicleSeat.EjectPassenger();
                        }
                    }

                    _controller.AssignPassenger(_newMain);

                    if (_controller.vehicleSeat != null)
                    {
                        if (_controller.vehicleSeat.NetworkpassengerBodyObject != _newMain)
                        {
                            _controller.vehicleSeat.AssignPassenger(_newMain);
                        }

                        var stateMachines = _controller.GetComponents<EntityStateMachine>();
                        foreach (var esm in stateMachines)
                        {
                            if (esm.customName == "Bag")
                            {
                                var newState = new BaggedObject();
                                newState.targetObject = _newMain;
                                esm.SetNextState(newState);
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

        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            private static bool _usingAdditionalSeat = false;

            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value && passengerObject != null)
                {
                    var state = GetState(__instance);
                    Log.Info($"[AssignPassenger.Prefix] START: incoming={passengerObject.name}");
                }

                if (passengerObject != null)
                {
                    var prefixModelLocator = passengerObject.GetComponent<ModelLocator>();
                    if (prefixModelLocator != null)
                    {
                        var state = BaggedObjectPatches.LoadObjectState(__instance, passengerObject);
                        if (state == null)
                        {
                            state = new Core.BaggedObjectStateData();
                            // Early capture before assignment changes any stats
                            state.CalculateFromObject(passengerObject, __instance);
                            BaggedObjectPatches.SaveObjectState(__instance, passengerObject, state);
                        }

                        if (!state.hasCapturedModelTransformState)
                        {
                            state.hasCapturedModelTransformState = true;
                        }
                    }
                }

                _usingAdditionalSeat = false;
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject!.name)) return false;
                if (passengerObject == null) return true;

                if (ProjectileRecoveryPatches.IsInProjectileState(passengerObject))
                {
                    ProjectileRecoveryPatches.RemoveFromProjectileState(passengerObject);
                }

                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null) modelLocator.dontDetatchFromParent = true;

                var body = passengerObject.GetComponent<CharacterBody>();
                if (body)
                {
                    if (body.baseMaxHealth <= 0 || body.levelMaxHealth < 0 ||
                        body.teamComponent == null || body.teamComponent.teamIndex < 0) return false;

                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                    }
                }

                if (body != null && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable))
                {
                    var bagState = GetState(__instance);
                    if (!bagState.DisabledCollidersByObject.ContainsKey(passengerObject))
                    {
                        bagState.DisabledCollidersByObject[passengerObject] = new Dictionary<Collider, bool>();
                    }
                    BodyColliderCache.DisableMovementColliders(passengerObject, bagState.DisabledCollidersByObject[passengerObject]);
                }

                var teleporterInteraction = passengerObject.GetComponent<RoR2.TeleporterInteraction>();
                if (teleporterInteraction != null)
                {
                    teleporterInteraction.enabled = false;
                    PersistenceManager.MarkTeleporterAsBagged(passengerObject);
                    MultiTeleporterTracker.UnregisterSecondary(teleporterInteraction);
                }

                PersistenceManager.RemovePersistedObject(passengerObject);
                PersistenceObjectsTracker.TrackBaggedObject(passengerObject);

                if (__instance != null) GetState(__instance).IncomingObject = passengerObject;

                int effectiveCapacity = __instance != null ? BagCapacityCalculator.GetUtilityMaxStock(__instance!, null) : 1;
                var list = (__instance != null) ? GetState(__instance!).BaggedObjects : null;
                if (list == null) return true;

                int objectsInBag = BagCapacityCalculator.GetCurrentBaggedCount(__instance!);
                int passengerInstanceId = passengerObject.GetInstanceID();
                bool isAlreadyTrackedByThisController = GetState(__instance!).ContainsInstanceId(passengerInstanceId);

                // We MUST NOT abort the grab if the object is already entering the state machine.
                // The HasRoomForGrab check already prevents the skill from firing if at capacity.
                // If we get here, we must accept the object, even if it exceeds capacity gracefully.

                if (effectiveCapacity <= 1 && isAlreadyTrackedByThisController)
                {
                    bool isAlreadyInMainSeat = __instance!.vehicleSeat != null &&
                        __instance.vehicleSeat.hasPassenger &&
                        ReferenceEquals(__instance.vehicleSeat.NetworkpassengerBodyObject, passengerObject);
                    if (isAlreadyInMainSeat) return false;
                }

                bool prioritize = PluginConfig.Instance.PrioritizeMainSeat.Value;
                if (NetworkServer.active && __instance != null)
                {
                    var netController = __instance.GetComponent<Networking.BottomlessBagNetworkController>();
                    if (netController != null && !__instance.hasAuthority) prioritize = netController.prioritizeMainSeat;
                }

                bool mainSeatOccupied = __instance != null && __instance.vehicleSeat != null && __instance.vehicleSeat.hasPassenger;

                // Fill-from-back logic: if prioritize is false OR main seat is already occupied, route to additional seat
                if ((!prioritize || mainSeatOccupied) && TryAssignToAdditionalSeat(__instance!, passengerObject, effectiveCapacity, isAlreadyTrackedByThisController))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AssignPassenger.Prefix] Redirected {passengerObject.name} to AdditionalSeat. _usingAdditionalSeat={_usingAdditionalSeat}, skipping original method.");
                    }
                    return false;
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AssignPassenger.Prefix] Proceeding to Main Seat for {passengerObject.name}. _usingAdditionalSeat={_usingAdditionalSeat}");
                }
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (passengerObject == null || ProjectileRecoveryPatches.IsInProjectileState(passengerObject)) return;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AssignPassenger.Postfix] START: passengerObject={passengerObject.name}, _usingAdditionalSeat={_usingAdditionalSeat}.");
                }

                BagHelpers.AddTracker(__instance, passengerObject);

                if (!_usingAdditionalSeat && __instance.vehicleSeat != null && NetworkServer.active)
                {
                    if (__instance.vehicleSeat.NetworkpassengerBodyObject != passengerObject) __instance.vehicleSeat.AssignPassenger(passengerObject);
                }

                var state = GetState(__instance);
                state.AdditionalSeats.TryRemove(passengerObject, out _);

                if (!_usingAdditionalSeat)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AssignPassenger.Postfix] Assigning to main seat: {passengerObject.name}");
                    }
                    GameObject? previousMain = GetMainSeatObject(__instance);
                    SetMainSeatObject(__instance, passengerObject);
                    API.DrifterBagAPI.InvokeOnMainPassengerChanged(__instance, previousMain, passengerObject);
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AssignPassenger.Postfix] Skipping main seat assignment for {passengerObject.name} (assigned to additional seat)");
                    }
                }

                var list = state.BaggedObjects;
                if (!state.ContainsInstanceId(passengerObject.GetInstanceID()))
                {
                    list.Add(passengerObject);
                    state.AddInstanceId(passengerObject.GetInstanceID());

                    // Synchronously update network state on client to prevent race conditions
                    var netController = __instance.GetComponent<Networking.BottomlessBagNetworkController>();
                    if (netController != null)
                    {
                        var ni = passengerObject.GetComponent<NetworkIdentity>();
                        if (ni != null) netController.TryAddBaggedObjectId(ni.netId);
                    }

                    int slotIndex = _usingAdditionalSeat ? -1 : list.Count - 1;
                    API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, slotIndex);
                }

                if (BaggedObjectPatches.LoadObjectState(__instance, passengerObject) == null)
                {
                    var newState = new BaggedObjectStateData();
                    newState.CalculateFromObject(passengerObject, __instance);
                    BaggedObjectPatches.SaveObjectState(__instance, passengerObject, newState);
                }

                if (NetworkServer.active) PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);

                BagPassengerManager.ForceRecalculateMass(__instance);
                state.IncomingObject = null;
                BagCarouselUpdater.UpdateCarousel(__instance);

                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    int finalIndex = state.IntendedSelectedIndex;

                    // If no explicit intent was set, or if we want to stay on the current occupant:
                    // Determine if the current selection is valid.
                    var currentMain = GetMainSeatObject(__instance);
                    if (finalIndex < 0)
                    {
                        // Default logic: If we have a main passenger, stay on them.
                        if (currentMain != null)
                        {
                            finalIndex = list.IndexOf(currentMain);
                        }

                        // Fallback: If still not set, target the newly grabbed object 
                        // only if we were looking at an empty slot.
                        if (finalIndex < 0)
                        {
                            finalIndex = list.Count - 1;
                        }
                    }

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[AssignPassenger.Postfix] Updating selection to {finalIndex} (Intent was {state.IntendedSelectedIndex}) for {passengerObject.name}");

                    BagCarouselUpdater.UpdateNetworkBagState(__instance, finalIndex);

                    // Clear intent after grab
                    state.IntendedSelectedIndex = -1;
                }
                DamagePreviewOverlay.InvalidateAllCaches();
            }

            private static bool TryAssignToAdditionalSeat(DrifterBagController __instance, GameObject passengerObject, int effectiveCapacity, bool isAlreadyTrackedByThisController)
            {
                if (isAlreadyTrackedByThisController || effectiveCapacity <= 1) return false;

                var state = GetState(__instance);
                int targetIndex = state.IntendedSelectedIndex;
                var seatDict = state.AdditionalSeats;

                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[TryAssignToAdditionalSeat] Searching for seat for {passengerObject.name}. Capacity={effectiveCapacity}, Intent={targetIndex}.");

                // If the user is targeting a specific slot, try to accommodate that slot if it's empty
                var newSeat = AdditionalSeatManager.FindOrCreateEmptySeat(__instance, ref seatDict);
                var list = state.BaggedObjects;
                int passengerInstanceId = passengerObject.GetInstanceID();

                if (newSeat != null)
                {
                    _usingAdditionalSeat = true;
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[TryAssignToAdditionalSeat] Found additional seat, setting _usingAdditionalSeat=true for {passengerObject.name}");

                    BagHelpers.AddTracker(__instance, passengerObject);
                    if (GetMainSeatObject(__instance) == passengerObject) SetMainSeatObject(__instance, null);

                    if (NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(passengerObject) && !passengerObject.GetComponent<AdditionalSeatBreakoutTimer>())
                    {
                        var timer = passengerObject.AddComponent<AdditionalSeatBreakoutTimer>();
                        timer.controller = __instance;
                        float mass = __instance.CalculateBaggedObjectMass(passengerObject);
                        float finalTime = Mathf.Max(10f - 0.005f * mass, 1f) * PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                        var hc = passengerObject.GetComponent<CharacterBody>();
                        if (hc && hc.isElite) finalTime *= 0.8f;
                        timer.breakoutTime = finalTime;

                        var storedState = BaggedObjectPatches.LoadObjectState(__instance, passengerObject);
                        if (storedState != null)
                        {
                            if (storedState.breakoutTime > 0f) timer.breakoutTime = storedState.breakoutTime;
                            timer.SetElapsedBreakoutTime(storedState.elapsedBreakoutTime);
                            timer.breakoutAttempts = storedState.breakoutAttempts;
                        }
                    }

                    seatDict[passengerObject] = newSeat;
                    if (NetworkServer.active)
                    {
                        newSeat.AssignPassenger(passengerObject);
                        var body = passengerObject.GetComponent<CharacterBody>();
                        if (body != null)
                        {
                            if (!state.DisabledCollidersByObject.ContainsKey(passengerObject)) state.DisabledCollidersByObject[passengerObject] = new Dictionary<Collider, bool>();
                            BodyColliderCache.DisableMovementColliders(passengerObject, state.DisabledCollidersByObject[passengerObject]);
                        }
                    }

                    if (!state.ContainsInstanceId(passengerInstanceId))
                    {
                        list.Add(passengerObject);
                        state.AddInstanceId(passengerInstanceId);
                        API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, list.Count - 1);
                    }

                    var existingState = BaggedObjectPatches.LoadObjectState(__instance, passengerObject);
                    if (existingState == null)
                    {
                        var infoState = new BaggedObjectStateData();
                        infoState.CalculateFromObject(passengerObject, __instance);
                        BaggedObjectPatches.SaveObjectState(__instance, passengerObject, infoState);
                    }

                    if (NetworkServer.active) PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                    state.IncomingObject = null;
                    BagCarouselUpdater.UpdateCarousel(__instance);
                    if (!DrifterBossGrabPlugin.IsSwappingPassengers) BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                    BagPassengerManager.ForceRecalculateMass(__instance);

                    var currentMain = GetMainSeatObject(__instance);
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[TryAssignToAdditionalSeat] Successfully assigned {passengerObject.name} to additional seat. Main seat object={currentMain?.name ?? "null"}");

                    return true;
                }
                else if (!NetworkServer.active)
                {
                    _usingAdditionalSeat = true;
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[TryAssignToAdditionalSeat] Client mode, setting _usingAdditionalSeat=true for {passengerObject.name}");

                    BagHelpers.AddTracker(__instance, passengerObject);
                    if (!state.ContainsInstanceId(passengerInstanceId))
                    {
                        list.Add(passengerObject);
                        state.AddInstanceId(passengerInstanceId);
                        API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, list.Count - 1);
                    }

                    var existingState = BaggedObjectPatches.LoadObjectState(__instance, passengerObject);
                    if (existingState == null || existingState.baseMaxHealth <= 0f)
                    {
                        var infoState = new BaggedObjectStateData();
                        infoState.CalculateFromObject(passengerObject, __instance);
                        BaggedObjectPatches.SaveObjectState(__instance, passengerObject, infoState);
                    }

                    state.IncomingObject = null;
                    BagCarouselUpdater.UpdateCarousel(__instance);
                    if (!DrifterBossGrabPlugin.IsSwappingPassengers) BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                    BagPassengerManager.ForceRecalculateMass(__instance);

                    var currentMain = GetMainSeatObject(__instance);
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[TryAssignToAdditionalSeat] Successfully assigned {passengerObject.name} to additional seat (client). Main seat object={currentMain?.name ?? "null"}");

                    return true;
                }
                return false;
            }
        }

        public static void SetMainSeatObject(DrifterBagController controller, GameObject? obj)
        {
            if (controller == null) return;
            var oldObj = GetState(controller).MainSeatObject;
            GetState(controller).MainSeatObject = obj;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[SetMainSeatObject] {controller.name}: {oldObj?.name ?? "null"} -> {obj?.name ?? "null"}");
            }
        }

        public static GameObject? GetMainSeatObject(DrifterBagController controller)
        {
            if (controller == null) return null;
            var obj = GetState(controller).MainSeatObject;
            if (obj == null || (obj is UnityEngine.Object uo && !uo))
            {
                GetState(controller).MainSeatObject = null;
                return null;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[GetMainSeatObject] {controller.name}: returning {obj?.name ?? "null"}");
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
            if (!UnityEngine.Networking.NetworkServer.active && bodyObject != null)
            {
                var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
                if (drifterBagController != null)
                {
                    // Manually parent the object on the client to prevent it from dropping to the ground
                    // Vanilla AssignPassenger does nothing on clients and returns false
                    bodyObject.transform.SetParent(__instance.transform);
                    bodyObject.transform.localPosition = Vector3.zero;
                    bodyObject.transform.localRotation = Quaternion.identity;

                    if (__instance.disableAllCollidersAndHurtboxes)
                    {
                        var allColliders = bodyObject.GetComponentsInChildren<Collider>();
                        foreach (var collider in allColliders) if (collider != null) collider.enabled = false;
                        var characterBody = bodyObject.GetComponent<CharacterBody>();
                        if (characterBody != null && characterBody.modelLocator != null)
                        {
                            var modelTransform = characterBody.modelLocator.modelTransform;
                            if (modelTransform != null)
                            {
                                var hurtBoxGroup = modelTransform.GetComponent<RoR2.HurtBoxGroup>();
                                if (hurtBoxGroup != null) hurtBoxGroup.hurtBoxesDeactivatorCounter++;
                            }
                        }
                    }
                    return false; // Block vanilla warning
                }
            }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(RoR2.VehicleSeat __instance, GameObject bodyObject)
        {
            if (bodyObject == null || !NetworkServer.active) return;
            var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
            if (drifterBagController == null) return;
            if (__instance == drifterBagController.vehicleSeat) return;

            var seatDict = BagPatches.GetState(drifterBagController).AdditionalSeats;
            foreach (var kvp in seatDict) if (kvp.Value == __instance && kvp.Key != bodyObject) seatDict.TryRemove(kvp.Key, out _);
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
            if (victim == null) return;

            foreach (var controller in BagPatches.GetAllControllers())
            {
                var list = BagPatches.GetState(controller).BaggedObjects;
                if (list != null && list.Contains(victim)) BagPassengerManager.RemoveBaggedObject(controller, victim);
            }

            if (PersistenceObjectManager.IsObjectPersisted(victim)) PersistenceObjectManager.RemovePersistedObject(victim, isDestroying: true);
        }
    }
}
