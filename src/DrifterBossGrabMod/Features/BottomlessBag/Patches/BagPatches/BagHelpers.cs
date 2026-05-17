#nullable enable
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

    public static class BagHelpers
    {

        public static string GetSafeName(UnityEngine.Object? obj) => obj ? obj!.name : "null";

        public static void AddTracker(DrifterBagController? controller, GameObject obj)
        {
            if (obj == null || controller == null) return;
            var tracker = obj.GetComponent<BaggedObjectTracker>();
            if (tracker == null)
            {
                tracker = obj.AddComponent<BaggedObjectTracker>();
                tracker.obj = obj;
            }

            if (tracker != null && tracker.controller != controller)
            {
                tracker.controller = controller;
            }

            if (tracker != null)
            {
                var esms = obj.GetComponents<EntityStateMachine>();
                foreach (var esm in esms)
                {
                    if (esm.customName == "Body")
                    {
                        BaggedObjectStatePatches.RegisterTrackedESM(esm, tracker);
                        break;
                    }
                }
            }
        }

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

        public static RoR2.VehicleSeat? GetAdditionalSeat(DrifterBagController? controller, GameObject obj)
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

        public static bool IsBaggedObject(DrifterBagController? controller, GameObject? obj)
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
