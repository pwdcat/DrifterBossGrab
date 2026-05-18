#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{

    public static class SeatTransitionHandler
    {

        internal static void HandleNullStateTransition(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject actualMainPassenger, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict, int validObjectCount)
        {
            if (!SeatValidator.HasSpaceForNullStateTransition(bagController, validObjectCount, localSeatDict))
            {
                Log.Debug($"[HandleNullStateTransition] No space for null transition, aborting.");
                return;
            }

            if (!SeatValidator.ValidateNullStateTransition(bagController, actualMainPassenger, localSeatDict))
            {
                Log.Debug($"[HandleNullStateTransition] ValidateNullStateTransition failed, aborting.");
                return;
            }
            var seatForCurrent = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict, true);

            if (actualMainPassenger != null)
            {
                var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, actualMainPassenger);
                if (currentState != null)
                {
                    var stateData = BaggedObjectPatches.LoadObjectState(bagController, actualMainPassenger) ?? new Core.BaggedObjectStateData();
                    if (stateData.targetObject == null) stateData.CalculateFromObject(actualMainPassenger, bagController);
                    stateData.CaptureBreakoutStateFromBaggedObject(currentState);
                    BaggedObjectPatches.SaveObjectState(bagController, actualMainPassenger, stateData);
                }
            }

            if (actualMainPassenger != null)
            {
                BaggedObjectStatePatches.BaggedObject_OnExit.MarkPreserveOverridesDuringCycling(actualMainPassenger);
            }

            vehicleSeat.EjectPassenger(actualMainPassenger);
            if (actualMainPassenger != null)
            {
                BaggedObjectPatches.RemoveUIOverlay(actualMainPassenger, bagController);
            }
            BagPatches.SetMainSeatObject(bagController, null);
            if (seatForCurrent != null && actualMainPassenger != null)
            {
                localSeatDict[actualMainPassenger] = seatForCurrent;
                BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                seatForCurrent.AssignPassenger(actualMainPassenger);

                if (UnityEngine.Networking.NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(actualMainPassenger) && !actualMainPassenger.GetComponent<AdditionalSeatBreakoutTimer>())
                {
                    var timer = actualMainPassenger.AddComponent<AdditionalSeatBreakoutTimer>();
                    timer.controller = bagController;

                    float mass = bagController.CalculateBaggedObjectMass(actualMainPassenger);
                    float baseBreakoutTime = 10f;
                    float breakoutMultiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                    float finalTime = Mathf.Max(baseBreakoutTime - 0.005f * mass, 1f);
                    var hc = actualMainPassenger.GetComponent<CharacterBody>();
                    if (hc && hc.isElite) finalTime *= 0.8f;
                    timer.breakoutTime = finalTime * breakoutMultiplier;

                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, actualMainPassenger);
                    if (storedState != null)
                    {
                        if (storedState.breakoutTime > 0f) timer.breakoutTime = storedState.breakoutTime;
                        timer.SetElapsedBreakoutTime(storedState.elapsedBreakoutTime);
                        timer.breakoutAttempts = storedState.breakoutAttempts;

                    }
                }
            }

            BaggedObjectPatches.RemoveUIOverlayForNullState(bagController);
        }

        internal static void HandleNullToObjectTransition(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict)
        {
            if (targetObject == null) return;

            var sourceAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject, localSeatDict);
            if (sourceAdditionalSeat != null)
            {

                var timer = targetObject.GetComponent<AdditionalSeatBreakoutTimer>();
                if (timer != null)
                {
                    var timerState = BaggedObjectPatches.LoadObjectState(bagController, targetObject) ?? new Core.BaggedObjectStateData();
                    if (timerState.targetObject == null)
                    {
                        timerState.CalculateFromObject(targetObject, bagController);
                    }
                    timerState.CaptureFromAdditionalTimer(timer);
                    BaggedObjectPatches.SaveObjectState(bagController, targetObject, timerState);
                }

                sourceAdditionalSeat.EjectPassenger(targetObject);
                localSeatDict.TryRemove(targetObject, out _);
            }

            int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
            int objectsInBag = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
            bool isBagFull = objectsInBag >= effectiveCapacity;

            if (isBagFull && NetworkServer.active && sourceAdditionalSeat == null)
            {
                    Log.Debug($"[HandleNullToObjectTransition] Bag is full, keeping {targetObject.name} in additional seat instead of main seat");

                var targetAdditionalSeat = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict, true);
                if (targetAdditionalSeat != null)
                {
                    localSeatDict[targetObject] = targetAdditionalSeat;
                    BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                    targetAdditionalSeat.AssignPassenger(targetObject);
                    return;
                }
            }

            Log.Debug($"[HandleNullToObjectTransition] Saving state before AssignPassenger (null -> {targetObject.name})");

            BagPatches.SetMainSeatObject(bagController, targetObject);

            BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

            bool wasSwapping = DrifterBossGrabPlugin._isSwappingPassengers;
            DrifterBossGrabPlugin._isSwappingPassengers = true;
            try
            {
                bagController.AssignPassenger(targetObject);
            }
            finally
            {
                DrifterBossGrabPlugin._isSwappingPassengers = wasSwapping;
            }
        }

        internal static void HandleObjectSwap(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject currentObject, GameObject targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict, int direction)
        {
            if (targetObject == null) return;

            if (!SeatValidator.ValidateSeatStateForSwap(bagController, currentObject, targetObject, localSeatDict))
            {
                return;
            }

            bool currentIsPhysicallyInSeat = vehicleSeat.hasPassenger &&
                vehicleSeat.NetworkpassengerBodyObject != null &&
                vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == currentObject.GetInstanceID();

            var targetAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject);

            if (currentIsPhysicallyInSeat)
            {

                if (currentObject != null)
                {
                    var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, currentObject);
                    if (currentState != null)
                    {
                        var stateData = BaggedObjectPatches.LoadObjectState(bagController, currentObject) ?? new Core.BaggedObjectStateData();
                        if (stateData.targetObject == null) stateData.CalculateFromObject(currentObject, bagController);
                        stateData.CaptureBreakoutStateFromBaggedObject(currentState);
                        BaggedObjectPatches.SaveObjectState(bagController, currentObject, stateData);
                    }
                }

                if (currentObject != null)
                {
                    BaggedObjectStatePatches.BaggedObject_OnExit.MarkPreserveOverridesDuringCycling(currentObject);
                }

                vehicleSeat.EjectPassenger(currentObject);
                if (currentObject != null)
                {
                    BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                }
                if (targetAdditionalSeat != null)
                {

                    var timer = targetObject.GetComponent<AdditionalSeatBreakoutTimer>();
                    if (timer != null)
                    {
                        var timerState = BaggedObjectPatches.LoadObjectState(bagController, targetObject) ?? new Core.BaggedObjectStateData();
                        if (timerState.targetObject == null)
                        {
                            timerState.CalculateFromObject(targetObject, bagController);
                        }
                        timerState.CaptureFromAdditionalTimer(timer);
                        BaggedObjectPatches.SaveObjectState(bagController, targetObject, timerState);
                    }

                    targetAdditionalSeat.EjectPassenger(targetObject);
                    localSeatDict.TryRemove(targetObject, out _);
                    if (currentObject != null)
                    {
                        BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                        localSeatDict[currentObject] = targetAdditionalSeat;
                    }
                    BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                    targetAdditionalSeat.AssignPassenger(currentObject);

                    if (currentObject != null && UnityEngine.Networking.NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(currentObject) && !currentObject.GetComponent<AdditionalSeatBreakoutTimer>())
                    {
                        var swapTimer = currentObject.AddComponent<AdditionalSeatBreakoutTimer>();
                        swapTimer.controller = bagController;
                        float mass = bagController.CalculateBaggedObjectMass(currentObject);
                        float baseTime = 10f;
                        float multiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                        float ft = Mathf.Max(baseTime - 0.005f * mass, 1f);
                        var cb = currentObject.GetComponent<CharacterBody>();
                        if (cb && cb.isElite) ft *= 0.8f;
                        swapTimer.breakoutTime = ft * multiplier;
                        var ss = BaggedObjectPatches.LoadObjectState(bagController, currentObject);
                        if (ss != null)
                        {
                            if (ss.breakoutTime > 0f) swapTimer.breakoutTime = ss.breakoutTime;
                            swapTimer.SetElapsedBreakoutTime(ss.elapsedBreakoutTime);
                            swapTimer.breakoutAttempts = ss.breakoutAttempts;
                        }
                    }
                }
                if (targetAdditionalSeat == null)
                {
                    var newSeat = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict, true);
                    if (newSeat != null && currentObject != null)
                    {
                        localSeatDict[currentObject] = newSeat;
                        BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                        newSeat.AssignPassenger(currentObject);
                    }
                }

                BagPatches.SetMainSeatObject(bagController, targetObject);
                BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;
                vehicleSeat.AssignPassenger(targetObject);

                if (targetObject != null)
                {
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (storedState != null)
                    {
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

                if (targetAdditionalSeat != null)
                {

                    var timer = targetObject.GetComponent<AdditionalSeatBreakoutTimer>();
                    if (timer != null)
                    {
                        var timerState = BaggedObjectPatches.LoadObjectState(bagController, targetObject) ?? new Core.BaggedObjectStateData();
                        if (timerState.targetObject == null)
                        {
                            timerState.CalculateFromObject(targetObject, bagController);
                        }
                        timerState.CaptureFromAdditionalTimer(timer);
                        BaggedObjectPatches.SaveObjectState(bagController, targetObject, timerState);
                    }

                    targetAdditionalSeat.EjectPassenger(targetObject);
                    localSeatDict.TryRemove(targetObject, out _);
                    if (currentObject != null)
                    {
                        localSeatDict[currentObject] = targetAdditionalSeat;
                        BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                        targetAdditionalSeat.AssignPassenger(currentObject);

                        if (UnityEngine.Networking.NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(currentObject) && !currentObject.GetComponent<AdditionalSeatBreakoutTimer>())
                        {
                            var swapTimer = currentObject.AddComponent<AdditionalSeatBreakoutTimer>();
                            swapTimer.controller = bagController;
                            float mass = bagController.CalculateBaggedObjectMass(currentObject);
                            float baseTime = 10f;
                            float multiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                            float ft = Mathf.Max(baseTime - 0.005f * mass, 1f);
                            var cb = currentObject.GetComponent<CharacterBody>();
                            if (cb && cb.isElite) ft *= 0.8f;
                            swapTimer.breakoutTime = ft * multiplier;
                            var ss = BaggedObjectPatches.LoadObjectState(bagController, currentObject);
                            if (ss != null)
                            {
                                if (ss.breakoutTime > 0f) swapTimer.breakoutTime = ss.breakoutTime;
                                swapTimer.SetElapsedBreakoutTime(ss.elapsedBreakoutTime);
                                swapTimer.breakoutAttempts = ss.breakoutAttempts;
                            }
                        }
                    }
                }

                BagPatches.SetMainSeatObject(bagController, targetObject);
                if (targetObject != null)
                {
                    localSeatDict.TryRemove(targetObject, out _);
                    BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                    vehicleSeat.AssignPassenger(targetObject);

                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                }
                BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
        }
    }
}
