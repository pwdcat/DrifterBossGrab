using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    // Provides validation methods for seat configurations and transitions
    public static class SeatValidator
    {
        // Validates seat configuration for a bag controller - ensures all tracked seats have correct passenger assignments
        internal static bool ValidateSeatConfiguration(DrifterBagController bagController, List<GameObject> validObjects, GameObject? actualMainPassenger, bool isInNullState, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (!isInNullState && actualMainPassenger == null)
            {
                return false;
            }
            if (isInNullState && validObjects.Count == 0)
            {
                return false;
            }
            if (seatDict.Count > 0)
            {
                foreach (var kvp in seatDict)
                {
                    var trackedObject = kvp.Key;
                    var trackedSeat = kvp.Value;
                    if (trackedSeat == null)
                    {
                        return false;
                    }
                    if (trackedSeat.hasPassenger)
                    {
                        var actualPassenger = trackedSeat.NetworkpassengerBodyObject;
                        if (actualPassenger.GetInstanceID() != trackedObject.GetInstanceID())
                        {
                            return false;
                        }
                    }
                    else if (!trackedSeat.hasPassenger)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Validates seat state for performing a swap between current and target objects
        internal static bool ValidateSeatStateForSwap(DrifterBagController bagController, GameObject? currentObject, GameObject? targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (targetObject == null) return false;
            var mainSeat = bagController.vehicleSeat;
            if (mainSeat == null)
            {
                return false;
            }
            bool isActuallyInMainSeat = false;
            if (mainSeat.hasPassenger)
            {
                var actualMainPassenger = mainSeat.NetworkpassengerBodyObject;
                isActuallyInMainSeat = actualMainPassenger != null && actualMainPassenger.GetInstanceID() == currentObject!.GetInstanceID();
            }

            // For client-grabbed objects, the server's vehicleSeat may not be populated
            // Trust the mainSeatDict tracking as authoritative for these cases
            if (!isActuallyInMainSeat && currentObject != null)
            {
                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                if (trackedMain != null && trackedMain.GetInstanceID() == currentObject.GetInstanceID())
                {

                    isActuallyInMainSeat = true;
                }
            }

            if (!isActuallyInMainSeat)
            {

                return false;
            }
            var targetAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject, seatDict);
            if (targetAdditionalSeat != null)
            {
                if (targetAdditionalSeat.hasPassenger)
                {
                    var actualTargetPassenger = targetAdditionalSeat.NetworkpassengerBodyObject;
                    if (actualTargetPassenger != null && actualTargetPassenger.GetInstanceID() == targetObject.GetInstanceID())
                    {
                    }
                }
            }
            else
            {

                return true; // Use fallback logic (e.g. FindOrCreateEmptySeat) in CycleToNextObject
            }
            if (seatDict.Count > 0)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    if (seat != null && seat.hasPassenger)
                    {
                        var seatPassenger = seat.NetworkpassengerBodyObject;
                        if (seatPassenger != null && currentObject != null && seatPassenger.GetInstanceID() == currentObject.GetInstanceID() && seat != mainSeat)
                        {
                            return false;
                        }
                        if (seatPassenger != null && seatPassenger.GetInstanceID() == targetObject.GetInstanceID() && seat == mainSeat)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // Validates that a transition to null state is possible
        internal static bool ValidateNullStateTransition(DrifterBagController bagController, GameObject? currentObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (currentObject == null) return false;
            var mainSeat = bagController.vehicleSeat;
            if (mainSeat == null)
            {
                return false;
            }
            bool isActuallyInMainSeat = false;
            if (mainSeat.hasPassenger)
            {
                var actualMainPassenger = mainSeat.NetworkpassengerBodyObject;
                isActuallyInMainSeat = actualMainPassenger != null && actualMainPassenger.GetInstanceID() == currentObject.GetInstanceID();
            }
            if (!isActuallyInMainSeat)
            {
                return false;
            }
            var availableSeat = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref seatDict);
            if (availableSeat == null)
            {
                return false;
            }
            if (seatDict.Count > 0)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    if (seat != null && seat.hasPassenger)
                    {
                        var seatPassenger = seat.NetworkpassengerBodyObject;
                        if (seatPassenger.GetInstanceID() == currentObject.GetInstanceID() && seat != mainSeat)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // Checks if there is space for a transition to null state
        internal static bool HasSpaceForNullStateTransition(DrifterBagController bagController, int currentObjectCount, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);

            if (currentObjectCount >= effectiveCapacity)
            {

                return false;
            }

            return true;
        }
    }
}
