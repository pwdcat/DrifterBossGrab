#nullable enable
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.Patches
{
    public static class ZoneDetectionPatches
    {

        public static class Timing
        {

            public const float MapZoneCheckInterval = 5f;
        }

        private static bool areOutOfBoundsZonesInverted = false;
        private static bool zoneInversionDetected = false;

        public static void DetectZoneInversion(Vector3 playerPosition)
        {
            if (zoneInversionDetected) return;
            MapZone[] mapZones = UnityEngine.Object.FindObjectsByType<MapZone>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
            int outOfBoundsCount = 0;
            bool playerInsideAnyOutOfBounds = false;
            int characterHullLayer = LayerMask.NameToLayer("CollideWithCharacterHullOnly");
            foreach (MapZone zone in mapZones)
            {
                if (zone.zoneType == MapZone.ZoneType.OutOfBounds && zone.gameObject.layer == characterHullLayer)
                {
                    outOfBoundsCount++;
                    if (zone.IsPointInsideMapZone(playerPosition))
                    {
                        playerInsideAnyOutOfBounds = true;
                    }
                }
            }
            if (outOfBoundsCount > 0)
            {

                areOutOfBoundsZonesInverted = !playerInsideAnyOutOfBounds;
                zoneInversionDetected = true;
            }
            else
            {

                areOutOfBoundsZonesInverted = false;
                zoneInversionDetected = true;
            }
        }

        public static void ResetZoneInversionDetection()
        {
            zoneInversionDetected = false;
            areOutOfBoundsZonesInverted = false;
        }
    }
}
