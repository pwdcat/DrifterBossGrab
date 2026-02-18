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
    // Handles cycling logic for bag passengers - manages server-side and client-side cycling operations
    public static class PassengerCycler
    {
        // Cycles through passengers in the bag by the specified amount - routes to server or handles locally based on network state
        public static void CyclePassengers(DrifterBagController bagController, int amount)
        {
            // Only allow cycling when BottomlessBag feature is enabled
            if (!FeatureState.IsCyclingEnabled)
            {
                return;
            }
            if (bagController == null || amount == 0) return;

            // Prevent scrolling if capacity is 1 or less
            if (BagCapacityCalculator.GetUtilityMaxStock(bagController) <= 1) return;

            // If we are on client and have authority, send a command to server
            if (!NetworkServer.active && bagController.hasAuthority)
            {

                // Use CycleNetworkHandler for client-to-server communication
                Networking.CycleNetworkHandler.SendCycleRequest(bagController, amount);
                return;
            }

            // If we are the server, perform the cycle directly
            if (NetworkServer.active)
            {
                ServerCyclePassengers(bagController, amount);
            }
        }

        // Server-side implementation of cycling - called from CycleNetworkHandler or directly on host
        public static void ServerCyclePassengers(DrifterBagController bagController, int amount)
        {
            // Only allow cycling when BottomlessBag feature is enabled
            if (!FeatureState.IsCyclingEnabled)
            {
                return;
            }
            if (!NetworkServer.active || amount == 0) return; // Safety guard

            if (bagController.vehicleSeat == null)
            {
                Log.Info($" [BottomlessBag] ERROR: vehicleSeat is null!");
                return;
            }

            List<GameObject> baggedObjects = BagPatches.GetState(bagController).BaggedObjects;
            if (baggedObjects == null || baggedObjects.Count == 0)
            {
                return;
            }

            var seenInstanceIds = new HashSet<int>();
            var validObjects = new List<GameObject>();
            var allObjectsInScene = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var potentialRegrabObjects = new List<GameObject>();
            foreach (var sceneObj in allObjectsInScene)
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
                    if (wasPreviouslyTracked && !OtherPatches.IsInProjectileState(sceneObj))
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
                bool isInProjectileState = OtherPatches.IsInProjectileState(obj);
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

        // Private method to cycle passengers by amount on all authoritative bag controllers
        private static void CyclePassengers(int amount)
        {
            if (amount == 0) return;
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);

            foreach (var bagController in bagControllers)
            {

                if (!bagController.isAuthority)
                {
                    continue;
                }

                CyclePassengers(bagController, amount);
                break;
            }
        }

        // Cycles to the next object in the valid objects list by the specified amount - handles all seat transitions and state updates
        private static void CycleToNextObject(DrifterBagController bagController, List<GameObject> validObjects, int amount)
        {
            // Use a local copy of the seatDict for atomic updates
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
                        var list = BagPatches.GetState(bagController).BaggedObjects;
                        int passengerInstanceId = seatPassenger.GetInstanceID();
                        bool alreadyTracked = false;
                        foreach (var existingObj in list)
                        {
                            if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                            {
                                alreadyTracked = true;
                                break;
                            }
                        }
                        if (!alreadyTracked)
                        {
                            list.Add(seatPassenger);
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
                if (!mainPassengerStillValid && OtherPatches.IsInProjectileState(mainPassenger))
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
                // For client-grabbed objects, the server may not see them in the vehicle seat
                // Trust the mainSeatDict tracking if the object is in validObjects
                if (actualMainPassenger == null)
                {
                    // Check if mainPassenger is in validObjects
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[CycleToNextObject] mainPassenger {mainPassenger.name} not in validObjects and not in seat, returning early");
                        return;
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CycleToNextObject] Trusting mainSeatDict for {mainPassenger.name} (client-grabbed object)");
                    }
                }
            }
            int emptySeatsCount = 0;
            foreach (var kvp in localSeatDict)
            {
                if (kvp.Value != null && !kvp.Value.hasPassenger)
                {
                    emptySeatsCount++;
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == bagController.vehicleSeat) continue;
                bool isTracked = localSeatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    emptySeatsCount++;
                }
            }
            // Determine the logical selection state
            // This is the source of truth for "where are we in the cycle" regardless of physical seat state
            bool isInNullState = actualMainPassenger == null && validObjects.Count > 0;

            int totalPositions = validObjects.Count + 1;
            if (actualMainPassenger == null)
            {
                if (validObjects.Count == 0)
                {
                    return;
                }
            }

            // Only fall back to seat passenger if the logical state is also null AND we REALLY have someone in the seat
            // This usually happens during the very first grab of a run or after a scene transition
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
                        var list = BagPatches.GetState(bagController).BaggedObjects;
                        int passengerInstanceId = seatPassenger.GetInstanceID();
                        bool alreadyTracked = false;
                        foreach (var existingObj in list)
                        {
                            if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                            {
                                alreadyTracked = true;
                                break;
                            }
                        }
                        if (!alreadyTracked)
                        {
                            list.Add(seatPassenger);
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
            bool nextIsNull = (nextIndex == validObjects.Count);

            // Check if bag is full (no empty slots)
            int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
            bool isBagFull = validObjects.Count >= effectiveCapacity;

            int direction = Math.Sign(amount);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CycleToNextObject] Index Calc: Current={currentIndex} (IsNull={currentIsNull}), Amount={amount}, Next={nextIndex} (IsNull={nextIsNull}), TotalPos={totalPositions}, IsBagFull={isBagFull}");
            }

            // If bag is full and we're trying to go to null state, skip null state and wrap around
            // This must be checked BEFORE any other logic to prevent early returns
            if (isBagFull && nextIsNull)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[CycleToNextObject] Bag is full, skipping null state and wrapping around");

                // Skip the null state and go to the next/previous valid object
                nextIndex = (direction > 0) ? 0 : validObjects.Count - 1;
                nextIsNull = false;

                // Skip the null state handling block entirely
                // Fall through to the swap logic below
            }

            bool hasValidSeatConfiguration = SeatValidator.ValidateSeatConfiguration(bagController, validObjects, actualMainPassenger, isInNullState, localSeatDict);
            if (!hasValidSeatConfiguration)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleToNextObject] Invalid Seat Conf, Aborting.");
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
