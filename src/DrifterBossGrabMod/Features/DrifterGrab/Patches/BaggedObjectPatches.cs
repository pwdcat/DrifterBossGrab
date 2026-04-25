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
    public static class BaggedObjectPatches
    {
        private static readonly HashSet<GameObject> _suppressedExitObjects = new HashSet<GameObject>();

        public static void SuppressExitForObject(GameObject obj)
        {
            if (obj == null) return;
            lock (_suppressedExitObjects)
            {
                _suppressedExitObjects.Add(obj);
            }
            // Reset after 2 seconds to be safe
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(ResetSuppressionForObject(obj, 2f));
            }
        }
        public static bool IsObjectExitSuppressed(GameObject obj)
        {
            if (obj == null) return false;
            lock (_suppressedExitObjects)
            {
                return _suppressedExitObjects.Contains(obj);
            }
        }
        private static System.Collections.IEnumerator ResetSuppressionForObject(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            lock (_suppressedExitObjects)
            {
                _suppressedExitObjects.Remove(obj);
            }
        }
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[SynchronizeBaggedObjectState] Called with targetObject={targetObject?.name ?? "null"}, EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}, hasAuthority={bagController.hasAuthority}");
            }
            BaggedObject? baggedObject = null;
            if (targetObject != null)
            {
                baggedObject = FindOrCreateBaggedObjectState(bagController, targetObject);
                if (PluginConfig.Instance.EnableDebugLogs.Value && baggedObject == null)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] FindOrCreateBaggedObjectState returned null for {targetObject.name}");
                }
                if (baggedObject != null)
                {
                    // Set the target immediately to ensure it's available when the state machine transitions
                    baggedObject.targetObject = targetObject;
                    UpdateTargetFields(baggedObject);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[SynchronizeBaggedObjectState] Set targetObject and called UpdateTargetFields for {targetObject.name}");
                    }
                }
            }

            // 1. Update network state (for multiplayer)
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {targetObject?.name ?? "null"}");
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
                // Check if we need to update to avoid redundant calls
                if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                {
                    var currentBaggedObj = bagController.baggedObject;
                    if (currentBaggedObj != targetObject)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[SynchronizeBaggedObjectState] Calling OnSyncBaggedObject for {targetObject?.name ?? "null"}");
                        // Use cached reflection to call private OnSyncBaggedObject
                        _onSyncBaggedObjectMethod?.Invoke(bagController, new object[] { targetObject! });
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] SKIPPED OnSyncBaggedObject - during passenger swap");
                }
            }

            // 2. Apply skill overrides (not handled by VehicleSeat.OnPassengerEnter())
            if (baggedObject != null && targetObject != null)
            {
                var baggedList = BagPatches.GetState(bagController).BaggedObjects;
                bool isInBag = baggedList != null && baggedList.Contains(targetObject);
                bool isProjectile = ProjectileRecoveryPatches.IsInProjectileState(targetObject);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[SynchronizeBaggedObjectState] Override check for {targetObject.name}: isInBag={isInBag}, isProjectile={isProjectile}");
                }

                if (isInBag && !isProjectile)
                {
                    var skillLocator = baggedObject.outer.GetComponent<SkillLocator>();
                    if (skillLocator != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[SynchronizeBaggedObjectState] Applying skill overrides for {targetObject.name}");
                        }
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[SynchronizeBaggedObjectState] SkillLocator is null for {targetObject.name}");
                        }
                    }
                }
            }

            // 3. Apply balance mode
            if (PluginConfig.Instance.EnableBalance.Value && targetObject != null)
            {
                var calculatedState = StateCalculator.CalculateState(
                    bagController,
                    targetObject,
                    PluginConfig.Instance.StateCalculationMode.Value);

                if (calculatedState != null)
                {
                    // Apply to BaggedObject state if it exists
                    if (baggedObject != null)
                    {
                        calculatedState.ApplyToBaggedObject(baggedObject);
                    }
                }
            }
        }
        public static void UpdateTargetFields(BaggedObject? instance)
        {
            if (instance == null || instance.targetObject == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[UpdateTargetFields] ENTRY: instance.targetObject={instance.targetObject.name}");
            }

            bool isBody = instance.targetObject.TryGetComponent<CharacterBody>(out var body);
            if (ReflectionCache.BaggedObject.IsBody != null)
            {
                ReflectionCache.BaggedObject.IsBody.SetValue(instance, isBody);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[UpdateTargetFields] Set isBody={isBody}");
                }
            }

            if (isBody && ReflectionCache.BaggedObject.TargetBody != null)
            {
                ReflectionCache.BaggedObject.TargetBody.SetValue(instance, body);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[UpdateTargetFields] Set targetBody={body?.name ?? "null"}");
                }
            }
            if (ReflectionCache.BaggedObject.VehiclePassengerAttributes != null)
            {
                instance.targetObject.TryGetComponent<SpecialObjectAttributes>(out var attributes);
                ReflectionCache.BaggedObject.VehiclePassengerAttributes.SetValue(instance, attributes);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[UpdateTargetFields] Set vehiclePassengerAttributes={(attributes != null ? "not null" : "null")}");
                }
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

            // When BagScaleCap is enabled
            if (PluginConfig.Instance.EnableBalance.Value)
            {
                bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                if (isScaleUncapped || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                {
                    if (controller != null)
                    {
                        BagPassengerManager.UpdateUncappedBagScale(controller, mass);
                    }
                }
            }
            else if (_setScaleMethod != null)
            {
                _setScaleMethod.Invoke(baggedObject, new object[] { bagScale01 });
            }
        }

        // Helper methods for per-object state storage
        public static void SaveObjectState(DrifterBagController controller, GameObject obj, BaggedObjectStateData state)
        {
            BaggedObjectStateStorage.SaveObjectState(controller, obj, state);
        }

        public static BaggedObjectStateData? LoadObjectState(DrifterBagController controller, GameObject obj)
        {
            return BaggedObjectStateStorage.LoadObjectState(controller, obj);
        }

        public static void CleanupObjectState(DrifterBagController controller, GameObject obj)
        {
            BaggedObjectStateStorage.CleanupObjectState(controller, obj);
        }

        // Remove the UI overlay for an object that has left the main seat.
        public static void RemoveUIOverlay(GameObject targetObject, DrifterBagController? bagController = null)
        {
            BaggedObjectUIPatches.RemoveUIOverlay(targetObject, bagController);
        }

        // Handle UI removal when cycling to null state (main seat becomes empty)
        public static void RemoveUIOverlayForNullState(DrifterBagController bagController)
        {
            BaggedObjectUIPatches.RemoveUIOverlayForNullState(bagController);
        }

        public static void RefreshUIOverlayForMainSeat(DrifterBagController? bagController, GameObject? targetObject)
        {
            BaggedObjectUIPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
        }

        // Helper method to check if an object is in the main seat
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
                var outerSeat = bagController.vehicleSeat;
                if (outerSeat == null)
                {
                    reason = "vehicle seat is null";
                }
                else
                {
                    var outerCurrentPassengerBodyObject = outerSeat.NetworkpassengerBodyObject;
                    result = outerCurrentPassengerBodyObject != null && ReferenceEquals(targetObject, outerCurrentPassengerBodyObject);
                    reason = $"physical seat match: {result}";

                    if (result && BagHelpers.GetAdditionalSeat(bagController, targetObject) != null)
                    {
                        result = false;
                        reason += " (but in additional seat)";
                    }
                }
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[IsInMainSeat] {targetObject.name}: result={result}, reason={reason}");
            }

            return result;
        }

        [HarmonyPatch(typeof(BaggedObject), "TryOverrideUtility")]
        public class BaggedObject_TryOverrideUtility
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                var bagController = __instance!.outer.GetComponent<DrifterBagController>();
                if (bagController == null) return true;
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);

                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                bool isBeingCycledToMain = trackedMain != null &&
                                         ReferenceEquals(trackedMain, targetObject);

                bool shouldAllowOverride = isMainSeatOccupant || isBeingCycledToMain;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObject_TryOverrideUtility.Prefix] targetObject={targetObject?.name ?? "null"}, " +
                            $"isMainSeatOccupant={isMainSeatOccupant}, " +
                            $"isBeingCycledToMain={isBeingCycledToMain}, " +
                            $"trackedMain={trackedMain?.name ?? "null"}, " +
                            $"shouldAllowOverride={shouldAllowOverride}.");
                }

                if (shouldAllowOverride)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BaggedObject_TryOverrideUtility.Prefix] ALLOWING override for {targetObject?.name ?? "null"}");
                    }
                    return true;
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BaggedObject_TryOverrideUtility.Prefix] SKIPPING override for {targetObject?.name ?? "null"} (not in main seat, not being cycled)");
                    }

                    if (trackedMain != null && skill && !skill.HasSkillOverrideOfPriority(GenericSkill.SkillOverridePriority.Contextual))
                    {
                        var utilityOverride = (SkillDef?)ReflectionCache.BaggedObject.UtilityOverride?.GetValue(__instance);
                        if (utilityOverride != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[BaggedObject_TryOverrideUtility.Prefix] RE-APPLYING utility override for main seat object {trackedMain.name} (override was cleaned up by vanilla OnExit)");
                            }
                            ReflectionCache.BaggedObject.OverriddenUtility?.SetValue(__instance, skill);
                            skill.SetSkillOverride(__instance, utilityOverride, GenericSkill.SkillOverridePriority.Contextual);
                            var skillLocator = __instance.outer.GetComponent<SkillLocator>();
                            if (skillLocator?.utility != null)
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
                try
                {
                    var targetObj = __instance?.targetObject;
                    var isBodyVal = ReflectionCache.BaggedObject.IsBody?.GetValue(__instance);
                    bool isBody = isBodyVal is bool b && b;
                    var overriddenUtility = ReflectionCache.BaggedObject.OverriddenUtility?.GetValue(__instance);
                    var utilityOverride = ReflectionCache.BaggedObject.UtilityOverride?.GetValue(__instance);
                    var vehiclePassengerAttributes = ReflectionCache.BaggedObject.VehiclePassengerAttributes?.GetValue(__instance);
                    var dbc = ReflectionCache.BaggedObject.DrifterBagController?.GetValue(__instance);

                    Log.Info($"[BaggedObject_TryOverrideUtility.Postfix] targetObject={targetObj?.name ?? "null"}, " +
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
        // Patch TryOverridePrimary to only allow skill overrides for main vehicle seat objects
        [HarmonyPatch(typeof(BaggedObject), "TryOverridePrimary")]
        public class BaggedObject_TryOverridePrimary
        {
            [HarmonyPrefix]
            public static bool Prefix(BaggedObject __instance, GenericSkill skill)
            {
                // Get the DrifterBagController
                var bagController = __instance.outer.GetComponent<DrifterBagController>();
                if (bagController == null) return true;
                var targetObject = __instance.targetObject;
                bool isMainSeatOccupant = IsInMainSeat(bagController, targetObject);

                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                bool isBeingCycledToMain = trackedMain != null &&
                                         ReferenceEquals(trackedMain, targetObject);

                bool shouldAllowOverride = isMainSeatOccupant || isBeingCycledToMain;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObject_TryOverridePrimary.Prefix] targetObject={targetObject?.name ?? "null"}, " +
                            $"isMainSeatOccupant={isMainSeatOccupant}, " +
                            $"isBeingCycledToMain={isBeingCycledToMain}, " +
                            $"trackedMain={trackedMain?.name ?? "null"}, " +
                            $"shouldAllowOverride={shouldAllowOverride}.");
                }

                if (shouldAllowOverride)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BaggedObject_TryOverridePrimary.Prefix] ALLOWING override for {targetObject?.name ?? "null"}");
                    }
                    return true; // Allow normal execution
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BaggedObject_TryOverridePrimary.Prefix] SKIPPING override for {targetObject?.name ?? "null"} (not in main seat, not being cycled)");
                    }

                    if (trackedMain != null && skill && !skill.HasSkillOverrideOfPriority(GenericSkill.SkillOverridePriority.Contextual))
                    {
                        var primaryOverride = (SkillDef?)ReflectionCache.BaggedObject.PrimaryOverride?.GetValue(__instance);
                        if (primaryOverride != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[BaggedObject_TryOverridePrimary.Prefix] RE-APPLYING primary override for main seat object {trackedMain.name} (override was cleaned up by vanilla OnExit)");
                            }
                            ReflectionCache.BaggedObject.OverriddenPrimary?.SetValue(__instance, skill);
                            skill.SetSkillOverride(__instance, primaryOverride, GenericSkill.SkillOverridePriority.Contextual);
                            var skillLocator = __instance.outer.GetComponent<SkillLocator>();
                            if (skillLocator?.primary != null)
                            {
                                skill.stock = skillLocator.primary.stock;
                            }
                        }
                    }

                    return false; // Skip vanilla — don't set overrides for non-main-seat objects
                }
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance, GenericSkill skill)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
                try
                {
                    var targetObj = __instance?.targetObject;
                    var isBodyVal = ReflectionCache.BaggedObject.IsBody?.GetValue(__instance);
                    bool isBody = isBodyVal is bool b && b;
                    var overriddenPrimary = ReflectionCache.BaggedObject.OverriddenPrimary?.GetValue(__instance);
                    var primaryOverride = ReflectionCache.BaggedObject.PrimaryOverride?.GetValue(__instance);
                    var vehiclePassengerAttributes = ReflectionCache.BaggedObject.VehiclePassengerAttributes?.GetValue(__instance);

                    Log.Info($"[BaggedObject_TryOverridePrimary.Postfix] targetObject={targetObj?.name ?? "null"}, " +
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[FindOrCreateBaggedObjectState] Called with targetObject={(targetObject != null ? targetObject.name : "null")}, NetworkServer.active={NetworkServer.active}");
            }

            var bagStateMachine = EntityStateMachine.FindByCustomName(bagController.gameObject, "Bag");
            if (bagStateMachine != null && bagStateMachine.state is BaggedObject bo && bo.targetObject == targetObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[FindOrCreateBaggedObjectState] Found existing BaggedObject state for {(targetObject != null ? targetObject.name : "null")}");
                }
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
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Debug($"[FindOrCreateBaggedObjectState] Skipping - bag full ({currentCount}/{effectiveCapacity}) for {(targetObject != null ? targetObject.name : "null")}");
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[FindOrCreateBaggedObjectState] Creating NEW BaggedObject with targetObject={(targetObject != null ? targetObject.name : "null")}, drifterBagController={(bagCtrl != null ? bagCtrl.name : "null")}");
                        }
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

        public static void HandlePassengerExit(RoR2.VehicleSeat seat, GameObject passenger)
        {
            if (seat == null || passenger == null) return;
            var bagController = seat.GetComponent<DrifterBagController>();
            if (bagController == null) return;
            if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

            var mainSeatObject = BagPatches.GetMainSeatObject(bagController);
            bool isTrackedAsMainSeat = mainSeatObject != null && ReferenceEquals(mainSeatObject, passenger);

            var baggedObjectsList = BagPatches.GetState(bagController).BaggedObjects;
            bool isInBaggedObjects = baggedObjectsList != null && baggedObjectsList!.Contains(passenger);

            // Check if the passenger was reassigned to another seat
            bool isInMainSeat = bagController.vehicleSeat != null &&
                                bagController.vehicleSeat.hasPassenger &&
                                ReferenceEquals(bagController.vehicleSeat.NetworkpassengerBodyObject, passenger);

            // Get the additional seat currently assigned to this object in our tracking dictionary
            var currentAssignedSeat = BagHelpers.GetAdditionalSeat(bagController, passenger);
            bool isInAdditionalSeat = currentAssignedSeat != null &&
                                      currentAssignedSeat.hasPassenger &&
                                      ReferenceEquals(currentAssignedSeat.NetworkpassengerBodyObject, passenger);

            if (isInMainSeat || isInAdditionalSeat)
            {
                return;
            }

            if ((isTrackedAsMainSeat || isInBaggedObjects) && !IsPassengerDeadOrDestroyed(passenger))
            {
                if (isTrackedAsMainSeat) BagPatches.SetMainSeatObject(bagController, null);
                if (isInBaggedObjects && baggedObjectsList != null)
                {
                    baggedObjectsList.Remove(passenger);
                    BagPatches.GetState(bagController).RemoveInstanceId(passenger.GetInstanceID());
                }

                BagCarouselUpdater.UpdateCarousel(bagController);
                BagCarouselUpdater.UpdateNetworkBagState(bagController);
                BagPassengerManager.ForceRecalculateMass(bagController);
                RemoveUIOverlay(passenger, bagController);

                // Clean up initialization tracking when passenger truly exits
                BaggedObjectStatePatches.BaggedObject_OnExit.ClearObjectSuccessfullyInitialized(passenger);
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
