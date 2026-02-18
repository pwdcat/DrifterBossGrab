using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    // Handles seat transitions during passenger cycling.
    public static class SeatTransitionHandler
    {
        // Handles transition from an object to null state.
        internal static void HandleNullStateTransition(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject actualMainPassenger, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict, int validObjectCount)
        {
            if (!SeatValidator.HasSpaceForNullStateTransition(bagController, validObjectCount, localSeatDict))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[HandleNullStateTransition] No space for null transition, aborting.");
                return;
            }

            if (!SeatValidator.ValidateNullStateTransition(bagController, actualMainPassenger, localSeatDict))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[HandleNullStateTransition] ValidateNullStateTransition failed, aborting.");
                return;
            }
            var seatForCurrent = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict);
            vehicleSeat.EjectPassenger(actualMainPassenger);
            if (actualMainPassenger != null)
            {
                BagPatches.SetMainSeatObject(bagController, null);
                BaggedObjectPatches.RemoveUIOverlay(actualMainPassenger, bagController);
            }
            BagPatches.SetMainSeatObject(bagController, null);
            if (seatForCurrent != null && actualMainPassenger != null)
            {
                seatForCurrent.AssignPassenger(actualMainPassenger);
                localSeatDict[actualMainPassenger] = seatForCurrent;
            }
            if (actualMainPassenger != null)
            {
                BagPatches.SetMainSeatObject(bagController, null);
            }
            BaggedObjectPatches.RemoveUIOverlayForNullState(bagController);
        }

        // Handles transition from null state to an object.
        internal static void HandleNullToObjectTransition(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict)
        {
             if (targetObject == null) return;

            var sourceAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject, localSeatDict);
            if (sourceAdditionalSeat != null)
            {
                sourceAdditionalSeat.EjectPassenger(targetObject);
                System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
            }

            // Save state before assigning passenger
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[HandleNullToObjectTransition] Saving state before AssignPassenger (null -> {targetObject.name})");

            bagController.AssignPassenger(targetObject);
            BagPatches.SetMainSeatObject(bagController, targetObject);

            // Restore target object's state after cycling to it
            if (targetObject != null)
            {
                var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                if (storedState != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[HandleNullToObjectTransition] Restoring stored state for {targetObject.name}");
                    var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
                    if (baggedState != null)
                    {
                        storedState.ApplyToBaggedObject(baggedState);
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleNullToObjectTransition] No stored state found for {targetObject.name}, using fresh state");
                }
            }
        }

        // Handles swapping between two objects.
        internal static void HandleObjectSwap(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject currentObject, GameObject targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict, int direction)
        {
             if (targetObject == null) return;

            if (!SeatValidator.ValidateSeatStateForSwap(bagController, currentObject, targetObject, localSeatDict))
            {
                return;
            }

            // Check if current is physically in seat (server-side).
            bool currentIsPhysicallyInSeat = vehicleSeat.hasPassenger &&
                vehicleSeat.NetworkpassengerBodyObject != null &&
                vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == currentObject.GetInstanceID();

            var targetAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject);

            if (currentIsPhysicallyInSeat)
            {
                // Server-side swap.
                vehicleSeat.EjectPassenger(currentObject);
                if (currentObject != null)
                {
                    BagPatches.SetMainSeatObject(bagController, null);
                    BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                }
                if (targetAdditionalSeat != null)
                {
                    targetAdditionalSeat.EjectPassenger(targetObject);
                    System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                    targetAdditionalSeat.AssignPassenger(currentObject);
                    BagPatches.SetMainSeatObject(bagController, null);
                    if (currentObject != null)
                    {
                        BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                    }
                    if (currentObject != null) localSeatDict[currentObject] = targetAdditionalSeat;
                }
                if (targetAdditionalSeat == null)
                {
                    var newSeat = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict);
                    if (newSeat != null && currentObject != null)
                    {
                        newSeat.AssignPassenger(currentObject);
                        localSeatDict[currentObject] = newSeat;
                    }
                }

                // Save current state.
                if (currentObject != null)
                {
                    var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, currentObject);
                    if (currentState != null)
                    {
                        var stateData = new Core.BaggedObjectStateData();
                        stateData.CaptureFromBaggedObject(currentState);
                        BaggedObjectPatches.SaveObjectState(bagController, currentObject, stateData);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[HandleObjectSwap] Saved state for {currentObject.name} before cycling away");
                    }
                }

                vehicleSeat.AssignPassenger(targetObject);
                BagPatches.SetMainSeatObject(bagController, targetObject);

                // Restore target state.
                if (targetObject != null)
                {
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (storedState != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[HandleObjectSwap] Restoring stored state for {targetObject.name}");
                        var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
                        if (baggedState != null)
                        {
                            storedState.ApplyToBaggedObject(baggedState);
                        }
                    }

                }

                BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
            else
            {
                // Client/Message swap.
                if (targetAdditionalSeat != null)
                {
                    targetAdditionalSeat.EjectPassenger(targetObject);
                    System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                    if (currentObject != null)
                    {
                        targetAdditionalSeat.AssignPassenger(currentObject);
                        localSeatDict[currentObject] = targetAdditionalSeat;
                    }
                }

                // Save current object's state before cycling away
                if (currentObject != null)
                {
                    var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, currentObject);
                    if (currentState != null)
                    {
                        var stateData = new Core.BaggedObjectStateData();
                        stateData.CaptureFromBaggedObject(currentState);
                        BaggedObjectPatches.SaveObjectState(bagController, currentObject, stateData);

                    }
                }

                BagPatches.SetMainSeatObject(bagController, targetObject);
                if (targetObject != null)
                {
                    // Ensure object is removed from additional seats dict immediately.
                    var realDict = BagPatches.GetState(bagController).AdditionalSeats;
                    if (realDict != null)
                    {
                        realDict.TryRemove(targetObject, out _);
                    }

                    vehicleSeat.AssignPassenger(targetObject);

                    // Restore target state.
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (storedState != null)
                    {

                        var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
                        if (baggedState != null)
                        {
                            storedState.ApplyToBaggedObject(baggedState);
                        }
                    }

                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                }
                BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
        }
    }
}
