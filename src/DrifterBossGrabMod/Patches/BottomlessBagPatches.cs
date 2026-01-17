using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
namespace DrifterBossGrabMod.Patches
{
    public static class BottomlessBagPatches
    {
        private static float _lastCycleTime = 0f;
        private const float CYCLE_COOLDOWN = 0.1f; // 100ms cooldown to prevent spamming
        public static void HandleInput()
        {
            if (PluginConfig.BottomlessBagEnabled.Value)
            {
                float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
                if (scrollDelta != 0f && Time.time >= _lastCycleTime + CYCLE_COOLDOWN)
                {
                    _lastCycleTime = Time.time;
                    CyclePassengers(scrollDelta > 0f);
                }
            }
        }
        private static void CyclePassengers(bool scrollUp)
        {
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bagController in bagControllers)
            {
                if (!bagController.isAuthority)
                {
                    continue;
                }
                if (bagController.vehicleSeat == null)
                {
                    Log.Info($" [BottomlessBag] ERROR: vehicleSeat is null!");
                    continue;
                }
                if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out var baggedObjects))
                {
                    continue;
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
                    continue;
                }
                CycleToNextObject(bagController, validObjects, scrollUp);
                break;
            }
        }
        private static void CycleToNextObject(DrifterBagController bagController, List<GameObject> validObjects, bool scrollUp)
        {
            // Use a local copy of the seatDict for atomic updates
            Dictionary<GameObject, RoR2.VehicleSeat> localSeatDict;
            if (!BagPatches.additionalSeatsDict.TryGetValue(bagController, out var existingSeatDict))
            {
                localSeatDict = new Dictionary<GameObject, RoR2.VehicleSeat>();
            }
            else
            {
                localSeatDict = new Dictionary<GameObject, RoR2.VehicleSeat>(existingSeatDict);
            }

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
                        if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                        {
                            list = new List<GameObject>();
                            BagPatches.baggedObjectsDict[bagController] = list;
                        }
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
                    mainPassenger = null;
                }
            }
            GameObject actualMainPassenger = null;
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
                    return;
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
                bool isTracked = localSeatDict.ContainsValue(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    emptySeatsCount++;
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
            if (actualMainPassenger == null && vehicleSeat.hasPassenger)
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
                        if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                        {
                            list = new List<GameObject>();
                            BagPatches.baggedObjectsDict[bagController] = list;
                        }
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
                    if (validObjects[i] != null && validObjects[i].GetInstanceID() == actualMainPassenger.GetInstanceID())
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
            int direction = scrollUp ? 1 : -1;
            int nextIndex = currentIndex + direction;
            if (nextIndex >= totalPositions) nextIndex = 0;
            if (nextIndex < 0) nextIndex = totalPositions - 1;
            bool nextIsNull = nextIndex == validObjects.Count;
            bool hasValidSeatConfiguration = ValidateSeatConfiguration(bagController, validObjects, actualMainPassenger, isInNullState, localSeatDict);
            if (!hasValidSeatConfiguration)
            {
                return;
            }
            if (nextIsNull)
            {
                if (!currentIsNull && actualMainPassenger != null)
                {
                    if (!HasSpaceForNullStateTransition(bagController, validObjects.Count, localSeatDict))
                    {
                        return;
                    }
                    DrifterBossGrabPlugin._isSwappingPassengers = true;
                    if (!ValidateNullStateTransition(bagController, actualMainPassenger, localSeatDict))
                    {
                        DrifterBossGrabPlugin._isSwappingPassengers = false;
                        return;
                    }
                    var seatForCurrent = FindOrCreateEmptySeat(bagController, ref localSeatDict);
                    vehicleSeat.EjectPassenger(actualMainPassenger);
                    if (actualMainPassenger != null)
                    {
                        BagPatches.mainSeatDict.Remove(bagController);
                    }
                    BaggedObjectPatches.RemoveUIOverlay(actualMainPassenger);
                    BagPatches.SetMainSeatObject(bagController, null);
                    if (seatForCurrent != null)
                    {
                        seatForCurrent.AssignPassenger(actualMainPassenger);
                        localSeatDict[actualMainPassenger] = seatForCurrent;
                    }
                    if (actualMainPassenger != null)
                    {
                        BagPatches.mainSeatDict.Remove(bagController);
                    }
                    BaggedObjectPatches.RemoveUIOverlayForNullState(bagController);
                    DrifterBossGrabPlugin._isSwappingPassengers = false;
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
                if (targetObject == null)
                {
                    return;
                }
                DrifterBossGrabPlugin._isSwappingPassengers = true;
                var sourceAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject, localSeatDict);
                if (sourceAdditionalSeat != null)
                {
                    sourceAdditionalSeat.EjectPassenger(targetObject);
                    localSeatDict.Remove(targetObject);
                }
                bagController.AssignPassenger(targetObject);
                BagPatches.SetMainSeatObject(bagController, targetObject);
                BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                DrifterBossGrabPlugin._isSwappingPassengers = false;
            }
            else
            {
                var currentObject = validObjects[currentIndex];
                var targetObject = validObjects[nextIndex];
                if (targetObject == null)
                {
                    return;
                }
                if (!ValidateSeatStateForSwap(bagController, currentObject, targetObject, localSeatDict))
                {
                    DrifterBossGrabPlugin._isSwappingPassengers = false;
                    return;
                }
                DrifterBossGrabPlugin._isSwappingPassengers = true;
                var targetAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject);
                vehicleSeat.EjectPassenger(currentObject);
                if (currentObject != null)
                {
                    BagPatches.mainSeatDict.Remove(bagController);
                }
                BaggedObjectPatches.RemoveUIOverlay(currentObject);
                if (targetAdditionalSeat != null)
                {
                    targetAdditionalSeat.EjectPassenger(targetObject);
                    localSeatDict.Remove(targetObject);
                    targetAdditionalSeat.AssignPassenger(currentObject);
                    BagPatches.SetMainSeatObject(bagController, null);
                    BaggedObjectPatches.RemoveUIOverlay(currentObject);
                    localSeatDict[currentObject] = targetAdditionalSeat;
                }
                if (targetAdditionalSeat == null)
                {
                    int totalSeatsCount = 0;
                    if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDictForCheck))
                    {
                        totalSeatsCount = seatDictForCheck.Count;
                    }
                    if (validObjects.Count == 1 && localSeatDict.Count == 0)
                    {
                        BagPatches.SetMainSeatObject(bagController, null);
                        DrifterBossGrabPlugin._isSwappingPassengers = false;
                        BagPatches.RemoveBaggedObject(bagController, currentObject);
                        return;
                    }
                    var seatForCurrent = FindOrCreateEmptySeat(bagController, ref localSeatDict);
                    if (seatForCurrent != null)
                    {
                        seatForCurrent.AssignPassenger(currentObject);
                        BagPatches.SetMainSeatObject(bagController, null);
                        BaggedObjectPatches.RemoveUIOverlay(currentObject);
                        localSeatDict[currentObject] = seatForCurrent;
                    }
                    else
                    {
                        Log.Warning($" [BottomlessBag] WARNING: No seat available for {currentObject.name}!");
                    }
                }
                bagController.AssignPassenger(targetObject);
                if (targetAdditionalSeat != null)
                {
                    localSeatDict.Remove(targetObject);
                }
                BagPatches.SetMainSeatObject(bagController, targetObject);
                BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                DrifterBossGrabPlugin._isSwappingPassengers = false;
            }
            // Update the global dict
            if (localSeatDict.Count == 0)
            {
                BagPatches.additionalSeatsDict.Remove(bagController);
            }
            else
            {
                BagPatches.additionalSeatsDict[bagController] = localSeatDict;
            }
        }
        private static bool ValidateSeatConfiguration(DrifterBagController bagController, List<GameObject> validObjects, GameObject? actualMainPassenger, bool isInNullState, Dictionary<GameObject, RoR2.VehicleSeat> seatDict)
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
        private static bool ValidateSeatStateForSwap(DrifterBagController bagController, GameObject? currentObject, GameObject? targetObject, Dictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            var mainSeat = bagController.vehicleSeat;
            if (mainSeat == null)
            {
                return false;
            }
            bool isActuallyInMainSeat = false;
            if (mainSeat.hasPassenger)
            {
                var actualMainPassenger = mainSeat.NetworkpassengerBodyObject;
                isActuallyInMainSeat = actualMainPassenger.GetInstanceID() == currentObject!.GetInstanceID();
            }
            if (!isActuallyInMainSeat)
            {
                return false;
            }
            var targetAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject, seatDict);
            if (targetAdditionalSeat != null)
            {
                bool seatHasCorrectPassenger = false;
                if (targetAdditionalSeat.hasPassenger)
                {
                    var actualTargetPassenger = targetAdditionalSeat.NetworkpassengerBodyObject;
                    if (actualTargetPassenger.GetInstanceID() == targetObject.GetInstanceID())
                    {
                        seatHasCorrectPassenger = true;
                    }
                }
            }
            else
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
                        if (seatPassenger.GetInstanceID() == targetObject.GetInstanceID() && seat == mainSeat)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        private static RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController, ref Dictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            var vehicleSeat = bagController.vehicleSeat;
            foreach (var kvp in seatDict)
            {
                if (kvp.Value != null && !kvp.Value.hasPassenger)
                {
                    return kvp.Value;
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == vehicleSeat) continue;
                bool isTracked = seatDict.ContainsValue(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    return childSeat;
                }
            }
            var seatObject = new GameObject($"AdditionalSeat_Empty_{DateTime.Now.Ticks}");
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            newSeat.seatPosition = vehicleSeat.seatPosition;
            newSeat.exitPosition = vehicleSeat.exitPosition;
            newSeat.ejectOnCollision = vehicleSeat.ejectOnCollision;
            newSeat.hidePassenger = vehicleSeat.hidePassenger;
            newSeat.exitVelocityFraction = vehicleSeat.exitVelocityFraction;
            newSeat.disablePassengerMotor = vehicleSeat.disablePassengerMotor;
            newSeat.isEquipmentActivationAllowed = vehicleSeat.isEquipmentActivationAllowed;
            newSeat.shouldProximityHighlight = vehicleSeat.shouldProximityHighlight;
            newSeat.disableInteraction = vehicleSeat.disableInteraction;
            newSeat.shouldSetIdle = vehicleSeat.shouldSetIdle;
            newSeat.additionalExitVelocity = vehicleSeat.additionalExitVelocity;
            newSeat.disableAllCollidersAndHurtboxes = vehicleSeat.disableAllCollidersAndHurtboxes;
            newSeat.disableColliders = vehicleSeat.disableColliders;
            newSeat.disableCharacterNetworkTransform = vehicleSeat.disableCharacterNetworkTransform;
            newSeat.ejectFromSeatOnMapEvent = vehicleSeat.ejectFromSeatOnMapEvent;
            newSeat.inheritRotation = vehicleSeat.inheritRotation;
            newSeat.holdPassengerAfterDeath = vehicleSeat.holdPassengerAfterDeath;
            newSeat.ejectPassengerToGround = vehicleSeat.ejectPassengerToGround;
            newSeat.ejectRayDistance = vehicleSeat.ejectRayDistance;
            newSeat.handleExitTeleport = vehicleSeat.handleExitTeleport;
            newSeat.setCharacterMotorPositionToCurrentPosition = vehicleSeat.setCharacterMotorPositionToCurrentPosition;
            newSeat.passengerState = vehicleSeat.passengerState;
            seatDict[null] = newSeat;
            return newSeat;
        }
        private static RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController)
        {
            var vehicleSeat = bagController.vehicleSeat;
            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && !kvp.Value.hasPassenger)
                    {
                        return kvp.Value;
                    }
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == vehicleSeat) continue;
                bool isTracked = seatDict != null && seatDict.ContainsValue(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    return childSeat;
                }
            }
            var seatObject = new GameObject($"AdditionalSeat_Empty_{DateTime.Now.Ticks}");
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            newSeat.seatPosition = vehicleSeat.seatPosition;
            newSeat.exitPosition = vehicleSeat.exitPosition;
            newSeat.ejectOnCollision = vehicleSeat.ejectOnCollision;
            newSeat.hidePassenger = vehicleSeat.hidePassenger;
            newSeat.exitVelocityFraction = vehicleSeat.exitVelocityFraction;
            newSeat.disablePassengerMotor = vehicleSeat.disablePassengerMotor;
            newSeat.isEquipmentActivationAllowed = vehicleSeat.isEquipmentActivationAllowed;
            newSeat.shouldProximityHighlight = vehicleSeat.shouldProximityHighlight;
            newSeat.disableInteraction = vehicleSeat.disableInteraction;
            newSeat.shouldSetIdle = vehicleSeat.shouldSetIdle;
            newSeat.additionalExitVelocity = vehicleSeat.additionalExitVelocity;
            newSeat.disableAllCollidersAndHurtboxes = vehicleSeat.disableAllCollidersAndHurtboxes;
            newSeat.disableColliders = vehicleSeat.disableColliders;
            newSeat.disableCharacterNetworkTransform = vehicleSeat.disableCharacterNetworkTransform;
            newSeat.ejectFromSeatOnMapEvent = vehicleSeat.ejectFromSeatOnMapEvent;
            newSeat.inheritRotation = vehicleSeat.inheritRotation;
            newSeat.holdPassengerAfterDeath = vehicleSeat.holdPassengerAfterDeath;
            newSeat.ejectPassengerToGround = vehicleSeat.ejectPassengerToGround;
            newSeat.ejectRayDistance = vehicleSeat.ejectRayDistance;
            newSeat.handleExitTeleport = vehicleSeat.handleExitTeleport;
            newSeat.setCharacterMotorPositionToCurrentPosition = vehicleSeat.setCharacterMotorPositionToCurrentPosition;
            newSeat.passengerState = vehicleSeat.passengerState;
            return newSeat;
        }
        private static RoR2.VehicleSeat GetAdditionalSeatForObject(DrifterBagController bagController, GameObject? obj, Dictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (obj == null) return null;
            if (seatDict.TryGetValue(obj, out var seat))
            {
                return seat;
            }
            return null;
        }
        private static RoR2.VehicleSeat GetAdditionalSeatForObject(DrifterBagController bagController, GameObject? obj)
        {
            if (obj == null) return null;
            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                if (seatDict.TryGetValue(obj, out var seat))
                {
                    return seat;
                }
            }
            return null;
        }
        private static RoR2.VehicleSeat GetOrCreateAdditionalSeat(DrifterBagController bagController, int seatIndex)
        {
            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                int currentIndex = 0;
                foreach (var kvp in seatDict)
                {
                    if (currentIndex == seatIndex)
                    {
                        return kvp.Value;
                    }
                    currentIndex++;
                }
            }
            var seatObject = new GameObject($"AdditionalSeat_Index_{seatIndex}_{DateTime.Now.Ticks}");
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            var mainSeat = bagController.vehicleSeat;
            newSeat.seatPosition = mainSeat.seatPosition;
            newSeat.exitPosition = mainSeat.exitPosition;
            newSeat.ejectOnCollision = mainSeat.ejectOnCollision;
            newSeat.hidePassenger = mainSeat.hidePassenger;
            newSeat.exitVelocityFraction = mainSeat.exitVelocityFraction;
            newSeat.disablePassengerMotor = mainSeat.disablePassengerMotor;
            newSeat.isEquipmentActivationAllowed = mainSeat.isEquipmentActivationAllowed;
            newSeat.shouldProximityHighlight = mainSeat.shouldProximityHighlight;
            newSeat.disableInteraction = mainSeat.disableInteraction;
            newSeat.shouldSetIdle = mainSeat.shouldSetIdle;
            newSeat.additionalExitVelocity = mainSeat.additionalExitVelocity;
            newSeat.disableAllCollidersAndHurtboxes = mainSeat.disableAllCollidersAndHurtboxes;
            newSeat.disableColliders = mainSeat.disableColliders;
            newSeat.disableCharacterNetworkTransform = mainSeat.disableCharacterNetworkTransform;
            newSeat.ejectFromSeatOnMapEvent = mainSeat.ejectFromSeatOnMapEvent;
            newSeat.inheritRotation = mainSeat.inheritRotation;
            newSeat.holdPassengerAfterDeath = mainSeat.holdPassengerAfterDeath;
            newSeat.ejectPassengerToGround = mainSeat.ejectPassengerToGround;
            newSeat.ejectRayDistance = mainSeat.ejectRayDistance;
            newSeat.handleExitTeleport = mainSeat.handleExitTeleport;
            newSeat.setCharacterMotorPositionToCurrentPosition = mainSeat.setCharacterMotorPositionToCurrentPosition;
            newSeat.passengerState = mainSeat.passengerState;
            return newSeat;
        }
        private static void PerformPassengerCycle(DrifterBagController bagController, GameObject currentMain, GameObject newMain)
        {
            var vehicleSeat = bagController.vehicleSeat;
            var additionalSeats = BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict) ? seatDict : null;
            RoR2.VehicleSeat? currentAdditionalSeat = null;
            if (additionalSeats != null && additionalSeats.TryGetValue(currentMain, out var seat))
            {
                currentAdditionalSeat = seat;
            }
            vehicleSeat.EjectPassenger(currentMain);
            vehicleSeat.AssignPassenger(newMain);
            if (currentAdditionalSeat != null)
            {
                currentAdditionalSeat.AssignPassenger(currentMain);
            }
            else if (additionalSeats != null)
            {
                var seatObject = new GameObject($"AdditionalSeat_Cycled_{DateTime.Now.Ticks}");
                seatObject.transform.SetParent(bagController.transform);
                seatObject.transform.localPosition = Vector3.zero;
                seatObject.transform.localRotation = Quaternion.identity;
                var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
                newSeat.seatPosition = vehicleSeat.seatPosition;
                newSeat.exitPosition = vehicleSeat.exitPosition;
                newSeat.ejectOnCollision = vehicleSeat.ejectOnCollision;
                newSeat.hidePassenger = vehicleSeat.hidePassenger;
                newSeat.exitVelocityFraction = vehicleSeat.exitVelocityFraction;
                newSeat.disablePassengerMotor = vehicleSeat.disablePassengerMotor;
                newSeat.isEquipmentActivationAllowed = vehicleSeat.isEquipmentActivationAllowed;
                newSeat.shouldProximityHighlight = vehicleSeat.shouldProximityHighlight;
                newSeat.disableInteraction = vehicleSeat.disableInteraction;
                newSeat.shouldSetIdle = vehicleSeat.shouldSetIdle;
                newSeat.additionalExitVelocity = vehicleSeat.additionalExitVelocity;
                newSeat.disableAllCollidersAndHurtboxes = vehicleSeat.disableAllCollidersAndHurtboxes;
                newSeat.disableColliders = vehicleSeat.disableColliders;
                newSeat.disableCharacterNetworkTransform = vehicleSeat.disableCharacterNetworkTransform;
                newSeat.ejectFromSeatOnMapEvent = vehicleSeat.ejectFromSeatOnMapEvent;
                newSeat.inheritRotation = vehicleSeat.inheritRotation;
                newSeat.holdPassengerAfterDeath = vehicleSeat.holdPassengerAfterDeath;
                newSeat.ejectPassengerToGround = vehicleSeat.ejectPassengerToGround;
                newSeat.ejectRayDistance = vehicleSeat.ejectRayDistance;
                newSeat.handleExitTeleport = vehicleSeat.handleExitTeleport;
                newSeat.setCharacterMotorPositionToCurrentPosition = vehicleSeat.setCharacterMotorPositionToCurrentPosition;
                newSeat.passengerState = vehicleSeat.passengerState;
                newSeat.AssignPassenger(currentMain);
                additionalSeats[currentMain] = newSeat;
            }
        }
        private static bool ValidateNullStateTransition(DrifterBagController bagController, GameObject? currentObject, Dictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            var mainSeat = bagController.vehicleSeat;
            if (mainSeat == null)
            {
                return false;
            }
            bool isActuallyInMainSeat = false;
            if (mainSeat.hasPassenger)
            {
                var actualMainPassenger = mainSeat.NetworkpassengerBodyObject;
                isActuallyInMainSeat = actualMainPassenger.GetInstanceID() == currentObject.GetInstanceID();
            }
            if (!isActuallyInMainSeat)
            {
                return false;
            }
            var availableSeat = FindOrCreateEmptySeat(bagController, ref seatDict);
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
        private static bool HasSpaceForNullStateTransition(DrifterBagController bagController, int currentObjectCount, Dictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            int effectiveCapacity = BagPatches.GetUtilityMaxStock(bagController);
            if (currentObjectCount >= effectiveCapacity)
            {
                return false;
            }
            int emptySeatsCount = 0;
            foreach (var kvp in seatDict)
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
                bool isTracked = seatDict.ContainsValue(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    emptySeatsCount++;
                }
            }
            if (emptySeatsCount == 0)
            {
                return false;
            }
            return true;
        }
    }
}