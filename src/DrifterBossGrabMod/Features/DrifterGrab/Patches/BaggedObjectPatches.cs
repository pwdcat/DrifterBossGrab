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

            Log.Debug($"[SynchronizeBaggedObjectState] Called with targetObject={(!targetObject ? "null" : targetObject!.name)}, EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}, hasAuthority={bagController.hasAuthority}");
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
                        Log.Debug($"[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {(!targetObject ? "null" : targetObject!.name)}");
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
                        Log.Debug($"[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {(!targetObject ? "null" : targetObject!.name)}");

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

            Log.Debug($"[UpdateTargetFields] ENTRY: instance.targetObject={instance.targetObject.name}");

            bool isBody = instance.targetObject.TryGetComponent<CharacterBody>(out var body);
            if (ReflectionCache.BaggedObject.IsBody != null)
            {
                ReflectionCache.BaggedObject.IsBody.SetValue(instance, isBody);
                Log.Debug($"[UpdateTargetFields] Set isBody={isBody}");
            }

            if (isBody && ReflectionCache.BaggedObject.TargetBody != null)
            {
                ReflectionCache.BaggedObject.TargetBody.SetValue(instance, body);
                Log.Debug($"[UpdateTargetFields] Set targetBody={(!body ? "null" : body!.name)}");
            }
            if (ReflectionCache.BaggedObject.VehiclePassengerAttributes != null)
            {
                instance.targetObject.TryGetComponent<SpecialObjectAttributes>(out var attributes);
                ReflectionCache.BaggedObject.VehiclePassengerAttributes.SetValue(instance, attributes);
                Log.Debug($"[UpdateTargetFields] Set vehiclePassengerAttributes={(attributes != null ? "not null" : "null")}");
            }
        }

        public static void UpdateBagScale(BaggedObject baggedObject, float mass)
        {
            if (baggedObject == null) return;

            float maxCapacity = DrifterBagController.maxMass;
            var controller = baggedObject.outer.GetComponent<DrifterBagController>();
            if (controller != null)
            {
                maxCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
            }

            float value = mass;
            if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.IsBagScaleCapInfinite)
            {
                value = Mathf.Clamp(mass, 1f, maxCapacity);
            }
            else
            {
                value = Mathf.Max(mass, 1f);
            }

            float t = (value - 1f) / (maxCapacity - 1f);
            float bagScale01 = 0.5f + 0.5f * t;

            if (_bagScale01Field != null)
            {
                _bagScale01Field.SetValue(baggedObject, bagScale01);
            }

            if (PluginConfig.Instance.EnableBalance.Value)
            {
                bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                if (isScaleUncapped || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                {
                    if (controller != null)
                    {
                        BagPassengerManager.UpdateUncappedBagScale(controller, mass);
                    }

                    bool uncappedActivelyScaling = mass > maxCapacity;
                    if (!uncappedActivelyScaling && _setScaleMethod != null)
                    {
                        _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
                    }
                }
                else if (_setScaleMethod != null)
                {
                    _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
                }
            }
            else if (_setScaleMethod != null)
            {
                _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
            }
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

                Log.Debug($"[BaggedObject_TryOverrideUtility.Prefix] targetObject={(!targetObject ? "null" : targetObject!.name)}, " +
                        $"isMainSeatOccupant={isMainSeatOccupant}, " +
                        $"isBeingCycledToMain={isBeingCycledToMain}, " +
                        $"trackedMain={(!trackedMain ? "null" : trackedMain!.name)}, " +
                        $"shouldAllowOverride={shouldAllowOverride}.");

                if (shouldAllowOverride)
                {
                    Log.Debug($"[BaggedObject_TryOverrideUtility.Prefix] ALLOWING override for {(!targetObject ? "null" : targetObject!.name)}");
                    return true;
                }
                else
                {
                    Log.Debug($"[BaggedObject_TryOverrideUtility.Prefix] SKIPPING override for {(!targetObject ? "null" : targetObject!.name)} (not in main seat, not being cycled)");

                    if (trackedMain != null && skill != null && !skill.HasSkillOverrideOfPriority(GenericSkill.SkillOverridePriority.Contextual))
                    {
                        var utilityOverride = (SkillDef?)ReflectionCache.BaggedObject.UtilityOverride?.GetValue(__instance);
                        if (utilityOverride != null)
                        {
                            Log.Debug($"[BaggedObject_TryOverrideUtility.Prefix] RE-APPLYING utility override for main seat object {trackedMain.name} (override was cleaned up by vanilla OnExit)");
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

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, GenericSkill skill)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
                if (__instance == null) return;
                try
                {
                    var targetObj = __instance?.targetObject;
                    var isBodyVal = ReflectionCache.BaggedObject.IsBody?.GetValue(__instance);
                    bool isBody = isBodyVal is bool b && b;
                    var overriddenUtility = ReflectionCache.BaggedObject.OverriddenUtility?.GetValue(__instance);
                    var utilityOverride = ReflectionCache.BaggedObject.UtilityOverride?.GetValue(__instance);
                    var vehiclePassengerAttributes = ReflectionCache.BaggedObject.VehiclePassengerAttributes?.GetValue(__instance);
                    var dbc = ReflectionCache.BaggedObject.DrifterBagController?.GetValue(__instance);

                    Log.Debug($"[BaggedObject_TryOverrideUtility.Postfix] targetObject={(!targetObj ? "null" : targetObj!.name)}, " +
                            $"isBody={isBody}, " +
                            $"vehiclePassengerAttributes={(vehiclePassengerAttributes != null ? "SET" : "NULL")}, " +
                            $"drifterBagController={(dbc != null ? "SET" : "NULL")}, " +
                            $"overriddenUtility={(overriddenUtility != null ? "SET" : "NULL")}, " +
                            $"utilityOverride={(utilityOverride != null ? ((UnityEngine.ScriptableObject)utilityOverride).name : "NULL")}, " +
                            $"skill={(skill != null ? skill.skillName : "null")}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[BaggedObject_TryOverrideUtility.Postfix] Diagnostic error: {ex.Message}");
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

                Log.Debug($"[BaggedObject_TryOverridePrimary.Prefix] targetObject={(!targetObject ? "null" : targetObject!.name)}, " +
                        $"isMainSeatOccupant={isMainSeatOccupant}, " +
                        $"isBeingCycledToMain={isBeingCycledToMain}, " +
                        $"trackedMain={(!trackedMain ? "null" : trackedMain!.name)}, " +
                        $"shouldAllowOverride={shouldAllowOverride}.");

                if (shouldAllowOverride)
                {
                    Log.Debug($"[BaggedObject_TryOverridePrimary.Prefix] ALLOWING override for {(!targetObject ? "null" : targetObject!.name)}");
                    return true;
                }
                else
                {
                    Log.Debug($"[BaggedObject_TryOverridePrimary.Prefix] SKIPPING override for {(!targetObject ? "null" : targetObject!.name)} (not in main seat, not being cycled)");

                    if (trackedMain != null && skill != null && !skill.HasSkillOverrideOfPriority(GenericSkill.SkillOverridePriority.Contextual))
                    {
                        var primaryOverride = (SkillDef?)ReflectionCache.BaggedObject.PrimaryOverride?.GetValue(__instance);
                        if (primaryOverride != null)
                        {
                            Log.Debug($"[BaggedObject_TryOverridePrimary.Prefix] RE-APPLYING primary override for main seat object {trackedMain.name} (override was cleaned up by vanilla OnExit)");
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

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, GenericSkill skill)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
                if (__instance == null) return;
                try
                {
                    var targetObj = __instance?.targetObject;
                    var isBodyVal = ReflectionCache.BaggedObject.IsBody?.GetValue(__instance);
                    bool isBody = isBodyVal is bool b && b;
                    var overriddenPrimary = ReflectionCache.BaggedObject.OverriddenPrimary?.GetValue(__instance);
                    var primaryOverride = ReflectionCache.BaggedObject.PrimaryOverride?.GetValue(__instance);
                    var vehiclePassengerAttributes = ReflectionCache.BaggedObject.VehiclePassengerAttributes?.GetValue(__instance);

                    Log.Debug($"[BaggedObject_TryOverridePrimary.Postfix] targetObject={(!targetObj ? "null" : targetObj!.name)}, " +
                            $"isBody={isBody}, " +
                            $"vehiclePassengerAttributes={(vehiclePassengerAttributes != null ? "SET" : "NULL")}, " +
                            $"overriddenPrimary={(overriddenPrimary != null ? "SET" : "NULL")}, " +
                            $"primaryOverride={(primaryOverride != null ? ((UnityEngine.ScriptableObject)primaryOverride).name : "NULL")}, " +
                            $"skill={(skill != null ? skill.skillName : "null")}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[BaggedObject_TryOverridePrimary.Postfix] Diagnostic error: {ex.Message}");
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

            Log.Debug($"[FindOrCreateBaggedObjectState] Called with targetObject={(!targetObject ? "null" : targetObject!.name)}, NetworkServer.active={NetworkServer.active}");

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo && bo.targetObject == targetObject)
            {
                Log.Debug($"[FindOrCreateBaggedObjectState] Found existing BaggedObject state for {(targetObject ? targetObject.name : "null")}");
                return bo;
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
                        targetStateMachine.SetState(newBaggedObject);
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
            if (!UnityEngine.Networking.NetworkServer.active) return;

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
