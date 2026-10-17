#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using RoR2.HudOverlay;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates;
using EntityStates.Drifter.Bag;
using EntityStateMachine = RoR2.EntityStateMachine;
using DrifterBossGrabMod.Features;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{

    // ========================================================================================
    // BAGGED OBJECT PATCHES
    // ========================================================================================
    public static class BaggedObjectPatches
    {

        // ========================================================================================
        // STATE SYNCHRONIZATION
        // ========================================================================================
        private static readonly MethodInfo _onSyncBaggedObjectMethod = ReflectionCache.DrifterBagController.OnSyncBaggedObject;
        private static readonly MethodInfo _tryOverrideUtilityMethod = ReflectionCache.BaggedObject.TryOverrideUtility;
        private static readonly MethodInfo _tryOverridePrimaryMethod = ReflectionCache.BaggedObject.TryOverridePrimary;
        private static readonly FieldInfo _bagScale01Field = ReflectionCache.BaggedObject.BagScale01;
        private static readonly MethodInfo _setScaleMethod = ReflectionCache.BaggedObject.SetScale;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DrifterBagController, BaggedObject> _initializingBagStates = new();

        public static BaggedObject? FindExistingBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null || targetObject == null) return null;

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo)
            {
                return bo;
            }
            return null;
        }

        public static void SynchronizeBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null) return;
            if (_initializingBagStates.TryGetValue(bagController, out _)) return;

            BaggedObject? baggedObject = null;
            if (targetObject != null)
            {
                baggedObject = FindOrCreateBaggedObjectState(bagController, targetObject);
                if (baggedObject == null)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] FindOrCreateBaggedObjectState returned null for {targetObject.name}");
                }
                if (baggedObject != null)
                {

                    baggedObject.targetObject = targetObject;
                    UpdateTargetFields(baggedObject);
                    Log.Debug($"[SynchronizeBaggedObjectState] Set targetObject and called UpdateTargetFields for {targetObject.name}");
                }
            }

            if (NetworkServer.active)
            {
                if (bagController.NetworkbaggedObject != targetObject)
                {
                    bagController.NetworkbaggedObject = targetObject;
                }

                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    var currentBaggedObj = bagController.baggedObject;
                    if (currentBaggedObj != targetObject)
                    {
                        _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
                }
            }
            else if (bagController.hasAuthority)
            {

                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    var currentBaggedObj = bagController.baggedObject;
                    if (currentBaggedObj != targetObject)
                    {

                        _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
                }
            }

            if (baggedObject != null && targetObject != null)
            {
                var baggedList = BagPatches.GetState(bagController).BaggedObjects;
                bool isInBag = baggedList != null && baggedList.Contains(targetObject);
                bool isProjectile = ProjectileRecoveryPatches.IsInProjectileState(targetObject);

                Log.Debug($"[SynchronizeBaggedObjectState] Override check for {targetObject.name}: isInBag={isInBag}, isProjectile={isProjectile}");

                if (isInBag && !isProjectile)
                {
                    PersistenceObjectsTracker.SetBaggedObjectVisibility(targetObject, false);
                    var skillLocator = baggedObject.outer.GetComponent<SkillLocator>();
                    if (skillLocator != null)
                    {
                        Log.Debug($"[SynchronizeBaggedObjectState] Applying skill overrides for {targetObject.name}");
                        if (skillLocator.utility != null)
                        {
                            _tryOverrideUtilityMethod?.Invoke(baggedObject, new object[] { skillLocator.utility });
                        }
                        if (skillLocator.primary != null)
                        {
                            _tryOverridePrimaryMethod?.Invoke(baggedObject, new object[] { skillLocator.primary });
                        }
                    }
                    else
                    {
                        Log.Debug($"[SynchronizeBaggedObjectState] SkillLocator is null for {targetObject.name}");
                    }
                }
            }

            if (PluginConfig.Instance.EnableBalance.Value && targetObject != null)
            {
                var calculatedState = StateCalculator.CalculateState(
                    bagController,
                    targetObject,
                    PluginConfig.Instance.StateCalculationMode.Value);

                if (calculatedState != null)
                {

                    if (baggedObject != null)
                    {
                        calculatedState.ApplyToBaggedObject(baggedObject);
                    }
                }
            }
        }

        // ========================================================================================
        // UTILITIES
        // ========================================================================================
        public static void UpdateTargetFields(BaggedObject? instance)
        {
            if (instance == null || instance.targetObject == null) return;

            bool isBody = instance.targetObject.TryGetComponent<CharacterBody>(out var body);
            if (ReflectionCache.BaggedObject.IsBody != null)
            {
                ReflectionCache.BaggedObject.IsBody.SetValue(instance, isBody);
            }

            if (isBody && ReflectionCache.BaggedObject.TargetBody != null)
            {
                ReflectionCache.BaggedObject.TargetBody.SetValue(instance, body);
            }
            if (ReflectionCache.BaggedObject.VehiclePassengerAttributes != null)
            {
                instance.targetObject.TryGetComponent<SpecialObjectAttributes>(out var attributes);
                ReflectionCache.BaggedObject.VehiclePassengerAttributes.SetValue(instance, attributes);
            }
        }

        public static void UpdateBagScale(BaggedObject baggedObject, float mass)
        {
            if (baggedObject == null) return;

            var controller = baggedObject.outer.GetComponent<DrifterBagController>();
            float bagScale01 = StateCalculator.CalculateBagScale01(controller, mass);

            if (_bagScale01Field != null)
            {
                _bagScale01Field.SetValue(baggedObject, bagScale01);
            }

            if (controller != null && (PluginConfig.Instance.EnableBalance.Value &&
                (PluginConfig.Instance.IsBagScaleCapInfinite || PluginConfig.Instance.ParsedBagScaleCap > 1f) ||
                controller.GetComponent<UncappedBagScaleComponent>() != null))
                BagPassengerManager.UpdateUncappedBagScale(controller, mass);

            if (_setScaleMethod != null)
                _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });

        }

        // ========================================================================================
        // STATE STORAGE
        // ========================================================================================
        public static void SaveObjectState(DrifterBagController controller, GameObject obj, BaggedObjectStateData state)
        {
            BaggedObjectStateStorage.SaveObjectState(controller, obj, state);
        }

        public static BaggedObjectStateData? LoadObjectState(DrifterBagController controller, GameObject obj)
        {
            return BaggedObjectStateStorage.LoadObjectState(controller, obj);
        }

        public static BaggedObjectStateData? FindStateForObject(GameObject obj)
        {
            return BaggedObjectStateStorage.FindStateForObject(obj);
        }

        public static void CleanupObjectState(DrifterBagController controller, GameObject obj, bool preserveForThrow = false)
        {
            BaggedObjectStateStorage.CleanupObjectState(controller, obj, preserveForThrow);
        }

        public static void PreserveStateForThrow(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.PreserveStateForThrow(controller, obj);
        }

        public static void RestorePreservedState(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.RestorePreservedState(controller, obj);
        }

        public static void ClearTemporaryPreservation(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.ClearTemporaryPreservation(controller, obj);
        }

        public static void ClearAllTemporaryPreservation(DrifterBagController controller)
        {
            BaggedObjectStateStorage.ClearAllTemporaryPreservation(controller);
        }

        // ========================================================================================
        // UI OVERLAY HELPERS
        // ========================================================================================
        public static void RemoveUIOverlay(GameObject targetObject, DrifterBagController? bagController = null)
        {
            BaggedObjectUIPatches.RemoveUIOverlay(targetObject, bagController);
        }

        public static void RemoveUIOverlayForNullState(DrifterBagController bagController)
        {
            BaggedObjectUIPatches.RemoveUIOverlayForNullState(bagController);
        }

        public static void RefreshUIOverlayForMainSeat(DrifterBagController? bagController, GameObject? targetObject)
        {
            BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
        }

        private static bool IsInMainSeat(DrifterBagController bagController, GameObject targetObject)
        {
            if (bagController == null || targetObject == null) return false;

            var trackedMainSeat = BagPatches.GetMainSeatObject(bagController);
            bool result = false;
            string reason = "";

            if (trackedMainSeat != null)
            {
                result = ReferenceEquals(targetObject, trackedMainSeat);
                reason = $"tracked main seat match: {result}";
            }
            else
            {
                result = false;
                reason = "tracked main seat is null";
            }

            Log.Debug($"[IsInMainSeat] {(targetObject ? targetObject.name : "null")}: result={result}, reason={reason}");

            return result;
        }

        // ========================================================================================
        // HARMONY PATCHES
        // ========================================================================================
        [HarmonyPatch(typeof(BaggedObject), "TryOverrideUtility")]
        public class BaggedObject_TryOverrideUtility
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                if (__instance == null || !__instance.outer) return true;
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null) return true;
                var targetObject = __instance.targetObject;
                if (targetObject == null) return false;

                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);

                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                bool isBeingCycledToMain = trackedMain != null &&
                                         ReferenceEquals(trackedMain, targetObject);

                bool shouldAllowOverride = isMainSeatOccupant || isBeingCycledToMain;

                if (shouldAllowOverride)
                {
                    return true;
                }
                else
                {

                    if (trackedMain != null && skill != null && !skill.HasSkillOverrideOfPriority(GenericSkill.SkillOverridePriority.Contextual))
                    {
                        var utilityOverride = (SkillDef?)ReflectionCache.BaggedObject.UtilityOverride?.GetValue(__instance);
                        if (utilityOverride != null)
                        {
                            ReflectionCache.BaggedObject.OverriddenUtility?.SetValue(__instance, skill);
                            skill.SetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                            var skillLocator = __instance.outer.GetComponent<SkillLocator>();
                            if (skillLocator != null && skillLocator.utility != null)
                            {
                                skill.stock = skillLocator.utility.stock;
                            }
                        }
                    }

                    return false;
                }
            }

        }

        [HarmonyPatch(typeof(BaggedObject), "TryOverridePrimary")]
        public class BaggedObject_TryOverridePrimary
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                if (__instance == null || !__instance.outer) return true;

                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null) return true;
                var targetObject = __instance.targetObject;
                if (targetObject == null) return false;

                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);

                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                bool isBeingCycledToMain = trackedMain != null &&
                                         ReferenceEquals(trackedMain, targetObject);

                bool shouldAllowOverride = isMainSeatOccupant || isBeingCycledToMain;

                if (shouldAllowOverride)
                {
                    return true;
                }
                else
                {

                    if (trackedMain != null && skill != null && !skill.HasSkillOverrideOfPriority(GenericSkill.SkillOverridePriority.Contextual))
                    {
                        var primaryOverride = (SkillDef?)ReflectionCache.BaggedObject.PrimaryOverride?.GetValue(__instance);
                        if (primaryOverride != null)
                        {
                            ReflectionCache.BaggedObject.OverriddenPrimary?.SetValue(__instance, skill);
                            skill.SetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                            var skillLocator = __instance.outer.GetComponent<SkillLocator>();
                            if (skillLocator != null && skillLocator.primary != null)
                            {
                                skill.stock = skillLocator.primary.stock;
                            }
                        }
                    }

                    return false;
                }
            }

        }

        public static GameObject? GetMainSeatOccupant(DrifterBagController controller)
        {
            if (controller == null || controller.vehicleSeat == null) return null;
            if (!controller.vehicleSeat.hasPassenger) return null;
            return controller.vehicleSeat.currentPassengerBody != null ? controller.vehicleSeat.currentPassengerBody.gameObject : null;
        }

        public static BaggedObject? FindOrCreateBaggedObjectState(DrifterBagController bagController, GameObject? targetObject)
        {
            if (bagController == null || targetObject == null) return null;
            if (_initializingBagStates.TryGetValue(bagController, out var initializingState))
                return initializingState.targetObject == targetObject ? initializingState : null;

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo && bo.targetObject == targetObject)
            {
                bool needsInitialization = NetworkServer.active && PluginConfig.Instance.BottomlessBagEnabled.Value &&
                    bagController.vehicleSeat != null && bagController.vehicleSeat.NetworkpassengerBodyObject == targetObject &&
                    !BaggedObjectStatePatches.HasInitializedState(bo);
                if (!needsInitialization)
                {
                    Log.Debug($"[FindOrCreateBaggedObjectState] Found existing BaggedObject state for {(targetObject ? targetObject.name : "null")}");
                    return bo;
                }
                Log.Debug($"[FindOrCreateBaggedObjectState] Replacing uninitialized main state for {targetObject.name}");
                BaggedObjectStatePatches.UnsetAllOverrides(bo);
            }

            try
            {
                var targetStateMachine = bagStateMachine;
                if (targetStateMachine == null)
                {
                    targetStateMachine = bagController.gameObject.AddComponent<EntityStateMachine>();
                    targetStateMachine.customName = "Bag";
                }

                if (targetStateMachine != null)
                {
                    var baggedList = BagPatches.GetState(bagController).BaggedObjects;
                    bool isTracked = baggedList != null && targetObject != null && baggedList.Contains(targetObject);
                    if (!isTracked)
                    {
                        int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController, targetObject);
                        int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                        if (currentCount >= effectiveCapacity)
                        {
                            Log.Debug($"[FindOrCreateBaggedObjectState] Skipping - bag full ({currentCount}/{effectiveCapacity}) for {(!targetObject ? "null" : targetObject!.name)}");
                            return null;
                        }
                    }

                    var constructor = typeof(BaggedObject).GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        var newBaggedObject = (BaggedObject)constructor.Invoke(null);
                        newBaggedObject.targetObject = targetObject;
                        var bagCtrl = bagStateMachine != null ? bagStateMachine.GetComponent<DrifterBagController>() : bagController.gameObject.GetComponent<DrifterBagController>();
                        if (bagCtrl != null)
                        {
                            ReflectionCache.BaggedObject.DrifterBagController?.SetValue(newBaggedObject, bagCtrl);
                        }
                        Log.Debug($"[FindOrCreateBaggedObjectState] Creating NEW BaggedObject with targetObject={(!targetObject ? "null" : targetObject!.name)}, drifterBagController={(!bagCtrl ? "null" : bagCtrl!.name)}");
                        _initializingBagStates.Add(bagController, newBaggedObject);
                        try
                        {
                            targetStateMachine.SetState(newBaggedObject);
                        }
                        finally
                        {
                            _initializingBagStates.Remove(bagController);
                        }
                        return newBaggedObject;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[FindOrCreateBaggedObjectState] Error: {ex}");
            }
            return null;
        }

        // ========================================================================================
        // PASSENGER EXIT HANDLING
        // ========================================================================================
        public static void HandlePassengerExit(RoR2.VehicleSeat seat, GameObject passenger)
        {
            if (seat == null || passenger == null) return;

            if (!UnityEngine.Networking.NetworkServer.active)
            {
                if (DrifterBossGrabPlugin.IsSwappingPassengers) return;
                var bagControllerClient = seat.GetComponentInParent<DrifterBagController>();
                var bagStateClient = (bagControllerClient != null) ? BagPatches.GetState(bagControllerClient) : null;
                var incomingObjClient = bagStateClient?.IncomingObject;
                if (incomingObjClient != null && ReferenceEquals(passenger, incomingObjClient)) return;

                bool isInBagList = bagStateClient != null && bagStateClient.BaggedObjects != null && bagStateClient.BaggedObjects.Contains(passenger);
                bool isTrackedMain = bagStateClient != null && ReferenceEquals(bagStateClient.MainSeatObject, passenger);
                bool isInAdditional = bagControllerClient != null && BagHelpers.GetAdditionalSeat(bagControllerClient, passenger) != null;
                bool isTracked = PersistenceObjectsTracker.IsObjectCurrentlyBagged(passenger);

                if (isInBagList || isTrackedMain || isInAdditional || isTracked)
                {
                    return;
                }

                PersistenceObjectsTracker.UntrackBaggedObject(passenger, false);
                BaggedObjectStatePatches.PerformPassengerRestoration(bagControllerClient, passenger);
                return;
            }

            if (Networking.NetworkUtils.IsNetworkIdentityInactive(passenger))
            {
                Log.Warning($"[HandlePassengerExit] Suppressing spurious exit for {passenger.name} - NetworkIdentity is inactive (likely deactivation bug)");
                Networking.NetworkUtils.TryEnsureNetworkIdentityActive(passenger);
                return;
            }

            var bagController = seat.GetComponentInParent<DrifterBagController>();
            if (bagController == null) return;
            if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

            var incomingObject = BagPatches.GetState(bagController).IncomingObject;
            if (incomingObject != null && ReferenceEquals(passenger, incomingObject))
            {
                Log.Debug($"[HandlePassengerExit] Skipping RemoveBaggedObject for {passenger.name} - same as IncomingObject (reassignment, not true exit)");
                return;
            }

            var baggedObjectsList = BagPatches.GetState(bagController).BaggedObjects;
            bool isInBaggedObjects = baggedObjectsList != null && baggedObjectsList.Contains(passenger);

            if (isInBaggedObjects)
            {
                Log.Debug($"[HandlePassengerExit] Passenger {passenger.name} exited seat {seat.name}. Triggering full RemoveBaggedObject.");

                RemoveUIOverlay(passenger, bagController);
                BagPassengerManager.RemoveBaggedObject(bagController, passenger);
            }
        }

        public static bool IsPassengerDeadOrDestroyed(GameObject passenger)
        {
            if (passenger == null) return true;
            var healthComponent = passenger.GetComponent<HealthComponent>();
            if (healthComponent != null && !healthComponent.alive) return true;
            if (passenger.GetComponent<SpecialObjectAttributes>()?.durability <= 0) return true;
            return false;
        }
    }
}
