#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Features;
using DrifterBossGrabMod.Balance;
using DrifterBossGrabMod.Networking;
using EntityStates;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Patches
{

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
            BagPatches.GetState(controller).MarkMassDirty();
        }

        public static void RemoveBaggedObject(DrifterBagController? controller, GameObject obj, bool isDestroying = false, bool skipStateReset = false, bool preserveStateDuringThrow = false)
        {
            if (controller == null || ReferenceEquals(obj, null)) return;

            int targetInstanceId;
            try
            {
                targetInstanceId = obj.GetInstanceID();
            }
            catch
            {
                targetInstanceId = -1;
            }

            if (DrifterBossGrabPlugin.IsSwappingPassengers)
            {
                return;
            }

            GameObject? mainPassengerBefore = BagPatches.GetMainSeatObject(controller);
            bool wasMainPassenger = (mainPassengerBefore != null && mainPassengerBefore == obj);

            if (mainPassengerBefore != null && mainPassengerBefore.GetInstanceID() == obj.GetInstanceID())
            {
                BagPatches.SetMainSeatObject(controller, null);
                wasMainPassenger = true;
            }

            var seatDict = BagPatches.GetState(controller).AdditionalSeats;
            VehicleSeat? ejectedAdditionalSeat = null;
            if (seatDict != null)
            {
                seatDict.TryRemove(obj, out ejectedAdditionalSeat);
                _removeKeysBuffer.Clear();
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && kvp.Value.NetworkpassengerBodyObject == obj)
                    {
                        _removeKeysBuffer.Add(kvp.Key);
                    }
                }
                foreach (var key in _removeKeysBuffer)
                {
                    seatDict.TryRemove(key, out _);
                }
            }

            bool isThrowing = ProjectileRecoveryPatches.IsInProjectileState(obj);

            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;

            if (list != null)
            {
                try
                {
                    var tracker = obj.GetComponent<BaggedObjectTracker>();
                    if (tracker != null)
                    {
                        tracker.isRemovingManual = true;

                        var esms = obj.GetComponents<EntityStateMachine>();
                        foreach (var esm in esms)
                        {
                            if (esm.customName == "Body")
                            {
                                BaggedObjectStatePatches.UnregisterTrackedESM(esm);
                                break;
                            }
                        }

                        UnityEngine.Object.Destroy(tracker);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RemoveBaggedObject] Error destroying tracker: {ex.Message}");
                }

                PersistenceObjectsTracker.UntrackBaggedObject(obj, isDestroying);

                list.RemoveAll(x => ReferenceEquals(x, null) || (x is UnityEngine.Object uo && !uo) || (targetInstanceId != -1 && x.GetInstanceID() == targetInstanceId));
                if (targetInstanceId != -1) BagPatches.GetState(controller).RemoveInstanceId(targetInstanceId);

                if (wasMainPassenger)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        var currentState = GetBagStateMachineState(controller);
                        Log.Debug($"[RemoveBaggedObject] Was main passenger destroyed. Current Bag state: {currentState}");
                    }

                    if (ReflectionCache.DrifterBagController.Smacks != null)
                    {
                        int currentSmacks = (int)ReflectionCache.DrifterBagController.Smacks.GetValue(controller);
                        var exitingState = BaggedObjectPatches.LoadObjectState(controller, obj);
                        if (exitingState != null)
                        {
                            exitingState.smacks = currentSmacks;
                            BaggedObjectPatches.SaveObjectState(controller, obj, exitingState);
                        }
                        ReflectionCache.DrifterBagController.Smacks.SetValue(controller, 0);
                    }

                    API.DrifterBagAPI.InvokeOnMainPassengerChanged(controller, mainPassengerBefore, null);

                    EjectMainPassengerIfServer(controller, obj, isDestroying);

                    BagPatches.SetMainSeatObject(controller, null);

                    if (AuthorityGuard.ShouldAutoPromote(controller) && list.Count > 0)
                    {
                        var newMain = list[0];
                        if (newMain != null && !ProjectileRecoveryPatches.IsInProjectileState(newMain))
                        {
                            Log.Debug($"[RemoveBaggedObject] Triggering autopromote for: {newMain.name}");

                            DelayedAutoPromote.Schedule(controller, newMain, 0.05f);
                        }
                    }
                }
            }

            if (isThrowing)
            {
                BagHelpers.CleanupEmptyAdditionalSeats(controller);
            }

            if (ejectedAdditionalSeat != null && NetworkServer.active && !isDestroying)
            {
                try
                {
                    ejectedAdditionalSeat.EjectPassenger(obj);
                        Log.Debug($"[RemoveBaggedObject] Ejected {obj.name} from additional seat");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RemoveBaggedObject] Error ejecting from additional seat: {ex.Message}");
                }
            }

            if (preserveStateDuringThrow)
            {
                if (controller != null && obj != null)
                {
                    BaggedObjectStateStorage.PreserveStateForThrow(controller, obj);
                }
            }
            else if (isDestroying || (isThrowing == false && !DrifterBossGrabPlugin.IsSwappingPassengers))
            {
                if (controller != null && obj != null)
                {
                    BaggedObjectPatches.CleanupObjectState(controller, obj);
                }

                if (obj != null) BaggedObjectStatePatches.BaggedObject_OnExit.ClearObjectSuccessfullyInitialized(obj);
            }

            if (wasMainPassenger && controller != null && obj != null)
            {
                BaggedObjectStatePatches.ForceCleanupOverrides(controller, obj);
            }

            if (obj != null)
            {
                var timer = obj.GetComponent<AdditionalSeatBreakoutTimer>();
                if (timer != null)
                {
                    UnityEngine.Object.Destroy(timer);
                }
            }

            if (AuthorityGuard.ShouldSendPersistence(controller) && list != null)
            {
                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, controller);
            }

            int direction = wasMainPassenger ? 1 : 0;
            if (controller != null)
            {

                IsProcessingThrowRemoval = isThrowing;

                BagCarouselUpdater.UpdateCarousel(controller, direction);
            }

            if (controller != null)
            {
                BagCarouselUpdater.UpdateNetworkBagState(controller, direction);
            }

            if (isThrowing && controller != null)
            {
                IsProcessingThrowRemoval = false;
            }

            if (controller != null && !skipStateReset)
            {
                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag")
                    {
                        var currentMain = controller != null ? BagPatches.GetMainSeatObject(controller) : null;
                        if (currentMain != null)
                        {
                            var newState = new BaggedObject();
                            newState.targetObject = currentMain;
                            esm.SetNextState(newState);
                        }
                        else
                        {
                            esm.SetNextStateToMain();
                        }
                        break;
                    }
                }
            }
            if (controller != null)
            {

                MarkMassDirty(controller);
            }

            if (obj != null && !isDestroying && !isThrowing)
            {

                if (controller != null)
                {
                    var bagState = BagPatches.GetState(controller);
                    if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(obj, out var disabledStates))
                    {
                        BodyColliderCache.RestoreMovementColliders(disabledStates);
                        bagState.DisabledCollidersByObject.TryRemove(obj, out _);

                        Log.Debug($"[RemoveBaggedObject] Restored movement colliders for ungrabbable enemy {obj.name}");
                    }
                }
            }

            if (PluginConfig.Instance.EnableObjectPersistence.Value)
            {
                var teleporterInteraction = (obj != null) ? obj.GetComponent<RoR2.TeleporterInteraction>() : null;
                if (teleporterInteraction != null && obj != null)
                {
                    PersistenceManager.UnmarkTeleporterAsBagged(obj);
                    teleporterInteraction.enabled = true;
                    MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);

                    var primary = MultiTeleporterTracker.GetPrimary();
                    if (primary != null)
                    {
                        TeleporterInteraction.instance = primary;
                    }
                }
            }

            if (obj != null && controller != null)
            {
                API.DrifterBagAPI.InvokeOnObjectReleased(controller, obj, isDestroying);
            }
        }

        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;

            var state = BagPatches.GetState(controller);

            float previousTotalMass = 0f;
            if (_baggedMassField != null)
            {
                previousTotalMass = (float)_baggedMassField.GetValue(controller);
            }

            float totalMass;

            if (PluginConfig.Instance.EnableBalance.Value &&
                PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.All)
            {

                totalMass = 0f;
                var list = BagPatches.GetState(controller).BaggedObjects;
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                        if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                        {
                            totalMass += controller.CalculateBaggedObjectMass(obj);
                        }
                    }
                }
            }
            else
            {

                var mainSeatObj = BagPatches.GetMainSeatObject(controller);
                if (mainSeatObj != null && !ProjectileRecoveryPatches.IsInProjectileState(mainSeatObj))
                {
                    totalMass = controller.CalculateBaggedObjectMass(mainSeatObj);
                }
                else
                {
                    totalMass = 0f;
                    var fallbackList = BagPatches.GetState(controller).BaggedObjects;
                    if (fallbackList != null)
                    {
                        foreach (var obj in fallbackList)
                        {
                            if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                            {
                                totalMass += controller.CalculateBaggedObjectMass(obj);
                            }
                        }
                    }
                }
            }

            totalMass = Mathf.Max(totalMass, 0f);

            if (_baggedMassField != null)
            {
                _baggedMassField.SetValue(controller, totalMass);

                controller.GetComponent<CharacterBody>()?.RecalculateStats();

                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag" && esm.state is BaggedObject baggedObject)
                    {
                        BaggedObjectPatches.UpdateBagScale(baggedObject, totalMass);
                        ReflectionCache.BaggedObject.BaggedMass?.SetValue(baggedObject, totalMass);
                        break;
                    }
                }

                UpdateModWalkSpeedPenalty(controller, totalMass);
            }

            UIPatches.UpdateMassCapacityUIOnCapacityChange(controller);

            if (PluginConfig.Instance.EnableBalance.Value)
            {
                bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                if (isScaleUncapped || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                {
                    UpdateUncappedBagScale(controller, totalMass);
                }
            }

            state.ClearMassDirty();

            API.DrifterBagAPI.InvokeOnMassRecalculated(controller, totalMass, previousTotalMass);

            if (PluginConfig.Instance.EnableBalance.Value)
            {
                float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
                if (massCapacity > 0f)
                {
                    float massRatio = totalMass / massCapacity;
                    if (massRatio > 1.0f)
                    {
                        API.DrifterBagAPI.InvokeOnOverencumbered(controller, massRatio);
                    }
                }
            }
        }

        public static void UpdateModWalkSpeedPenalty(DrifterBagController controller, float totalMass)
        {
            if (controller == null) return;
            var motor = controller.GetComponent<CharacterMotor>();
            if (motor == null) return;

            float penalty = 0f;
            if (PluginConfig.Instance.EnableBalance.Value || PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                var body = controller.GetComponent<CharacterBody>();
                float health = body != null ? body.maxHealth : 0f;
                float level = body != null ? body.level : 1f;
                float stocks = body != null && body.skillLocator != null && body.skillLocator.utility != null
                    ? body.skillLocator.utility.maxStock : 1f;
                float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
                float totalCapacity = CapacityScalingSystem.GetTotalCapacity(controller);

                var penaltyVars = _penaltyVarsBuffer;
                penaltyVars.Clear();
                penaltyVars["T"] = totalMass;
                penaltyVars["M"] = massCapacity;
                penaltyVars["C"] = totalCapacity;
                penaltyVars["H"] = health;
                penaltyVars["L"] = level;
                penaltyVars["MC"] = PluginConfig.Instance.ParsedMassCap;
                penaltyVars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;

                penalty = FormulaParser.Evaluate(PluginConfig.Instance.MovespeedPenaltyFormula.Value, penaltyVars);
            }

            if (totalMass <= 0f || penalty <= 0f)
            {

                RemoveModWalkSpeedPenalty(controller);
                return;
            }

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
                var motor = controller.GetComponent<CharacterMotor>();
                motor?.RemoveWalkSpeedPenalty(modifier);
                _modWalkSpeedModifiers.Remove(controller);
            }
        }

        public static void SuppressVanillaWalkSpeedModifier(BaggedObject instance)
        {
            if (instance == null) return;

            if (!PluginConfig.Instance.EnableBalance.Value && !PluginConfig.Instance.BottomlessBagEnabled.Value) return;

            try
            {
                var modifier = _walkSpeedModifierField?.GetValue(instance) as CharacterMotor.WalkSpeedPenaltyModifier;
                if (modifier != null)
                {
                    var motor = instance.outer?.GetComponent<CharacterMotor>();
                    motor?.RemoveWalkSpeedPenalty(modifier);
                    _walkSpeedModifierField?.SetValue(instance, null);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SuppressVanillaWalkSpeedModifier] Error: {ex.Message}");
            }
        }

        public static void UpdateUncappedBagScale(DrifterBagController controller, float mass)
        {
            if (controller == null) return;

            var uncappedScaleComponent = BagPatches.GetState(controller).UncappedBagScale;
            if (uncappedScaleComponent == null)
            {

                uncappedScaleComponent = controller.gameObject.GetComponent<UncappedBagScaleComponent>();
                if (uncappedScaleComponent == null)
                {
                    uncappedScaleComponent = controller.gameObject.AddComponent<UncappedBagScaleComponent>();
                    uncappedScaleComponent.Initialize(controller);

                    if (uncappedScaleComponent != null && uncappedScaleComponent.IsInitialized)
                    {
                        BagPatches.GetState(controller).UncappedBagScale = uncappedScaleComponent;
                    }
                    else
                    {
                        Log.Warning($"[BagPatch] Failed to initialize UncappedBagScaleComponent for {controller.name}");
                        return;
                    }
                }
                else
                {

                    BagPatches.GetState(controller).UncappedBagScale = uncappedScaleComponent;
                }
            }

            if (uncappedScaleComponent != null && uncappedScaleComponent.IsInitialized)
            {
                uncappedScaleComponent.UpdateScaleFromMass(mass);
            }
        }

        private static string GetBagStateMachineState(DrifterBagController controller)
        {
            if (controller == null) return "null";
            var stateMachines = controller.GetComponents<EntityStateMachine>();
            foreach (var esm in stateMachines)
            {
                if (esm.customName == "Bag")
                {
                    return esm.state?.GetType().Name ?? "null";
                }
            }
            return "not found";
        }

        private static void EjectMainPassengerIfServer(DrifterBagController controller, GameObject obj, bool isDestroying)
        {
            if (!AuthorityGuard.IsServerWithPassenger(controller, obj)) return;

            if (isDestroying)
            {
                try
                {
                        Log.Debug($"[RemoveBaggedObject] About to eject passenger from main seat: {BagHelpers.GetSafeName(obj)}");

                    controller.vehicleSeat.EjectPassenger(obj);

                        Log.Debug($"[RemoveBaggedObject] Successfully ejected passenger from main seat");
                }
                catch (Exception ex)
                {
                    Log.Error($"[RemoveBaggedObject] Error ejecting passenger: {ex.GetType().Name} - {ex.Message}");

                    try
                    {
                            Log.Debug($"[RemoveBaggedObject] Forcibly clearing passenger state due to exception.");

                        var passengerField = typeof(RoR2.VehicleSeat).GetField("passengerBodyObject", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (passengerField != null) passengerField.SetValue(controller.vehicleSeat, null);

                        controller.vehicleSeat.NetworkpassengerBodyObject = null;
                    }
                    catch (Exception innerEx)
                    {
                        Log.Error($"[RemoveBaggedObject] Failed to forcefully clear passenger state: {innerEx.Message}");
                    }
                }
            }
            else
            {
                controller.vehicleSeat.EjectPassenger(obj);
            }
        }
    }
}
