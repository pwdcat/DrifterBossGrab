#nullable enable
using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod.Patches
{

    public static class BagCarouselUpdater
    {

        internal static readonly List<UI.BaggedObjectCarousel> ActiveCarousels = new List<UI.BaggedObjectCarousel>();

        private static bool IsValidBaggedObject(GameObject obj)
        {
            if (obj == null || !obj) return false;

            var healthComp = obj.GetComponent<HealthComponent>();
            if (healthComp != null && !healthComp.alive) return false;

            var attributes = obj.GetComponent<SpecialObjectAttributes>();
            if (attributes != null && attributes.durability <= 0) return false;

            return true;
        }

        public static void UpdateCarousel(DrifterBagController controller, int direction = 0)
        {
            Log.Debug($"[UpdateCarousel] Controller: {(controller ? controller.name : "null")} Dir: {direction}.");
            for (int i = ActiveCarousels.Count - 1; i >= 0; i--)
            {
                var carousel = ActiveCarousels[i];
                if (carousel == null)
                {
                    ActiveCarousels.RemoveAt(i);
                    continue;
                }
                carousel.PopulateCarousel(direction);
            }
        }

        public static void UpdateNetworkBagState(DrifterBagController? controller, int direction = 0)
        {
            if (ReferenceEquals(controller, null) || (controller is UnityEngine.Object uController && !uController)) return;

            if (!NetworkServer.active && !controller.hasAuthority) return;

            var netController = controller.GetComponent<BottomlessBagNetworkController>();
            if (netController != null)
            {
                var baggedObjects = BagPatches.GetState(controller).BaggedObjects;

                baggedObjects.RemoveAll(obj => ReferenceEquals(obj, null) ||
                                              (obj is UnityEngine.Object uo && !uo) ||
                                              !IsValidBaggedObject(obj));

                var additionalSeats = new List<GameObject>();

                var seatDict = BagPatches.GetState(controller).AdditionalSeats;
                if (seatDict != null)
                {
                    foreach (var seat in seatDict.Values)
                    {
                        if (seat != null && seat.gameObject != null && seat.gameObject && IsValidBaggedObject(seat.gameObject))
                        {
                            additionalSeats.Add(seat.gameObject);
                        }
                    }
                }

                int selectedIndex = -1;
                var mainPassenger = BagPatches.GetMainSeatObject(controller);

                if (mainPassenger != null && !IsValidBaggedObject(mainPassenger))
                {

                    BagPatches.SetMainSeatObject(controller, null);
                    mainPassenger = null;
                }

                bool isActuallyInMainSeat = false;
                if (mainPassenger != null && controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
                {
                    if (ReferenceEquals(controller.vehicleSeat.NetworkpassengerBodyObject, mainPassenger))
                    {
                        isActuallyInMainSeat = true;
                    }
                }

                bool useTrackedMainSeat = mainPassenger != null && !isActuallyInMainSeat;

                if (isActuallyInMainSeat || useTrackedMainSeat)
                {
                    if (useTrackedMainSeat && PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[UpdateNetworkBagState] Main passenger {mainPassenger?.name} is tracked as main but not physically in main seat, using fallback. " +
                                $"Physical passenger: {controller.vehicleSeat?.NetworkpassengerBodyObject?.name ?? "null"}.");
                    }

                    for (int i = 0; i < baggedObjects.Count; i++)
                    {
                        var obj = baggedObjects[i];
                        if (obj != null && mainPassenger != null && obj.GetInstanceID() == mainPassenger.GetInstanceID())
                        {
                            selectedIndex = i;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                var reason = isActuallyInMainSeat ? "physically in main seat" : "tracked as main (fallback)";
                                Log.Debug($"[UpdateNetworkBagState] Setting selectedIndex to {i} for {obj.name} ({reason}).");
                            }
                            break;
                        }
                    }
                }

                netController.SetBagState(selectedIndex, baggedObjects, additionalSeats, direction);
            }
        }
    }
}
