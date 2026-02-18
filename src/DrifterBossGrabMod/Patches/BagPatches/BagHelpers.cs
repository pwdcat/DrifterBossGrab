using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Patches
{
    // Provides static helper methods for bag operations
    public static class BagHelpers
    {
        // Adds a tracker component to a bagged object
        public static void AddTracker(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return;
            var tracker = obj.GetComponent<BaggedObjectTracker>();
            if (tracker == null)
            {
                tracker = obj.AddComponent<BaggedObjectTracker>();
                tracker.obj = obj;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddTracker] Added BaggedObjectTracker to {obj.name} for {controller.name}");
                }
            }

            if (tracker != null && tracker.controller != controller)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddTracker] Updating controller reference for {obj.name} from {(tracker.controller ? tracker.controller.name : "null/destroyed")} to {controller.name}");
                }
                tracker.controller = controller;
            }
        }

        // Cleans up empty additional seats for the given controller
        public static void CleanupEmptyAdditionalSeats(DrifterBagController? controller)
        {
            if (controller == null)
            {
                return;
            }
            var seatDict = BagPatches.GetState(controller).AdditionalSeats;
            var seatsToRemove = new List<GameObject>();
            if (seatDict != null)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    if (seat == null || seat.gameObject == null)
                    {
                         if (seat != null && NetworkServer.active)
                         {
                             NetworkServer.Destroy(seat.gameObject);
                         }
                         if (seat != null && seat.gameObject != null)
                         {
                             UnityEngine.Object.Destroy(seat.gameObject);
                         }
                         seatsToRemove.Add(kvp.Key);
                    }
                }
                foreach (var obj in seatsToRemove)
                {
                    seatDict.TryRemove(obj, out _);
                }

            }
            var childSeats = controller.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == controller.vehicleSeat) continue;
                bool isTracked = seatDict != null && seatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Destroy(childSeat.gameObject);
                    }
                    UnityEngine.Object.Destroy(childSeat.gameObject);
                }
            }
        }

        // Gets the additional seat for a given bagged object
        public static RoR2.VehicleSeat? GetAdditionalSeat(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return null;
            var seatDict = BagPatches.GetState(controller).AdditionalSeats;
            if (seatDict != null)
            {
                if (seatDict.TryGetValue(obj, out var seat))
                {
                    return seat;
                }
            }
            return null;
        }

        // Checks if an object is currently bagged
        public static bool IsBaggedObject(DrifterBagController controller, GameObject? obj)
        {
            if (obj == null || controller == null) return false;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list != null)
            {
                int targetInstanceId = obj.GetInstanceID();
                foreach (var trackedObj in list)
                {
                    if (trackedObj != null && trackedObj.GetInstanceID() == targetInstanceId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
