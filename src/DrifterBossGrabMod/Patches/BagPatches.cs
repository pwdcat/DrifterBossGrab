using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
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
        private float _delayTime = Constants.Multipliers.DefaultMassMultiplier;
        private float _elapsedTime = 0f;
        private bool _skipStateReset = false;

        public static void Schedule(DrifterBagController controller, GameObject? newMain, float delay = 0.0f, bool skipStateReset = false)
        {
            var go = new GameObject($"DelayedAutoPromote_{newMain?.name ?? "null"}");
            var delayed = go.AddComponent<DelayedAutoPromote>();
            delayed._controller = controller;
            delayed._newMain = newMain;
            delayed._delayTime = delay;
            delayed._skipStateReset = skipStateReset;

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

            if (_controller != null && _newMain != null && !OtherPatches.IsInProjectileState(_newMain))
            {
                // Check if the object is still in the bag
                var list = BagPatches.GetState(_controller).BaggedObjects;
                if (list != null)
                {
                    bool stillInBag = list.Contains(_newMain);
                    if (!stillInBag)
                    {

                        Destroy(gameObject);
                        return;
                    }
                }

                // Eject from additional seat if needed
                var seatDict = BagPatches.GetState(_controller).AdditionalSeats;
                if (seatDict != null)
                {
                    if (seatDict.ContainsKey(_newMain))
                    {
                        foreach (var kvp in seatDict)
                        {
                            if (kvp.Key == _newMain && kvp.Value != null)
                            {

                                if (NetworkServer.active)
                                {
                                    kvp.Value.EjectPassenger(_newMain);
                                }
                                seatDict.TryRemove(_newMain, out _);
                                break;
                            }
                        }
                    }
                }

                // Assign to main seat
                if (NetworkServer.active)
                {
                    _controller.AssignPassenger(_newMain);

                    if (_controller.vehicleSeat != null)
                    {
                        if (_controller.vehicleSeat.NetworkpassengerBodyObject != _newMain)
                        {

                            _controller.vehicleSeat.AssignPassenger(_newMain);

                        }

                        // Force transition to BaggedObject state (reset was skipped).
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
                    BagPatches.SetMainSeatObject(_controller, _newMain);

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

                _usingAdditionalSeat = false;
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }
                if (passengerObject == null) return true;

                // If the object was previously thrown and grabbed mid-air, it is no longer a projectile
                if (OtherPatches.IsInProjectileState(passengerObject))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AssignPassenger] Object {passengerObject.name} grabbed mid-air. Removing from projectile tracking.");
                    }
                    OtherPatches.RemoveFromProjectileState(passengerObject);
                }

                CharacterBody? body = null;
                var localDisabledStates = new Dictionary<GameObject, bool>();
                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null)
                {
                    bool isCycling = DrifterBossGrabPlugin.IsSwappingPassengers;
                    var existingPreserver = passengerObject.GetComponent<ModelStatePreserver>();

                    if (!isCycling && PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator.modelTransform != null && existingPreserver == null)
                    {

                        passengerObject.AddComponent<ModelStatePreserver>();
                    }

                    if (!isCycling && PluginConfig.Instance.EnableObjectPersistence.Value && !modelLocator.autoUpdateModelTransform)
                    {
                        modelLocator.autoUpdateModelTransform = true;
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
                    StateManagement.DisableMovementColliders(passengerObject, localDisabledStates);
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
                bool isAlreadyTrackedByThisController = false;
                foreach (var trackedObj in list)
                {
                    if (trackedObj != null && trackedObj.GetInstanceID() == passengerInstanceId)
                    {
                        isAlreadyTrackedByThisController = true;
                        break;
                    }
                }

                // When UncapCapacity is enabled AND EnableBalance is true, check mass capacity instead of slot capacity
                if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapCapacity.Value && !isAlreadyTrackedByThisController)
                {
                    // Calculate total mass including the incoming passenger
                    float totalMass = BagCapacityCalculator.CalculateTotalBagMass(__instance!, passengerObject);

                    // Calculate mass capacity with overencumbrance limit
                    float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(__instance!);
                    float overencumbranceMultiplier = Constants.Multipliers.DefaultMassMultiplier + (PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor);
                    float maxMassCapacity = massCapacity * overencumbranceMultiplier;

                    // Check if we would exceed mass capacity
                    if (totalMass > maxMassCapacity)
                    {

                        return false;
                    }
                }

                if (!isAlreadyTrackedByThisController && objectsInBag >= effectiveCapacity)
                {
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

                if (TryAssignToAdditionalSeat(__instance!, passengerObject, effectiveCapacity, isAlreadyTrackedByThisController))
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
                    if (OtherPatches.IsInProjectileState(passengerObject))
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
                        SetMainSeatObject(__instance, passengerObject);
                        var seatDict = GetState(__instance).AdditionalSeats;
                        if (seatDict != null)
                        {
                            System.Collections.Generic.CollectionExtensions.Remove(seatDict, passengerObject, out _);
                        }
                    }

                    var list = GetState(__instance).BaggedObjects;
                    bool alreadyTracked = false;
                    int passengerInstanceId = passengerObject.GetInstanceID();
                    foreach (var existingObj in list)
                    {
                        if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                        {
                            alreadyTracked = true;
                            break;
                        }
                    }
                    if (!alreadyTracked)
                    {
                        list.Add(passengerObject);

                        // Initialize state storage on first grab.
                        if (PluginConfig.Instance.EnableBalance.Value)
                        {
                            var newState = new BaggedObjectStateData();
                            newState.CalculateFromObject(passengerObject, __instance);
                            BaggedObjectPatches.SaveObjectState(__instance, passengerObject, newState);

                        }
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

                 // Attach breakout timer if network active
                 if (NetworkServer.active && !passengerObject.GetComponent<AdditionalSeatBreakoutTimer>())
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
                 }

                 seatDict[passengerObject] = newSeat;

                 if (NetworkServer.active)
                 {
                     newSeat.AssignPassenger(passengerObject);
                 }

                 if (!list.Any(o => o != null && o.GetInstanceID() == passengerInstanceId))
                 {
                     list.Add(passengerObject);
                 }

                 if (NetworkServer.active)
                 {
                     PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                 }

                  // Update carousel (intermediate state).
                 BagCarouselUpdater.UpdateCarousel(__instance);

                  // Update carousel again (final state after mass cap).
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

                 if (!list.Any(o => o != null && o.GetInstanceID() == passengerInstanceId))
                 {
                     list.Add(passengerObject);
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
            foreach (var kvp in seatDict.ToList())
            {
                if (kvp.Value == __instance)
                {
                    System.Collections.Generic.CollectionExtensions.Remove(seatDict, kvp.Key, out _);
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
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
