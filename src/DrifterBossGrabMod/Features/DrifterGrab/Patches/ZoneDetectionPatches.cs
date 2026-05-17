#nullable enable
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.Patches
{
    public static class ZoneDetectionPatches
    {
        // Constants for timing intervals.
        public static class Timing
        {
            // Interval in seconds between MapZoneChecker checks for each projectile instance.
            // Throttles checks to prevent excessive processing.
            public const float MapZoneCheckInterval = 5f;
        }

        // Tracks whether OutOfBounds zones are inverted in the current stage
        private static bool areOutOfBoundsZonesInverted = false;
        private static bool zoneInversionDetected = false;

        public static void DetectZoneInversion(Vector3 playerPosition)
        {
            if (zoneInversionDetected) return; // Already detected for this stage
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
                // If player is not inside OutOfBounds zones at spawn, zones are inverted
                areOutOfBoundsZonesInverted = !playerInsideAnyOutOfBounds;
                zoneInversionDetected = true;
            }
            else
            {
                // No OutOfBounds zones found
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
