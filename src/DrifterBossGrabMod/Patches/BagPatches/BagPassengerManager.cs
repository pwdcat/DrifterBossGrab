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
using EntityStates;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Patches
{
    // Provides static helper methods for managing bag passengers and mass calculation
    public static class BagPassengerManager
    {
        // Cached reflection fields
        private static readonly FieldInfo _baggedMassField = AccessTools.Field(typeof(DrifterBagController), "baggedMass");
        private static readonly FieldInfo _walkSpeedModifierField = AccessTools.Field(typeof(BaggedObject), "walkSpeedModifier");

        // Mod-managed walk speed penalty modifiers
        private static readonly Dictionary<DrifterBagController, CharacterMotor.WalkSpeedPenaltyModifier> _modWalkSpeedModifiers
            = new Dictionary<DrifterBagController, CharacterMotor.WalkSpeedPenaltyModifier>();

        // Marks the bag's mass as dirty
        public static void MarkMassDirty(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).MarkMassDirty();
            Log.Debug($"[MarkMassDirty] Marked mass as dirty for {controller.name}");
        }

        // Removes a bagged object from the controller
        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false, bool skipStateReset = false)
        {
            if (ReferenceEquals(obj, null)) return;

            int targetInstanceId = ErrorHandler.SafeExecute("RemoveBaggedObject.GetInstanceID", () => obj.GetInstanceID(), -1);

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
            if (seatDict != null)
            {
                 if (seatDict.ContainsKey(obj))
                 {
                     System.Collections.Generic.CollectionExtensions.Remove(seatDict, obj, out _);
                 }
                 var toRemove = new List<GameObject>();
                 foreach(var kvp in seatDict)
                 {
                     if(kvp.Value != null && kvp.Value.NetworkpassengerBodyObject == obj)
                     {
                         toRemove.Add(kvp.Key);
                     }
                 }
                 foreach(var key in toRemove)
                 {
                     System.Collections.Generic.CollectionExtensions.Remove(seatDict, key, out _);
                 }
            }

            bool isThrowing = OtherPatches.IsInProjectileState(obj);

            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;

            // Re-fetch to ensure we have the list object ref if needed
            if (list != null)
            {
                ErrorHandler.SafeExecute("RemoveBaggedObject.DestroyTracker", () =>
                {
                    var tracker = obj.GetComponent<BaggedObjectTracker>();
                    if (tracker != null)
                    {
                        tracker.isRemovingManual = true;
                        UnityEngine.Object.Destroy(tracker);
                    }
                });

                list.RemoveAll(x => ReferenceEquals(x, null) || (x is UnityEngine.Object uo && !uo) || (targetInstanceId != -1 && x.GetInstanceID() == targetInstanceId));

                if (wasMainPassenger)
                {
                    if (NetworkServer.active && controller.vehicleSeat != null && controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    {
                        Log.Debug($"[RemoveBaggedObject] Force ejecting {(obj ? obj.name : "null")} from Main Seat to clear it (isThrowing: {isThrowing}, isDestroying: {isDestroying})");

                        if (isDestroying)
                        {
                            ErrorHandler.SafeExecute("RemoveBaggedObject.EjectPassenger", () =>
                            {
                                controller.vehicleSeat.EjectPassenger(obj);
                            });
                        }
                        else
                        {
                            controller.vehicleSeat.EjectPassenger(obj);
                        }
                    }

                    BagPatches.SetMainSeatObject(controller, null);

                    if (PluginConfig.Instance.AutoPromoteMainSeat.Value && list.Count > 0 && (NetworkServer.active || (controller && controller.hasAuthority)))
                    {
                        var newMain = list[0];
                        if (newMain != null && !OtherPatches.IsInProjectileState(newMain))
                        {
                            Log.Debug($"[RemoveBaggedObject] Auto-promoting {(newMain ? newMain.name : "null")} to Main Seat after removal of previous main.");

                            // Auto-promote immediately
                            DelayedAutoPromote.Schedule(controller, newMain, 0.0f);
                        }
                    }
                }
            }

            if (isThrowing)
            {
                BagHelpers.CleanupEmptyAdditionalSeats(controller);
            }

            // Clean up object state when object is truly removed
            // Keep state if object is moving to additional seat
            if (isDestroying || isThrowing)
            {
                if (controller != null && obj != null)
                {
                    BaggedObjectPatches.CleanupObjectState(controller, obj);
                    Log.Debug($"[RemoveBaggedObject] Cleaned up state for {obj.name} (isDestroying: {isDestroying}, isThrowing: {isThrowing})");
                }
            }

            if (obj != null)
            {
                var timer = obj.GetComponent<AdditionalSeatBreakoutTimer>();
                if (timer != null)
                {
                    UnityEngine.Object.Destroy(timer);
                }
            }

            if (UnityEngine.Networking.NetworkServer.active && list != null)
            {
                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, controller);
            }

            int direction = wasMainPassenger ? 1 : 0;
            if (controller != null)
            {
                BagCarouselUpdater.UpdateCarousel(controller, direction);
            }

            if (controller != null)
            {
                BagCarouselUpdater.UpdateNetworkBagState(controller, direction);
            }

            if (controller != null && !skipStateReset)
            {
                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag")
                    {
                        Log.Debug($"[RemoveBaggedObject] Updating Bag state machine for {(controller ? controller.name : "null")}");

                         var currentMain = controller != null ? BagPatches.GetMainSeatObject(controller) : null;
                         if (currentMain != null)
                         {
                              Log.Debug($"[RemoveBaggedObject] Transitioning Bag state machine to BaggedObject for {(currentMain ? currentMain.name : "null")}");
                              var newState = new BaggedObject();
                              newState.targetObject = currentMain;
                              esm.SetNextState(newState);
                         }
                         else
                         {
                              Log.Debug($"[RemoveBaggedObject] Resetting Bag state machine to Main (Idle)");
                              esm.SetNextStateToMain();
                         }
                        break;
                    }
                }
            }
            else if (controller != null && skipStateReset)
            {
                Log.Debug($"[RemoveBaggedObject] Skipping Bag state machine reset (skipStateReset=true) - delayed promotion will handle state transition");
            }
            if (controller != null)
            {
                // Mark mass as dirty.
                MarkMassDirty(controller);
            }

            if (obj != null && !isDestroying && !isThrowing)
            {
                var preserver = obj.GetComponent<ModelStatePreserver>();
                if (preserver != null)
                {
                    Log.Debug($"[BagPatches.RemoveBaggedObject] === REMOVING BAGGED OBJECT ===");
                    Log.Debug($"[BagPatches.RemoveBaggedObject] Object: {obj.name}");
                    Log.Debug($"[BagPatches.RemoveBaggedObject] Found ModelStatePreserver on {obj.name}");
                    Log.Debug($"[BagPatches.RemoveBaggedObject] Restoring original model state for {obj.name}");
                    Log.Debug($"[BagPatches.RemoveBaggedObject] isDestroying: {isDestroying}, isThrowing: {isThrowing}");
                    Log.Debug($"[BagPatches.RemoveBaggedObject] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                    Log.Debug($"[BagPatches.RemoveBaggedObject] ================================");

                    preserver.RestoreOriginalState(false);
                    UnityEngine.Object.Destroy(preserver);
                    Log.Debug($"[BagPatches.RemoveBaggedObject] >>> DESTROYED ModelStatePreserver on {obj.name}");
                }
                else
                {
                    Log.Debug($"[BagPatches.RemoveBaggedObject] >>> NO ModelStatePreserver found on {obj.name}");
                }
            }
        }

        // Forces recalculation of the bag's mass.
        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;

            // Check dirty flag to prevent redundant calculations
            var state = BagPatches.GetState(controller);
            if (!state.IsMassDirty)
            {
                Log.Debug($"[ForceRecalculateMass] Skipping recalculation for {controller.name} - mass is not dirty");
                return;
            }

            float totalMass;

            // Check StateCalculationMode to determine mass calculation method
            // When StateCalculationMode is All, always use aggregate mass regardless of StateCalculationModeEnabled
            if (PluginConfig.Instance.EnableBalance.Value &&
                PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.All)
            {
                // All Mode: Calculate aggregate mass across all bagged objects
                totalMass = 0f;
                var list = BagPatches.GetState(controller).BaggedObjects;
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                        if (obj != null && !OtherPatches.IsInProjectileState(obj))
                        {
                            totalMass += controller.CalculateBaggedObjectMass(obj);
                        }
                    }
                }

                // Apply All mode mass multiplier
                totalMass *= PluginConfig.Instance.AllModeMassMultiplier.Value;

                Log.Debug($"[ForceRecalculateMass] All Mode: Aggregated mass {totalMass} for {controller.name} (Objects: {(list?.Count ?? 0)}, Multiplier: {PluginConfig.Instance.AllModeMassMultiplier.Value})");
            }
            else
            {
                // Current Mode: Calculate individual object mass (main seat only)
                var mainSeatObj = BagPatches.GetMainSeatObject(controller);
                if (mainSeatObj != null && !OtherPatches.IsInProjectileState(mainSeatObj))
                {
                    totalMass = controller.CalculateBaggedObjectMass(mainSeatObj);

                    Log.Debug($"[ForceRecalculateMass] Current Mode: Individual mass {totalMass} for {controller.name} (Object: {mainSeatObj.name})");
                }
                else
                {
                    totalMass = 0f;

                    Log.Debug($"[ForceRecalculateMass] Current Mode: No main seat object, mass set to 0 for {controller.name}");
                }
            }

            // Clamp or uncap based on config - only apply UncapMass when EnableBalance is true
            if (!PluginConfig.Instance.EnableBalance.Value || !PluginConfig.Instance.UncapMass.Value)
            {
                totalMass = Mathf.Clamp(totalMass, 0f, Constants.Limits.MaxMass);
            }
            else
            {
                totalMass = Mathf.Max(totalMass, 0f);
            }

            if (_baggedMassField != null)
            {
                _baggedMassField.SetValue(controller, totalMass);
                Log.Debug($"[ForceRecalculateMass] Set final baggedMass to {totalMass} for {controller.name} (Mode: {PluginConfig.Instance.StateCalculationMode.Value})");

                controller.GetComponent<CharacterBody>()?.RecalculateStats();

                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag" && esm.state is BaggedObject baggedObject)
                    {
                        BaggedObjectPatches.UpdateBagScale(baggedObject, totalMass);
                        break;
                    }
                }

                // Update mod-managed walk speed penalty based on aggregate mass.
                // This works regardless of whether a BaggedObject state is active,
                // so the penalty persists even on the null slot (selectedIndex=-1)
                UpdateModWalkSpeedPenalty(controller, totalMass);
            }

            // Update Capacity UI
            UIPatches.UpdateMassCapacityUIOnCapacityChange(controller);

            // Update uncapped bag scale if enabled - only when EnableBalance is true
            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapBagScale.Value)
            {
                UpdateUncappedBagScale(controller, totalMass);
            }

            // Clear dirty flag after successful recalculation
            state.ClearMassDirty();
        }

        // Updates the mod-managed walk speed penalty based on aggregate baggedMass.
        public static void UpdateModWalkSpeedPenalty(DrifterBagController controller, float totalMass)
        {
            if (controller == null) return;
            var motor = controller.GetComponent<CharacterMotor>();
            if (motor == null) return;

            // Calculate penalty using config settings
            // Only apply balance penalty settings when EnableBalance is true
            var minPenalty = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.MinMovespeedPenalty.Value : 0f;
            var maxPenalty = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.MaxMovespeedPenalty.Value : 0f;
            var finalLimit = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.FinalMovespeedPenaltyLimit.Value : 0f;
            // Calculate mass ratio for penalty interpolation
            float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
            float value = Mathf.Clamp(totalMass, Constants.Limits.MinimumMass, massCapacity);
            // Only apply UncapMass when EnableBalance is true
            if (PluginConfig.Instance.EnableBalance.Value && PluginConfig.Instance.UncapMass.Value)
                value = Mathf.Max(totalMass, Constants.Limits.MinimumMass);

            float t = Mathf.InverseLerp(Constants.Limits.MinimumMass, massCapacity, value);
            // Use config settings for penalty range instead of hardcoded values
            float penalty = Mathf.Lerp(minPenalty, maxPenalty, t);

            // Clamp to final limit
            penalty = Mathf.Min(penalty, finalLimit);

            if (totalMass <= 0f || penalty <= 0f)
            {
                // No objects — remove modifier
                RemoveModWalkSpeedPenalty(controller);
                return;
            }

            if (_modWalkSpeedModifiers.TryGetValue(controller, out var modifier))
            {
                // Update in-place (WalkSpeedPenaltyModifier is a class, so this is by reference)
                modifier.penalty = penalty;
                motor.RecalculateWalkSpeedPenalty();
            }
            else
            {
                // Create new
                var newModifier = new CharacterMotor.WalkSpeedPenaltyModifier { penalty = penalty };
                motor.AddWalkSpeedPenalty(newModifier);
                _modWalkSpeedModifiers[controller] = newModifier;
            }
        }

        // Removes the mod-managed walk speed penalty.
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

        // Suppresses the vanilla-created walk speed modifier.
        public static void SuppressVanillaWalkSpeedModifier(BaggedObject instance)
        {
            if (instance == null) return;
            ErrorHandler.SafeExecute("SuppressVanillaWalkSpeedModifier", () =>
            {
                var modifier = _walkSpeedModifierField?.GetValue(instance) as CharacterMotor.WalkSpeedPenaltyModifier;
                if (modifier != null)
                {
                    var motor = instance.outer?.GetComponent<CharacterMotor>();
                    motor?.RemoveWalkSpeedPenalty(modifier);
                    _walkSpeedModifierField?.SetValue(instance, null);
                }
            });
        }

        // Update the uncapped bag scale component.
        public static void UpdateUncappedBagScale(DrifterBagController controller, float mass)
        {
            if (controller == null) return;

            // Get or create uncapped bag scale component
            var uncappedScaleComponent = BagPatches.GetState(controller).UncappedBagScale;
            if (uncappedScaleComponent == null)
            {
                // Add component if it doesn't exist
                uncappedScaleComponent = controller.gameObject.GetComponent<UncappedBagScaleComponent>();
                if (uncappedScaleComponent == null)
                {
                    uncappedScaleComponent = controller.gameObject.AddComponent<UncappedBagScaleComponent>();
                    uncappedScaleComponent.Initialize(controller);

                    // Only store in state if initialization was successful
                    // The component's IsInitialized property indicates success
                    if (uncappedScaleComponent != null && uncappedScaleComponent.IsInitialized)
                    {
                        BagPatches.GetState(controller).UncappedBagScale = uncappedScaleComponent;
                        Log.Debug($"[UpdateUncappedBagScale] Successfully initialized and stored UncappedBagScaleComponent for {controller.name}");
                    }
                    else
                    {
                        Log.Warning($"[UpdateUncappedBagScale] Failed to initialize UncappedBagScaleComponent for {controller.name}");
                        return;
                    }
                }
                else
                {
                    // Component existed but wasn't in state, store it now
                    BagPatches.GetState(controller).UncappedBagScale = uncappedScaleComponent;
                }
            }

            // Update scale based on mass (only if component is valid and initialized)
            if (uncappedScaleComponent != null && uncappedScaleComponent.IsInitialized)
            {
                uncappedScaleComponent.UpdateScaleFromMass(mass);
            }
        }
    }
}
