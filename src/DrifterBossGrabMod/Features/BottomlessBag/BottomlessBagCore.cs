#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.UI;
using DrifterBossGrabMod.Balance;
using EntityStates;
using EntityStates.Drifter.Bag;
using EntityStateMachine = RoR2.EntityStateMachine;

namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // BAGGED OBJECT TRACKER
    // ========================================================================================

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

    // ========================================================================================
    // DELAYED AUTO PROMOTE
    // ========================================================================================

    public class DelayedAutoPromote : MonoBehaviour
    {
        private DrifterBagController? _controller;
        private GameObject? _newMain;
        private float _delayTime = 0f;
        private float _elapsedTime = 0f;

        public static void Schedule(DrifterBagController controller, GameObject? newMain, float delay = 0.0f)
        {
            if (delay == 0f)
            {
                ExecutePromotionImmediate(controller, newMain);
                return;
            }

            var go = new GameObject($"DelayedAutoPromote_{BagHelpers.GetSafeName(newMain)}");
            var delayed = go.AddComponent<DelayedAutoPromote>();
            delayed._controller = controller;
            delayed._newMain = newMain;
            delayed._delayTime = Mathf.Abs(delay);
        }

        private static void ExecutePromotionImmediate(DrifterBagController controller, GameObject? newMain)
        {
            if (controller == null || newMain == null || ProjectileRecoveryPatches.IsInProjectileState(newMain))
                return;

            Log.DebugIfEnabled("[ExecutePromotionImmediate] Promoting {0}", BagHelpers.GetSafeName(newMain));

            if (!API.DrifterBagAPI.IsObjectInBag(controller, newMain))
                return;

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

            if (API.DrifterBagAPI.GetAdditionalSeats(controller).TryGetValue(newMain, out var existingSeat) && existingSeat != null)
            {
                if (NetworkServer.active)
                    existingSeat.EjectPassenger(newMain);
                API.DrifterBagAPI.RemoveAdditionalSeat(controller, newMain);
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
                GameObject? previousMain = API.DrifterBagAPI.GetMainPassenger(controller);
                API.DrifterBagAPI.SetMainSeatObject(controller, newMain);
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
                Log.DebugIfEnabled("[ExecutePromotion] Promoting {0} (Delay: {1}s)", BagHelpers.GetSafeName(_newMain), _delayTime);
                if (!API.DrifterBagAPI.IsObjectInBag(_controller, _newMain))
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

                if (API.DrifterBagAPI.GetAdditionalSeats(_controller).TryGetValue(_newMain, out var existingSeat) && existingSeat != null)
                {
                    if (NetworkServer.active)
                        existingSeat.EjectPassenger(_newMain);
                    API.DrifterBagAPI.RemoveAdditionalSeat(_controller, _newMain);
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
                    GameObject? previousMain = API.DrifterBagAPI.GetMainPassenger(_controller);
                    API.DrifterBagAPI.SetMainSeatObject(_controller, _newMain);
                    API.DrifterBagAPI.InvokeOnMainPassengerChanged(_controller, previousMain, _newMain);
                }
            }
            Destroy(gameObject);
        }
    }

    // ========================================================================================
    // BAG PATCHES
    // ========================================================================================

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

                if (passengerObject != null)
                {
                    var prefixModelLocator = passengerObject.GetComponent<ModelLocator>();
                    if (prefixModelLocator != null)
                    {
                        var state = API.DrifterBagAPI.LoadObjectState(__instance, passengerObject);
                        if (state == null)
                        {
                            state = new Core.BaggedObjectStateData();
                            // Early capture before assignment changes any stats
                            state.CalculateFromObject(passengerObject, __instance);
                            API.DrifterBagAPI.SaveObjectState(__instance, passengerObject, state);
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

                // Fill-from-back logic
                if ((!prioritize || mainSeatOccupied) && TryAssignToAdditionalSeat(__instance!, passengerObject, effectiveCapacity, isAlreadyTrackedByThisController))
                {
                    Log.DebugIfEnabled("[AssignPassenger.Prefix] Redirected {0} to AdditionalSeat. _usingAdditionalSeat={1}, skipping original method.",
                        passengerObject.name, _usingAdditionalSeat);
                    return false;
                }

                Log.DebugIfEnabled("[AssignPassenger.Prefix] Proceeding to Main Seat for {0}. _usingAdditionalSeat={1}",
                    passengerObject.name, _usingAdditionalSeat);
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (passengerObject == null || ProjectileRecoveryPatches.IsInProjectileState(passengerObject)) return;

                Log.DebugIfEnabled("[AssignPassenger.Postfix] passengerObject={0}, _usingAdditionalSeat={1}.",
                    passengerObject.name, _usingAdditionalSeat);

                BagHelpers.AddTracker(__instance, passengerObject);

                if (!_usingAdditionalSeat && __instance.vehicleSeat != null && NetworkServer.active)
                {
                    if (__instance.vehicleSeat.NetworkpassengerBodyObject != passengerObject) __instance.vehicleSeat.AssignPassenger(passengerObject);
                }

                var state = GetState(__instance);
                state.AdditionalSeats.TryRemove(passengerObject, out _);

                if (!_usingAdditionalSeat)
                {
                    Log.DebugIfEnabled("[AssignPassenger.Postfix] Assigning to main seat: {0}", passengerObject.name);
                    GameObject? previousMain = GetMainSeatObject(__instance);
                    SetMainSeatObject(__instance, passengerObject);
                    API.DrifterBagAPI.InvokeOnMainPassengerChanged(__instance, previousMain, passengerObject);
                }
                else
                {
                    Log.DebugIfEnabled("[AssignPassenger.Postfix] Skipping main seat assignment for {0} (assigned to additional seat)", passengerObject.name);
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

                if (API.DrifterBagAPI.LoadObjectState(__instance, passengerObject) == null)
                {
                    var newState = new BaggedObjectStateData();
                    newState.CalculateFromObject(passengerObject, __instance);
                    API.DrifterBagAPI.SaveObjectState(__instance, passengerObject, newState);
                }

                if (NetworkServer.active) PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);

                BagPassengerManager.ForceRecalculateMass(__instance);
                state.IncomingObject = null;
                BagCarouselUpdater.UpdateCarousel(__instance);

                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    int finalIndex = state.IntendedSelectedIndex;

                    var currentMain = GetMainSeatObject(__instance);
                    if (finalIndex < 0)
                    {
                        if (currentMain != null)
                        {
                            finalIndex = list.IndexOf(currentMain);
                        }

                        if (finalIndex < 0)
                        {
                            finalIndex = list.Count - 1;
                        }
                    }

                    Log.DebugIfEnabled("[AssignPassenger.Postfix] Updating selection to {0} (Intent was {1}) for {2}",
                        finalIndex, state.IntendedSelectedIndex, passengerObject.name);

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

                Log.DebugIfEnabled("[TryAssignToAdditionalSeat] Searching for seat for {0}. Capacity={1}, Intent={2}.",
                    passengerObject.name, effectiveCapacity, targetIndex);

                // If the user is targeting a specific slot, try to accommodate that slot if it's empty
                var newSeat = AdditionalSeatManager.FindOrCreateEmptySeat(__instance, ref seatDict);
                var list = state.BaggedObjects;
                int passengerInstanceId = passengerObject.GetInstanceID();

                if (newSeat != null)
                {
                    _usingAdditionalSeat = true;
                    Log.DebugIfEnabled("[TryAssignToAdditionalSeat] Found additional seat, setting _usingAdditionalSeat=true for {0}", passengerObject.name);

                    BagHelpers.AddTracker(__instance, passengerObject);
                    if (GetMainSeatObject(__instance) == passengerObject) SetMainSeatObject(__instance, null);

                    if (NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(passengerObject) && !passengerObject.GetComponent<AdditionalSeatBreakoutTimer>())
                    {
                        var timer = passengerObject.AddComponent<AdditionalSeatBreakoutTimer>();
                        timer.controller = __instance;
                        float mass = __instance.CalculateBaggedObjectMass(passengerObject);
                        float finalTime = Mathf.Max(10f - 0.005f * mass, 1f) * (PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.BreakoutTimeMultiplier.Value : 1f);
                        var hc = passengerObject.GetComponent<CharacterBody>();
                        if (hc && hc.isElite) finalTime *= 0.8f;
                        timer.breakoutTime = finalTime;

                        var storedState = API.DrifterBagAPI.LoadObjectState(__instance, passengerObject);
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

                    var existingState = API.DrifterBagAPI.LoadObjectState(__instance, passengerObject);
                    if (existingState == null)
                    {
                        var infoState = new BaggedObjectStateData();
                        infoState.CalculateFromObject(passengerObject, __instance);
                        API.DrifterBagAPI.SaveObjectState(__instance, passengerObject, infoState);
                    }

                    if (NetworkServer.active) PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                    state.IncomingObject = null;
                    BagCarouselUpdater.UpdateCarousel(__instance);
                    if (!DrifterBossGrabPlugin.IsSwappingPassengers) BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                    BagPassengerManager.ForceRecalculateMass(__instance);

                    var currentMain = GetMainSeatObject(__instance);
                    Log.DebugIfEnabled("[TryAssignToAdditionalSeat] Successfully assigned {0} to additional seat. Main seat object={1}",
                        passengerObject.name, currentMain?.name ?? "null");

                    return true;
                }
                else if (!NetworkServer.active)
                {
                    _usingAdditionalSeat = true;
                    Log.DebugIfEnabled("[TryAssignToAdditionalSeat] Client mode, setting _usingAdditionalSeat=true for {0}", passengerObject.name);

                    BagHelpers.AddTracker(__instance, passengerObject);
                    if (!state.ContainsInstanceId(passengerInstanceId))
                    {
                        list.Add(passengerObject);
                        state.AddInstanceId(passengerInstanceId);
                        API.DrifterBagAPI.InvokeOnObjectGrabbed(__instance, passengerObject, list.Count - 1);
                    }

                    var existingState = API.DrifterBagAPI.LoadObjectState(__instance, passengerObject);
                    if (existingState == null || existingState.baseMaxHealth <= 0f)
                    {
                        var infoState = new BaggedObjectStateData();
                        infoState.CalculateFromObject(passengerObject, __instance);
                        API.DrifterBagAPI.SaveObjectState(__instance, passengerObject, infoState);
                    }

                    state.IncomingObject = null;
                    BagCarouselUpdater.UpdateCarousel(__instance);
                    if (!DrifterBossGrabPlugin.IsSwappingPassengers) BagCarouselUpdater.UpdateNetworkBagState(__instance, 0);
                    BagPassengerManager.ForceRecalculateMass(__instance);

                    var currentMain = GetMainSeatObject(__instance);
                    Log.DebugIfEnabled("[TryAssignToAdditionalSeat] Successfully assigned {0} to additional seat (client). Main seat object={1}",
                        passengerObject.name, currentMain?.name ?? "null");

                    return true;
                }
                return false;
            }
        }

        public static void SetMainSeatObject(DrifterBagController controller, GameObject? obj)
        {
            if (controller == null) return;
            var oldObj = GetState(controller).MainSeatObject;

            if (ReferenceEquals(oldObj, obj)) return;

            GetState(controller).MainSeatObject = obj;

            Log.DebugIfEnabled("[SetMainSeatObject] {0}: {1} -> {2}",
                controller.name, oldObj?.name ?? "null", obj?.name ?? "null");

            if (oldObj != null || obj == null)
            {
                API.DrifterBagAPI.UnsetAllOverrides(null, controller.gameObject);
            }
        }

        public static GameObject? GetMainSeatObject(DrifterBagController controller)
        {
            if (controller == null) return null;
            var obj = GetState(controller).MainSeatObject;

            if (BagPassengerManager.IsProcessingThrowRemoval)
            {
                return null;
            }

            if (obj == null || (obj is UnityEngine.Object uo && !uo))
            {
                GetState(controller).MainSeatObject = null;
                return null;
            }
            return obj;
        }
    }

    // ========================================================================================
    // VEHICLE SEAT PATCHES
    // ========================================================================================

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
                    return false;
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

            var seatDict = API.DrifterBagAPI.GetAdditionalSeats(drifterBagController);
            foreach (var kvp in seatDict) if (kvp.Value == __instance && kvp.Key != bodyObject) seatDict.TryRemove(kvp.Key, out _);
            seatDict[bodyObject] = __instance;
        }
    }

    // ========================================================================================
    // GLOBAL EVENT PATCHES
    // ========================================================================================

    [HarmonyPatch(typeof(GlobalEventManager), nameof(GlobalEventManager.OnCharacterDeath))]
    public static class GlobalEventManager_OnCharacterDeath
    {
        [HarmonyPostfix]
        public static void Postfix(DamageReport damageReport)
        {
            if (damageReport == null || damageReport.victimBody == null) return;
            GameObject victim = damageReport.victimBody.gameObject;
            if (victim == null) return;

            foreach (var controller in API.DrifterBagAPI.GetAllControllers())
            {
                var list = API.DrifterBagAPI.GetBaggedObjects(controller);
                if (list != null && list.Contains(victim)) BagPassengerManager.RemoveBaggedObject(controller, victim);
            }

            if (PersistenceObjectManager.IsObjectPersisted(victim)) PersistenceObjectManager.RemovePersistedObject(victim, isDestroying: true);
        }
    }

    // ========================================================================================
    // BAG CAPACITY CALCULATOR
    // ========================================================================================

    public static class BagCapacityCalculator
    {
        private static readonly Dictionary<string, float> _capacityVarsBuffer = new Dictionary<string, float>();
        private static readonly HashSet<int> _countedInstanceIdsBuffer = new HashSet<int>(); public static int GetUtilityMaxStock(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value) return Constants.Limits.SingleCapacity;

            var body = drifterBagController.GetComponent<CharacterBody>();
            int extraSlots = 0;
            var vars = _capacityVarsBuffer;

            if (body)
            {
                vars["H"] = body.maxHealth;
                vars["L"] = body.level;
                vars["C"] = body.skillLocator && body.skillLocator.utility ? body.skillLocator.utility.maxStock : 1;
                vars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
            }
            else
            {
                vars["H"] = 0;
                vars["L"] = 1;
                vars["C"] = 1;
                vars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
            }

            extraSlots = Balance.FormulaParser.EvaluateInt(PluginConfig.Instance.SlotScalingFormula.Value, vars);

            int utilityStocks = (body && body.skillLocator && body.skillLocator.utility) ? body.skillLocator.utility.maxStock : 1;
            int slotCapacity = extraSlots == int.MaxValue ? int.MaxValue : utilityStocks + extraSlots;

            if (PluginConfig.Instance.EnableBalance.Value && body)
            {
                int usedCapacity = GetCurrentBaggedCount(drifterBagController);
                float currentMass = CalculateTotalBagMass(drifterBagController, null);
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(drifterBagController);
                float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                    ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
                    : Constants.Multipliers.DefaultMassMultiplier;
                float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                if (currentMass >= maxMassCapacity) slotCapacity = Math.Max(1, usedCapacity);
            }
            return slotCapacity;
        }

        public static float CalculateTotalBagMass(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            if (drifterBagController == null) return 0f;
            float totalMass = drifterBagController.baggedMass;
            GameObject? predictiveIncomingObject = incomingObject ?? API.DrifterBagAPI.GetIncomingObject(drifterBagController);

            if (predictiveIncomingObject != null)
            {
                totalMass += drifterBagController.CalculateBaggedObjectMass(predictiveIncomingObject);
            }
            return totalMass;
        }

        public static int GetCurrentBaggedCount(DrifterBagController controller)
        {
            if (controller == null) return 0;
            var netController = controller.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController != null) return netController.GetTotalObjectCount();

            var list = API.DrifterBagAPI.GetBaggedObjects(controller);
            if (list == null) return 0;

            int objectsInBag = 0;
            var countedInstanceIds = _countedInstanceIdsBuffer;
            countedInstanceIds.Clear();

            foreach (var obj in list)
            {
                if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                {
                    int instanceId = obj.GetInstanceID();
                    if (!countedInstanceIds.Contains(instanceId))
                    {
                        countedInstanceIds.Add(instanceId);
                        objectsInBag++;
                    }
                }
            }
            return objectsInBag;
        }

        public static bool HasRoomForGrab(DrifterBagController controller, GameObject? incomingObject = null)
        {
            if (controller == null) return false;
            if (PluginConfig.Instance.BottomlessBagEnabled.Value && PluginConfig.Instance.IsSlotScalingFormulaInfinite)
            {
                float currentMass = CalculateTotalBagMass(controller, null);
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
                float overencumbranceMultiplier = PluginConfig.Instance.EnableBalance.Value
                    ? Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor)
                    : Constants.Multipliers.DefaultMassMultiplier;
                float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                bool hasRoom = currentMass < maxMassCapacity;
                if (!hasRoom) API.DrifterBagAPI.InvokeOnBagFull(controller);
                return hasRoom;
            }

            int effectiveCapacity = GetUtilityMaxStock(controller, incomingObject);
            int currentCount = GetCurrentBaggedCount(controller);
            bool hasRoomSlot = currentCount < effectiveCapacity;
            if (!hasRoomSlot) API.DrifterBagAPI.InvokeOnBagFull(controller);
            return hasRoomSlot;
        }

        public static float GetBaggedObjectMass(DrifterBagController controller) => controller ? controller.baggedMass : 0f;
    }

    // ========================================================================================
    // BAG HELPERS
    // ========================================================================================

    public static class BagHelpers
    {
        public static string GetSafeName(UnityEngine.Object? obj) => obj ? obj!.name : "null";
        public static string GetSafeName(object? obj)
        {
            if (obj == null) return "null";
            if (obj is UnityEngine.Object uo) return uo ? uo.name : "null";
            return obj.ToString() ?? "null";
        }

        public static void AddTracker(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return;
            var tracker = obj.GetComponent<BaggedObjectTracker>() ?? obj.AddComponent<BaggedObjectTracker>();
            tracker.obj = obj;
            tracker.controller = controller;

            var esms = obj.GetComponents<EntityStateMachine>();
            foreach (var esm in esms)
            {
                if (esm.customName == "Body")
                {
                    API.DrifterBagAPI.RegisterTrackedESM(esm, tracker);
                    break;
                }
            }
        }

        public static void CleanupEmptyAdditionalSeats(DrifterBagController? controller)
        {
            if (controller == null) return;
            var seatDict = API.DrifterBagAPI.GetAdditionalSeats(controller);
            var seatsToRemove = new List<GameObject>();
            if (seatDict != null)
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value == null || kvp.Value.gameObject == null) seatsToRemove.Add(kvp.Key);
                }
                foreach (var obj in seatsToRemove) seatDict.TryRemove(obj, out _);
            }
        }

        public static RoR2.VehicleSeat? GetAdditionalSeat(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return null;
            var seatDict = API.DrifterBagAPI.GetAdditionalSeats(controller);
            if (seatDict != null && seatDict.TryGetValue(obj, out var seat)) return seat;
            return null;
        }

        public static bool IsBaggedObject(DrifterBagController controller, GameObject? obj)
        {
            if (obj == null || controller == null) return false;
            var list = API.DrifterBagAPI.GetBaggedObjects(controller);
            if (list != null)
            {
                int targetInstanceId = obj.GetInstanceID();
                foreach (var trackedObj in list)
                {
                    if (trackedObj != null && trackedObj.GetInstanceID() == targetInstanceId) return true;
                }
            }
            return false;
        }
    }

    // ========================================================================================
    // BAG PASSENGER MANAGER
    // ========================================================================================

    public static class BagPassengerManager
    {
        private static readonly FieldInfo _baggedMassField = ReflectionCache.DrifterBagController.BaggedMass;
        private static readonly FieldInfo _walkSpeedModifierField = ReflectionCache.BaggedObject.WalkSpeedModifier;

        private static readonly Dictionary<DrifterBagController, CharacterMotor.WalkSpeedPenaltyModifier> _modWalkSpeedModifiers
            = new Dictionary<DrifterBagController, CharacterMotor.WalkSpeedPenaltyModifier>();

        private static readonly List<GameObject> _removeKeysBuffer = new List<GameObject>();
        private static readonly Dictionary<string, float> _penaltyVarsBuffer = new Dictionary<string, float>();

        public static volatile bool IsProcessingThrowRemoval = false;

        public static void MarkMassDirty(DrifterBagController controller)
        {
            if (controller == null) return;
            API.DrifterBagAPI.MarkMassDirty(controller);
        }

        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false, bool skipStateReset = false, bool preserveStateDuringThrow = false)
        {
            if (obj == null) return;
            if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

            int targetInstanceId;
            try { targetInstanceId = obj.GetInstanceID(); } catch { targetInstanceId = -1; }

            // Purge from network ID lists immediately to prevent stale re-sync during transition animations
            if (obj != null && obj.GetComponent<NetworkIdentity>() is { } ni)
            {
                var net = controller.GetComponent<Networking.BottomlessBagNetworkController>();
                if (net) net.RemoveBaggedObjectId(ni.netId);
            }

            GameObject? mainPassengerBefore = API.DrifterBagAPI.GetMainPassenger(controller);
            bool wasMainPassenger = (mainPassengerBefore != null && mainPassengerBefore == obj);

            if (mainPassengerBefore != null && mainPassengerBefore.GetInstanceID() == targetInstanceId)
            {
                API.DrifterBagAPI.SetMainSeatObject(controller, null);
                wasMainPassenger = true;
            }

            var seatDict = API.DrifterBagAPI.GetAdditionalSeats(controller);
            if (seatDict != null)
            {
                seatDict.TryRemove(obj!, out _);
                _removeKeysBuffer.Clear();
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && kvp.Value.NetworkpassengerBodyObject == obj) _removeKeysBuffer.Add(kvp.Key);
                }
                foreach (var key in _removeKeysBuffer) seatDict.TryRemove(key, out _);
            }

            bool isThrowing = ProjectileRecoveryPatches.IsInProjectileState(obj);
            var list = API.DrifterBagAPI.GetBaggedObjects(controller);
            if (list == null) return;

            var tracker = obj!.GetComponent<BaggedObjectTracker>();
            if (tracker != null)
            {
                tracker.isRemovingManual = true;
                var esms = obj.GetComponents<EntityStateMachine>();
                foreach (var esm in esms)
                {
                    if (esm.customName == "Body")
                    {
                        API.DrifterBagAPI.UnregisterTrackedESM(esm);
                        break;
                    }
                }
                UnityEngine.Object.Destroy(tracker);
            }

            list.RemoveAll(x => ReferenceEquals(x, null) || (x is UnityEngine.Object uo && !uo) || (targetInstanceId != -1 && x.GetInstanceID() == targetInstanceId));
            if (targetInstanceId != -1) API.DrifterBagAPI.RemoveInstanceId(controller, targetInstanceId);

            if (wasMainPassenger)
            {
                API.DrifterBagAPI.InvokeOnMainPassengerChanged(controller, mainPassengerBefore, null);

                if (NetworkServer.active && controller.vehicleSeat != null && controller.vehicleSeat.NetworkpassengerBodyObject == obj
                    && !isDestroying && obj != null && obj.activeInHierarchy)
                {
                    controller.vehicleSeat.EjectPassenger(obj);
                }

                BaggedObjectStatePatches.UnsetAllOverrides(null, controller.gameObject);
                API.DrifterBagAPI.SetMainSeatObject(controller, null);

                if (controller != null && NetworkServer.active && !controller!.hasAuthority && controller.GetComponent<Networking.BottomlessBagNetworkController>() is { } nc ? nc.autoPromoteMainSeat && list.Count > 0 : PluginConfig.Instance.AutoPromoteMainSeat.Value && list.Count > 0 && (NetworkServer.active || (controller && controller!.hasAuthority)))
                {
                    var newMain = list[0];
                    if (newMain != null && !ProjectileRecoveryPatches.IsInProjectileState(newMain))
                    {
                        DelayedAutoPromote.Schedule(controller!, newMain, 0.05f);
                    }
                }
            }

            if (isThrowing) BagHelpers.CleanupEmptyAdditionalSeats(controller);

            if (preserveStateDuringThrow)
            {
                if (controller != null && obj != null) API.DrifterBagAPI.PreserveStateForThrow(controller, obj);
            }
            else if (isDestroying || (isThrowing == false && !DrifterBossGrabPlugin.IsSwappingPassengers))
            {
                if (controller != null && obj != null) API.DrifterBagAPI.CleanupObjectState(controller, obj);
                if (obj != null) BaggedObjectStatePatches.BaggedObject_OnExit.ClearObjectSuccessfullyInitialized(obj);
            }

            if (wasMainPassenger && controller != null && obj != null) BaggedObjectStatePatches.ForceCleanupOverrides(controller, obj);

            if (obj != null)
            {
                var timer = obj.GetComponent<AdditionalSeatBreakoutTimer>();
                if (timer != null) UnityEngine.Object.Destroy(timer);
            }

            if (NetworkServer.active && list != null) PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, controller);

            if (controller != null)
            {
                IsProcessingThrowRemoval = isThrowing;
                BagCarouselUpdater.UpdateCarousel(controller, wasMainPassenger ? 1 : 0);
                BagCarouselUpdater.UpdateNetworkBagState(controller, wasMainPassenger ? 1 : 0);
                IsProcessingThrowRemoval = false;

                if (!skipStateReset)
                {
                    var esm = EntityStateMachine.FindByCustomName(controller.gameObject, "Bag");
                    if (esm != null)
                    {
                        var currentMain = API.DrifterBagAPI.GetMainPassenger(controller);
                        if (currentMain != null) esm.SetNextState(new BaggedObject { targetObject = currentMain });
                        else esm.SetNextStateToMain();
                    }
                }
                MarkMassDirty(controller);
            }

            if (obj != null && !isDestroying && !isThrowing && controller != null)
            {
                API.DrifterBagAPI.RestoreColliders(controller, obj);
            }

            var teleporterInteraction = (obj != null) ? obj.GetComponent<RoR2.TeleporterInteraction>() : null;
            if (teleporterInteraction != null && obj != null)
            {
                teleporterInteraction.enabled = true;
                if (PluginConfig.Instance.EnableObjectPersistence.Value)
                {
                    PersistenceManager.UnmarkTeleporterAsBagged(obj);
                    MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);
                    var primary = MultiTeleporterTracker.GetPrimary();
                    if (primary != null) TeleporterInteraction.instance = primary;
                }
            }

            if (obj != null && controller != null) API.DrifterBagAPI.InvokeOnObjectReleased(controller, obj, isDestroying);
        }

        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;
            if (!API.DrifterBagAPI.IsMassDirty(controller)) return;

            float previousTotalMass = _baggedMassField != null ? (float)_baggedMassField.GetValue(controller) : 0f;
            float totalMass = 0f;

            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.All)
            {
                var list = API.DrifterBagAPI.GetBaggedObjects(controller);
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                        if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj)) totalMass += controller.CalculateBaggedObjectMass(obj);
                    }
                }
            }
            else
            {
                var mainSeatObj = API.DrifterBagAPI.GetMainPassenger(controller);
                if (mainSeatObj != null && !ProjectileRecoveryPatches.IsInProjectileState(mainSeatObj)) totalMass = controller.CalculateBaggedObjectMass(mainSeatObj);
            }

            totalMass = Mathf.Max(totalMass, 0f);
            if (_baggedMassField != null)
            {
                _baggedMassField.SetValue(controller, totalMass);
                controller.GetComponent<CharacterBody>()?.RecalculateStats();
                var esm = EntityStateMachine.FindByCustomName(controller.gameObject, "Bag");
                if (esm != null && esm.state is BaggedObject baggedObject) API.DrifterBagAPI.UpdateBagScale(baggedObject, totalMass);
                UpdateModWalkSpeedPenalty(controller, totalMass);
            }

            UIPatches.UpdateMassCapacityUIOnCapacityChange(controller);
            if (PluginConfig.Instance.EnableBalance.Value && (PluginConfig.Instance.IsBagScaleCapInfinite || PluginConfig.Instance.ParsedBagScaleCap > 1f))
            {
                UpdateUncappedBagScale(controller, totalMass);
            }

            API.DrifterBagAPI.ClearMassDirty(controller);
            API.DrifterBagAPI.InvokeOnMassRecalculated(controller, totalMass, previousTotalMass);

            if (PluginConfig.Instance.EnableBalance.Value)
            {
                float massCapacity = CapacityScalingSystem.CalculateMassCapacity(controller);
                if (massCapacity > 0f && totalMass / massCapacity > 1.0f) API.DrifterBagAPI.InvokeOnOverencumbered(controller, totalMass / massCapacity);
            }
        }

        public static void UpdateModWalkSpeedPenalty(DrifterBagController controller, float totalMass)
        {
            if (controller == null) return;
            var motor = controller.GetComponent<CharacterMotor>();
            if (motor == null) return;

            float penalty = 0f;
            if (PluginConfig.Instance.EnableBalance.Value)
            {
                var body = controller.GetComponent<CharacterBody>();
                var vars = _penaltyVarsBuffer;
                vars.Clear();
                vars["T"] = totalMass;
                vars["M"] = CapacityScalingSystem.CalculateMassCapacity(controller);
                vars["C"] = CapacityScalingSystem.GetTotalCapacity(controller);
                vars["H"] = body ? body.maxHealth : 0f;
                vars["L"] = body ? body.level : 1f;
                vars["MC"] = PluginConfig.Instance.ParsedMassCap;
                vars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
                penalty = FormulaParser.Evaluate(PluginConfig.Instance.MovespeedPenaltyFormula.Value, vars);
            }

            if (totalMass <= 0f || penalty <= 0f) { RemoveModWalkSpeedPenalty(controller); return; }

            if (_modWalkSpeedModifiers.TryGetValue(controller, out var modifier))
            {
                modifier.penalty = penalty;
                motor.RecalculateWalkSpeedPenalty();
            }
            else
            {
                var newModifier = new CharacterMotor.WalkSpeedPenaltyModifier { penalty = penalty };
                motor.AddWalkSpeedPenalty(newModifier);
                _modWalkSpeedModifiers[controller] = newModifier;
            }
        }

        public static void RemoveModWalkSpeedPenalty(DrifterBagController controller)
        {
            if (controller == null) return;
            if (_modWalkSpeedModifiers.TryGetValue(controller, out var modifier))
            {
                controller.GetComponent<CharacterMotor>()?.RemoveWalkSpeedPenalty(modifier);
                _modWalkSpeedModifiers.Remove(controller);
            }
        }

        public static void SuppressVanillaWalkSpeedModifier(BaggedObject instance)
        {
            if (instance == null) return;
            var modifier = _walkSpeedModifierField?.GetValue(instance) as CharacterMotor.WalkSpeedPenaltyModifier;
            if (modifier != null)
            {
                instance.outer?.GetComponent<CharacterMotor>()?.RemoveWalkSpeedPenalty(modifier);
                _walkSpeedModifierField?.SetValue(instance, null);
            }
        }

        public static void UpdateUncappedBagScale(DrifterBagController controller, float mass)
        {
            if (controller == null) return;
            var component = API.DrifterBagAPI.GetUncappedBagScale(controller) ?? controller.gameObject.GetComponent<UncappedBagScaleComponent>();

            if (component == null)
            {
                component = controller.gameObject.AddComponent<UncappedBagScaleComponent>();
                component.Initialize(controller);
            }

            if (component != null && component.IsInitialized)
            {
                API.DrifterBagAPI.SetUncappedBagScale(controller, component);
                component.UpdateScaleFromMass(mass);
            }
        }
    }

    // ========================================================================================
    // UNCAPPED BAG SCALE COMPONENT
    // ========================================================================================

    public class UncappedBagScaleComponent : MonoBehaviour
    {
        private DrifterBagController? _bagController;
        private Transform[]? _filteredBones;
        private Vector3[]? _originalBoneScales;
        private float _targetScale = 1f;
        private float _currentScale = 1f;
        private bool _isInitialized = false;

        public bool IsInitialized => _isInitialized;
        public float TargetScale { get => _targetScale; set => _targetScale = Mathf.Max(value, 1.0f); }

        public void Initialize(DrifterBagController bagController)
        {
            if (_isInitialized) return;
            if (bagController == null) { Log.Error("[UncappedBagScaleComponent] Cannot initialize with null bag controller"); return; }

            _bagController = bagController;
            if (bagController.GetComponent<CharacterBody>()?.modelLocator?.modelTransform?.Find("meshBag")?.GetComponent<SkinnedMeshRenderer>() is { } smr)
            {
                var bones = smr.bones;
                var keywords = new[] { "bagMaster_l", "bag04_l", "bagBulk_l", "bagBulk_l_end", "bagBulgeBt_l", "bagBulgeRt_l", "bagBulgeRt_l_end", "bagBulgeLf_l", "bagBulgeLf_l_end", "bagPocketRt_l", "bagPocketRt_l_end", "bagPocketLf_l", "bagPocketLf_l_end", "bagFlap1_l", "bagFlap2_l", "bagFlap3_l" };
                var filtered = new List<Transform>();
                var scales = new List<Vector3>();

                foreach (var b in bones) if (b && keywords.Any(k => k.Equals(b.name, StringComparison.OrdinalIgnoreCase))) { filtered.Add(b); scales.Add(b.localScale); }

                _filteredBones = filtered.ToArray();
                _originalBoneScales = scales.ToArray();
                _isInitialized = true;
                Log.DebugIfEnabled("[UncappedBagScaleComponent] Initialized with {0} bones", _filteredBones.Length);
            }
        }

        public void UpdateScaleFromMass(float mass)
        {
            if (!_isInitialized || _filteredBones == null) return;
            float max = _bagController ? DrifterBossGrabMod.Balance.CapacityScalingSystem.CalculateMassCapacity(_bagController!) : DrifterBagController.maxMass;
            if (mass <= max) { TargetScale = 1.0f; return; }

            float newScale = 1.0f + (mass - 1f) / (max - 1f);
            if (!PluginConfig.Instance.IsBagScaleCapInfinite) newScale = Mathf.Min(newScale, PluginConfig.Instance.ParsedBagScaleCap);
            TargetScale = newScale;
            Log.DebugIfEnabled("[UncappedBagScaleComponent] Mass {0} > max {1}, scale {2:F2}", mass, max, newScale);
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _filteredBones == null || _originalBoneScales == null || _targetScale <= 1.0f || Mathf.Approximately(_currentScale, _targetScale)) return;
            _currentScale = Mathf.Lerp(_currentScale, _targetScale, Time.deltaTime * 10f);
            for (int i = 0; i < _filteredBones.Length; i++) if (_filteredBones[i]) _filteredBones[i].localScale = _originalBoneScales[i] * _currentScale;
        }

        private void OnDestroy() { ResetBoneScales(); _isInitialized = false; }
        public void ResetBoneScales() { if (_filteredBones == null || _originalBoneScales == null) return; for (int i = 0; i < _filteredBones.Length; i++) if (_filteredBones[i]) _filteredBones[i].localScale = _originalBoneScales[i]; _currentScale = _targetScale = 1.0f; }
    }
}
