#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class PassengerCycler
    {

        private static readonly HashSet<int> _seenInstanceIdsBuffer = new HashSet<int>();
        private static readonly List<GameObject> _validObjectsBuffer = new List<GameObject>();
        private static readonly List<GameObject> _potentialRegrabObjectsBuffer = new List<GameObject>();

        public static void CyclePassengers(DrifterBagController bagController, int amount)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                return;
            }
            if (bagController == null || amount == 0) return;

            if (BagCapacityCalculator.GetUtilityMaxStock(bagController) <= 1) return;

            if (!NetworkServer.active && bagController.hasAuthority)
            {
                Networking.CycleNetworkHandler.SendCycleRequest(bagController, amount);
                return;
            }

            if (NetworkServer.active)
            {
                ServerCyclePassengers(bagController, amount);
            }
        }

        public static void ServerCyclePassengers(DrifterBagController bagController, int amount)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                return;
            }
            if (!NetworkServer.active || amount == 0) return;

            if (bagController.vehicleSeat == null)
            {
                Log.Debug($" [BottomlessBag] ERROR: vehicleSeat is null!");
                return;
            }

            List<GameObject> baggedObjects = BagPatches.GetState(bagController).BaggedObjects;
            if (baggedObjects == null || baggedObjects.Count == 0)
            {
                return;
            }

            var seenInstanceIds = _seenInstanceIdsBuffer;
            seenInstanceIds.Clear();
            var validObjects = _validObjectsBuffer;
            validObjects.Clear();
            var potentialRegrabObjects = _potentialRegrabObjectsBuffer;
            potentialRegrabObjects.Clear();
            foreach (var sceneObj in SpecialObjectAttributesPatches.RegisteredObjects)
            {
                if (sceneObj != null && PluginConfig.IsGrabbable(sceneObj))
                {
                    bool wasPreviouslyTracked = false;
                    foreach (var trackedObj in baggedObjects)
                    {
                        if (trackedObj != null && trackedObj.GetInstanceID() == sceneObj.GetInstanceID())
                        {
                            wasPreviouslyTracked = true;
                            break;
                        }
                    }
                    if (wasPreviouslyTracked && !ProjectileRecoveryPatches.IsInProjectileState(sceneObj))
                    {
                        potentialRegrabObjects.Add(sceneObj);
                    }
                }
            }
            foreach (var obj in baggedObjects)
            {
                if (obj == null)
                {
                    continue;
                }
                bool isInProjectileState = ProjectileRecoveryPatches.IsInProjectileState(obj);
                if (isInProjectileState)
                {

                    continue;
                }
                int instanceId = obj.GetInstanceID();
                if (!seenInstanceIds.Contains(instanceId))
                {
                    seenInstanceIds.Add(instanceId);
                    validObjects.Add(obj);
                }

            }
            foreach (var regrabObj in potentialRegrabObjects)
            {
                int instanceId = regrabObj.GetInstanceID();
                if (!seenInstanceIds.Contains(instanceId))
                {
                    seenInstanceIds.Add(instanceId);
                    validObjects.Add(regrabObj);
                }
            }
            if (validObjects.Count == 0)
            {

                return;
            }

            CycleToNextObject(bagController, validObjects, amount);
        }

        private static void CycleToNextObject(DrifterBagController bagController, List<GameObject> validObjects, int amount)
        {

            ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict;
            var existingSeatDict = BagPatches.GetState(bagController).AdditionalSeats;
            localSeatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>(existingSeatDict);

            var vehicleSeat = bagController.vehicleSeat;
            GameObject? mainPassenger = BagPatches.GetMainSeatObject(bagController);
            if (mainPassenger == null && vehicleSeat.hasPassenger)
            {
                GameObject? seatPassenger = null;
                if (vehicleSeat.hasPassenger)
                {
                    seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                }
                if (seatPassenger != null)
                {
                    bool shouldTrack = false;
                    foreach (var obj in validObjects)
                    {
                        if (obj != null && obj.GetInstanceID() == seatPassenger.GetInstanceID())
                        {
                            shouldTrack = true;
                            break;
                        }
                    }
                    if (shouldTrack)
                    {
                        var state = BagPatches.GetState(bagController);
                        var list = state.BaggedObjects;
                        int passengerInstanceId = seatPassenger.GetInstanceID();
                        if (!state.ContainsInstanceId(passengerInstanceId))
                        {
                            list.Add(seatPassenger);
                            state.AddInstanceId(passengerInstanceId);
                        }
                        BagPatches.SetMainSeatObject(bagController, seatPassenger);
                        BagCarouselUpdater.UpdateCarousel(bagController, 0);
                        mainPassenger = seatPassenger;
                    }
                }
            }
            if (mainPassenger == null && vehicleSeat.hasPassenger)
            {
                var seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                bool shouldTrack = false;
                foreach (var obj in validObjects)
                {
                    if (obj != null && obj.GetInstanceID() == seatPassenger.GetInstanceID())
                    {
                        shouldTrack = true;
                        break;
                    }
                }
                if (shouldTrack)
                {
                    BagPatches.SetMainSeatObject(bagController, seatPassenger);
                    BagCarouselUpdater.UpdateCarousel(bagController, 0);
                    mainPassenger = seatPassenger;
                }
            }
            if (mainPassenger != null)
            {
                bool isActuallyInMainSeat = false;
                bool isActuallyInAdditionalSeat = false;
                if (vehicleSeat.hasPassenger)
                {
                    if (vehicleSeat.hasPassenger && vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == mainPassenger.GetInstanceID())
                    {
                        isActuallyInMainSeat = true;
                    }
                }
                if (localSeatDict.Count > 0)
                {
                    foreach (var kvp in localSeatDict)
                    {
                        if (kvp.Value != null && kvp.Value.hasPassenger)
                        {
                            if (kvp.Value.NetworkpassengerBodyObject.GetInstanceID() == mainPassenger.GetInstanceID())
                            {
                                isActuallyInAdditionalSeat = true;
                                break;
                            }
                        }
                    }
                }
                if (!isActuallyInMainSeat && isActuallyInAdditionalSeat)
                {
                    BagPatches.SetMainSeatObject(bagController, null);
                    mainPassenger = null;
                }
            }
            if (mainPassenger != null)
            {
                bool mainPassengerStillValid = false;
                int mainPassengerInstanceId = mainPassenger.GetInstanceID();
                foreach (var obj in validObjects)
                {
                    if (obj != null && obj.GetInstanceID() == mainPassengerInstanceId)
                    {
                        mainPassengerStillValid = true;
                        break;
                    }
                }
                if (!mainPassengerStillValid && ProjectileRecoveryPatches.IsInProjectileState(mainPassenger))
                {
                    mainPassengerStillValid = false;
                }
                if (!mainPassengerStillValid)
                {
                    BagPatches.SetMainSeatObject(bagController, null);
                    BagCarouselUpdater.UpdateCarousel(bagController, 0);
                    mainPassenger = null;
                }
            }
            GameObject? actualMainPassenger = null;
            int actualMainPassengerInstanceId = mainPassenger?.GetInstanceID() ?? 0;
            foreach (var obj in validObjects)
            {
                if (obj != null && obj.GetInstanceID() == actualMainPassengerInstanceId && actualMainPassengerInstanceId != 0)
                {
                    actualMainPassenger = obj;
                    break;
                }
            }
            if (actualMainPassenger == null && mainPassenger != null)
            {
                if (vehicleSeat.hasPassenger)
                {
                    GameObject? seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                    if (seatPassenger != null && seatPassenger.GetInstanceID() == actualMainPassengerInstanceId)
                    {
                        actualMainPassenger = mainPassenger;
                    }
                }
                if (actualMainPassenger == null)
                {
                    bool isInValidObjects = false;
                    int mpInstanceId = mainPassenger.GetInstanceID();
                    foreach (var obj in validObjects)
                    {
                        if (obj != null && obj.GetInstanceID() == mpInstanceId)
                        {
                            isInValidObjects = true;
                            actualMainPassenger = obj;
                            break;
                        }
                    }

                    if (!isInValidObjects)
                    {
                        Log.Debug($"[CycleToNextObject] mainPassenger {mainPassenger.name} not in validObjects and not in seat, returning early");
                        return;
                    }
                    else
                    {
                        Log.Debug($"[CycleToNextObject] Trusting mainSeatDict for {mainPassenger.name} (client-grabbed object)");
                    }
                }
            }

            bool isInNullState = actualMainPassenger == null && validObjects.Count > 0;

            int totalPositions = validObjects.Count + 1;
            if (actualMainPassenger == null)
            {
                if (validObjects.Count == 0)
                {
                    return;
                }
            }

            if (actualMainPassenger == null && !isInNullState && vehicleSeat.hasPassenger)
            {
                GameObject? seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                if (seatPassenger != null)
                {
                    bool shouldTrack = false;
                    foreach (var obj in validObjects)
                    {
                        if (obj != null && obj.GetInstanceID() == seatPassenger.GetInstanceID())
                        {
                            shouldTrack = true;
                            break;
                        }
                    }
                    if (shouldTrack)
                    {
                        var state = BagPatches.GetState(bagController);
                        var list = state.BaggedObjects;
                        int passengerInstanceId = seatPassenger.GetInstanceID();
                        if (!state.ContainsInstanceId(passengerInstanceId))
                        {
                            list.Add(seatPassenger);
                            state.AddInstanceId(passengerInstanceId);
                        }
                        BagPatches.SetMainSeatObject(bagController, seatPassenger);
                        BagCarouselUpdater.UpdateCarousel(bagController, 0);
                        actualMainPassenger = seatPassenger;
                    }
                }
            }

            int currentIndex = -1;
            bool currentIsNull = false;
            if (isInNullState)
            {
                currentIndex = validObjects.Count;
                currentIsNull = true;
            }
            else
            {
                for (int i = 0; i < validObjects.Count; i++)
                {
                    if (validObjects[i] != null && actualMainPassenger != null && validObjects[i].GetInstanceID() == actualMainPassenger.GetInstanceID())
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            if (currentIndex < 0 && !currentIsNull)
            {
                currentIndex = validObjects.Count;
                currentIsNull = true;
            }
            int nextIndex = (currentIndex + amount) % totalPositions;
            if (nextIndex < 0) nextIndex += totalPositions;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                var target = (nextIndex >= 0 && nextIndex < validObjects.Count) ? validObjects[nextIndex] : null;
                Log.Debug($"[PassengerCycler] User scrolled to index {nextIndex}. Target={(target != null ? target.name : "null")}");
            }

            bool nextIsNull = (nextIndex == validObjects.Count);

            int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
            bool isBagFull = validObjects.Count >= effectiveCapacity;

            int direction = Math.Sign(amount);

            Log.Debug($"[CycleToNextObject] Index Calc: Current={currentIndex} (IsNull={currentIsNull}), Amount={amount}, Next={nextIndex} (IsNull={nextIsNull}), TotalPos={totalPositions}, IsBagFull={isBagFull}");

            if (isBagFull && nextIsNull)
            {
                Log.Debug($"[CycleToNextObject] Bag is full, skipping null state and wrapping around");

                nextIndex = (direction > 0) ? 0 : validObjects.Count - 1;
                nextIsNull = false;
            }

            BagPatches.GetState(bagController).IntendedSelectedIndex = nextIndex;

            bool hasValidSeatConfiguration = SeatValidator.ValidateSeatConfiguration(bagController, validObjects, actualMainPassenger, isInNullState, localSeatDict);
            if (!hasValidSeatConfiguration)
            {
                Log.Debug($"[CycleToNextObject] Invalid Seat Conf, Aborting.");
                return;
            }
            DrifterBossGrabPlugin._isSwappingPassengers = true;
            try
            {
                if (nextIsNull)
                {
                    if (!currentIsNull && actualMainPassenger != null)
                    {
                        SeatTransitionHandler.HandleNullStateTransition(bagController, vehicleSeat, actualMainPassenger, localSeatDict, validObjects.Count);
                    }
                    else
                    {
                        nextIsNull = false;
                        nextIndex = 0;
                    }
                }
                else if (!nextIsNull && currentIsNull)
                {
                    var targetObject = validObjects[nextIndex];
                    SeatTransitionHandler.HandleNullToObjectTransition(bagController, vehicleSeat, targetObject, localSeatDict);
                }
                else
                {
                    var currentObject = validObjects[currentIndex];
                    var targetObject = validObjects[nextIndex];
                    SeatTransitionHandler.HandleObjectSwap(bagController, vehicleSeat, currentObject, targetObject, localSeatDict, direction);
                }

                BagPatches.GetState(bagController).AdditionalSeats = localSeatDict;

                BagCarouselUpdater.UpdateCarousel(bagController, direction);
                BagCarouselUpdater.UpdateNetworkBagState(bagController, direction);
            }
            finally
            {
                DrifterBossGrabPlugin._isSwappingPassengers = false;
            }
            if (!nextIsNull)
            {
                var targetObject = nextIndex < validObjects.Count ? validObjects[nextIndex] : null;
                if (targetObject != null)
                {

                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                }
            }
            BagPassengerManager.ForceRecalculateMass(bagController);
        }
    }
}
