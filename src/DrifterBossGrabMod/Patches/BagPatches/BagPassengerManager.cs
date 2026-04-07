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
using EntityStates;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Patches
{
    // Provides static helper methods for managing bag passengers and mass calculation
    public static class BagPassengerManager
    {
        // Cached reflection fields - using centralized ReflectionCache
        private static readonly FieldInfo _baggedMassField = ReflectionCache.DrifterBagController.BaggedMass;
        private static readonly FieldInfo _walkSpeedModifierField = ReflectionCache.BaggedObject.WalkSpeedModifier;

        // Mod-managed walk speed penalty modifiers
        private static readonly Dictionary<DrifterBagController, CharacterMotor.WalkSpeedPenaltyModifier> _modWalkSpeedModifiers
            = new Dictionary<DrifterBagController, CharacterMotor.WalkSpeedPenaltyModifier>();

        // Static cached lists to avoid per-operation allocations
        private static readonly List<GameObject> _removeKeysBuffer = new List<GameObject>();
        private static readonly Dictionary<string, float> _penaltyVarsBuffer = new Dictionary<string, float>();

        // Track if RemoveBaggedObject is actively processing a throw removal
        public static volatile bool IsProcessingThrowRemoval = false;

        // Marks the bag's mass as dirty
        public static void MarkMassDirty(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPatches.GetState(controller).MarkMassDirty();
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
                 seatDict.Remove(obj, out _);
                 _removeKeysBuffer.Clear();
                 foreach(var kvp in seatDict)
                 {
                     if(kvp.Value != null && kvp.Value.NetworkpassengerBodyObject == obj)
                     {
                         _removeKeysBuffer.Add(kvp.Key);
                     }
                 }
                 foreach(var key in _removeKeysBuffer)
                 {
                      seatDict.TryRemove(key, out _);
                 }
            }

            bool isThrowing = ProjectileRecoveryPatches.IsInProjectileState(obj);

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
                if (targetInstanceId != -1) BagPatches.GetState(controller).RemoveInstanceId(targetInstanceId);

                if (wasMainPassenger)
                {
                    if (NetworkServer.active && controller.vehicleSeat != null && controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    {
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

                    // Fire OnMainPassengerChanged event when main passenger is cleared
                    API.DrifterBagAPI.InvokeOnMainPassengerChanged(controller, mainPassengerBefore, null);

                    if (controller != null && NetworkServer.active && !controller!.hasAuthority && controller.GetComponent<Networking.BottomlessBagNetworkController>() is { } nc ? nc.autoPromoteMainSeat && list.Count > 0 : PluginConfig.Instance.AutoPromoteMainSeat.Value && list.Count > 0 && (NetworkServer.active || (controller && controller!.hasAuthority)))
                    {
                        var newMain = list[0];
                        if (newMain != null && !ProjectileRecoveryPatches.IsInProjectileState(newMain))
                        {
                            // Auto-promote immediately
                            DelayedAutoPromote.Schedule(controller!, newMain, 0.0f);
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
                }

                if (wasMainPassenger && controller != null && obj != null)
                {
                    BaggedObjectStatePatches.ForceCleanupOverrides(controller, obj);
                }
                
                // Clean up initialization tracking
                BaggedObjectStatePatches.BaggedObject_OnExit.ClearObjectSuccessfullyInitialized(obj);
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
                // Set flag to indicate we're processing a throw removal
                IsProcessingThrowRemoval = isThrowing;

                // Restore carousel update - needed to trigger network state update
                BagCarouselUpdater.UpdateCarousel(controller, direction);
            }

            if (controller != null)
            {
                BagCarouselUpdater.UpdateNetworkBagState(controller, direction);
            }

            // Clear flag after updates are done
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
                // Mark mass as dirty.
                MarkMassDirty(controller);
            }

              if (obj != null && !isDestroying && !isThrowing)
              {
                  var preserver = obj.GetComponent<ModelStatePreserver>();
                  if (preserver != null)
                  {
                      preserver.RestoreOriginalState(false);
                      UnityEngine.Object.Destroy(preserver);
                  }

                  // Ensure colliders are restored when removing ungrabbable enemies from the bag manually
                  if (controller != null)
                  {
                      var bagState = BagPatches.GetState(controller);
                      if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(obj, out var disabledStates))
                      {
                          BodyColliderCache.RestoreMovementColliders(disabledStates);
                          bagState.DisabledCollidersByObject.TryRemove(obj, out _);
                          
                          if (PluginConfig.Instance.EnableDebugLogs.Value)
                          {
                              Log.Info($"[RemoveBaggedObject] Restored movement colliders for ungrabbable enemy {obj.name}");
                          }
                      }
                  }
              }

              // Fire OnObjectReleased event
              if (obj != null && controller != null)
              {
                  API.DrifterBagAPI.InvokeOnObjectReleased(controller, obj, isDestroying);
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
                return;
            }

            // Store previous mass for event
            float previousTotalMass = 0f;
            if (_baggedMassField != null)
            {
                previousTotalMass = (float)_baggedMassField.GetValue(controller);
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
                        if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                        {
                            totalMass += controller.CalculateBaggedObjectMass(obj);
                        }
                    }
                }
            }
            else
            {
                // Current Mode: Calculate individual object mass (main seat only)
                var mainSeatObj = BagPatches.GetMainSeatObject(controller);
                if (mainSeatObj != null && !ProjectileRecoveryPatches.IsInProjectileState(mainSeatObj))
                {
                    totalMass = controller.CalculateBaggedObjectMass(mainSeatObj);
                }
                else
                {
                    totalMass = 0f;
                }
            }

            // Clamp or uncap based on config
            bool isMassUncapped = false;
            float maxMass = Constants.Limits.MaxMass;
            
            if (PluginConfig.Instance.IsMassCapInfinite)
            {
                isMassUncapped = true;
            }
            else if (float.TryParse(PluginConfig.Instance.MassCap.Value, out float parsedMassCap))
            {
                maxMass = parsedMassCap;
            }

            if (!isMassUncapped)
            {
                totalMass = Mathf.Clamp(totalMass, 0f, maxMass);
            }
            else
            {
                totalMass = Mathf.Max(totalMass, 0f);
            }

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
            if (PluginConfig.Instance.EnableBalance.Value)
            {
                bool isScaleUncapped = PluginConfig.Instance.IsBagScaleCapInfinite;
                if (isScaleUncapped || PluginConfig.Instance.ParsedBagScaleCap > 1f)
                {
                    UpdateUncappedBagScale(controller, totalMass);
                }
            }

            // Clear dirty flag after successful recalculation
            state.ClearMassDirty();

            // Fire OnMassRecalculated event
            API.DrifterBagAPI.InvokeOnMassRecalculated(controller, totalMass, previousTotalMass);

            // Fire OnOverencumbered event if mass ratio exceeds 1.0
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

        // Updates the mod-managed walk speed penalty based on aggregate baggedMass.
        public static void UpdateModWalkSpeedPenalty(DrifterBagController controller, float totalMass)
        {
            if (controller == null) return;
            var motor = controller.GetComponent<CharacterMotor>();
            if (motor == null) return;

            // Calculate penalty using formula when EnableBalance is true
            float penalty = 0f;
            if (PluginConfig.Instance.EnableBalance.Value)
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
                    }
                    else
                    {
                        Log.Warning($"[BagPatch] Failed to initialize UncappedBagScaleComponent for {controller.name}");
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
