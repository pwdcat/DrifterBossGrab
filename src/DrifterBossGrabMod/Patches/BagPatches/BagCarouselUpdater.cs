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
    // Provides static helper methods for updating the bag carousel and network state
    public static class BagCarouselUpdater
    {
        // Updates the bag carousel UI for the given controller
        public static void UpdateCarousel(DrifterBagController controller, int direction = 0)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[UpdateCarousel] Controller: {(controller ? controller.name : "null")} Dir: {direction}");
            var carousels = UnityEngine.Object.FindObjectsByType<UI.BaggedObjectCarousel>(FindObjectsSortMode.None);
            foreach (var carousel in carousels)
            {
                carousel.PopulateCarousel(direction);
            }
        }

        // Updates the network bag state for the given controller
        public static void UpdateNetworkBagState(DrifterBagController? controller, int direction = 0)
        {
            if (ReferenceEquals(controller, null) || (controller is UnityEngine.Object uController && !uController)) return;

            if (!NetworkServer.active && !controller.hasAuthority) return;

            var netController = controller.GetComponent<BottomlessBagNetworkController>();
            if (netController != null)
            {
                var baggedObjects = BagPatches.GetState(controller).BaggedObjects;

                baggedObjects.RemoveAll(obj => ReferenceEquals(obj, null) || (obj is UnityEngine.Object uo && !uo));

                var additionalSeats = new List<GameObject>();
                // Consolidated state access
                var seatDict = BagPatches.GetState(controller).AdditionalSeats;
                if (seatDict != null)
                {
                    foreach (var seat in seatDict.Values)
                    {
                        if (seat != null) additionalSeats.Add(seat.gameObject);
                    }
                }

                int selectedIndex = -1;
                var mainPassenger = BagPatches.GetMainSeatObject(controller);

                bool isActuallyInMainSeat = false;
                if (mainPassenger != null && controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
                {
                    if (ReferenceEquals(controller.vehicleSeat.NetworkpassengerBodyObject, mainPassenger))
                    {
                        isActuallyInMainSeat = true;
                    }
                }

                // On client (non-server), fall back to tracked main seat object
                bool useTrackedMainSeat = !NetworkServer.active && controller.hasAuthority && mainPassenger != null && !isActuallyInMainSeat;

                if (isActuallyInMainSeat || useTrackedMainSeat)
                {
                    for (int i = 0; i < baggedObjects.Count; i++)
                    {
                        var obj = baggedObjects[i];
                        if (obj != null && mainPassenger != null && obj.GetInstanceID() == mainPassenger.GetInstanceID())
                        {
                            selectedIndex = i;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                var reason = isActuallyInMainSeat ? "physically in main seat" : "tracked as main (client)";
                                Log.Info($"[UpdateNetworkBagState] Setting selectedIndex to {i} for {obj.name} ({reason})");
                            }
                            break;
                        }
                    }
                }
                else if (mainPassenger != null && PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UpdateNetworkBagState] Skipping selectedIndex calculation - {mainPassenger.name} is tracked as main but not physically in main seat (likely in additional seat)");
                }

                netController.SetBagState(selectedIndex, baggedObjects, additionalSeats, direction);
            }
        }
    }
}
