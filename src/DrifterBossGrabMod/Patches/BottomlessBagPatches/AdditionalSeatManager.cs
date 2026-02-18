using System;
using System.Collections.Concurrent;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    // Manages additional seats for the bottomless bag feature - provides methods for finding, creating, and configuring additional seats
    public static class AdditionalSeatManager
    {
        // Copies properties from a source seat to a target seat
        public static void CopySeatProperties(RoR2.VehicleSeat source, RoR2.VehicleSeat target)
        {
            if (source == null || target == null) return;
            target.seatPosition = source.seatPosition;
            target.exitPosition = source.exitPosition;
            target.ejectOnCollision = source.ejectOnCollision;
            target.hidePassenger = source.hidePassenger;
            target.exitVelocityFraction = source.exitVelocityFraction;
            target.disablePassengerMotor = source.disablePassengerMotor;
            target.isEquipmentActivationAllowed = source.isEquipmentActivationAllowed;
            target.shouldProximityHighlight = source.shouldProximityHighlight;
            target.disableInteraction = source.disableInteraction;
            target.shouldSetIdle = source.shouldSetIdle;
            target.additionalExitVelocity = source.additionalExitVelocity;
            target.disableAllCollidersAndHurtboxes = source.disableAllCollidersAndHurtboxes;
            target.disableColliders = source.disableColliders;
            target.disableCharacterNetworkTransform = source.disableCharacterNetworkTransform;
            target.ejectFromSeatOnMapEvent = source.ejectFromSeatOnMapEvent;
            target.inheritRotation = source.inheritRotation;
            target.holdPassengerAfterDeath = source.holdPassengerAfterDeath;
            target.ejectPassengerToGround = source.ejectPassengerToGround;
            target.ejectRayDistance = source.ejectRayDistance;
            target.handleExitTeleport = source.handleExitTeleport;
            target.setCharacterMotorPositionToCurrentPosition = source.setCharacterMotorPositionToCurrentPosition;
            target.passengerState = source.passengerState;
        }

        // Finds an existing empty seat or creates a new one if needed
        public static RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController, ref ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            var vehicleSeat = bagController.vehicleSeat;
            foreach (var kvp in seatDict)
            {
                if (kvp.Value != null && !kvp.Value.hasPassenger)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[FindOrCreateEmptySeat] Found existing empty tracked seat for {bagController.name}");
                    return kvp.Value;
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == vehicleSeat) continue;
                bool isTracked = seatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[FindOrCreateEmptySeat] Found existing empty untracked seat for {bagController.name}");
                    return childSeat;
                }
            }

            int currentCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
            int totalAdditionalSeats = seatDict.Count;

            if (totalAdditionalSeats >= currentCapacity - 1)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[FindOrCreateEmptySeat] Cannot create additional seat. Capacity reached ({totalAdditionalSeats + 1}/{currentCapacity})");
                return null!;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[FindOrCreateEmptySeat] Creating new additional seat (Current additional: {totalAdditionalSeats}, Capacity: {currentCapacity})");

            if (!NetworkServer.active) return null!;

            var seatObject = (Networking.BagStateSync.AdditionalSeatPrefab != null)
                ? UnityEngine.Object.Instantiate(Networking.BagStateSync.AdditionalSeatPrefab)
                : new GameObject($"AdditionalSeat_Empty_{DateTime.Now.Ticks}");

            seatObject.SetActive(true);
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;

            var newSeat = seatObject.GetComponent<RoR2.VehicleSeat>();
            if (newSeat == null) newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();

            NetworkServer.Spawn(seatObject);
            CopySeatProperties(vehicleSeat, newSeat);
            return newSeat;
        }

        // Finds an existing empty seat or creates a new one if needed - uses the bag controller's state to get the seat dictionary
        public static RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController)
        {
            var seatDict = BagPatches.GetState(bagController).AdditionalSeats;
            return FindOrCreateEmptySeat(bagController, ref seatDict);
        }

        // Gets the additional seat associated with a specific object
        internal static RoR2.VehicleSeat? GetAdditionalSeatForObject(DrifterBagController bagController, GameObject? obj, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (obj == null) return null!;
            if (seatDict.TryGetValue(obj, out var seat))
            {
                return seat;
            }
            return null!;
        }

        // Gets the additional seat associated with a specific object - uses the bag controller's state to get the seat dictionary
        internal static RoR2.VehicleSeat? GetAdditionalSeatForObject(DrifterBagController bagController, GameObject? obj)
        {
            if (obj == null) return null!;
            var seatDict = BagPatches.GetState(bagController).AdditionalSeats;
            if (seatDict != null)
            {
                return GetAdditionalSeatForObject(bagController, obj, seatDict);
            }
            return null!;
        }
    }
}
