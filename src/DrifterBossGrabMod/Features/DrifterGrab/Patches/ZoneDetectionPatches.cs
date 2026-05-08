#nullable enable
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.Patches
{
    public static class ZoneDetectionPatches
    {
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
                    bool inside = zone.IsPointInsideMapZone(playerPosition);
                    if (inside)
                    {
                        playerInsideAnyOutOfBounds = true;
                    }

                    Log.DebugIfEnabled("[ZoneDetection] Found MapZone: {0} | Type: {1} | PlayerInside: {2}", zone.name, zone.triggerType, inside);
                }
            }

            if (outOfBoundsCount > 0)
            {
                areOutOfBoundsZonesInverted = !playerInsideAnyOutOfBounds;
                zoneInversionDetected = true;

                Log.DebugIfEnabled("[ZoneDetection] Detection complete. Inverted: {0} (based on player spawn)", areOutOfBoundsZonesInverted);
            }
            else
            {
                // No OutOfBounds zones found
                areOutOfBoundsZonesInverted = false;
                zoneInversionDetected = true;
            }
        }

        // Checks if a position is currently out of bounds
        public static bool IsPositionOOB(Vector3 position)
        {
            MapZone[] mapZones = UnityEngine.Object.FindObjectsByType<MapZone>(UnityEngine.FindObjectsInactive.Exclude, UnityEngine.FindObjectsSortMode.None);
            int characterHullLayer = LayerMask.NameToLayer("CollideWithCharacterHullOnly");

            foreach (MapZone zone in mapZones)
            {
                if (zone.zoneType == MapZone.ZoneType.OutOfBounds && zone.gameObject.layer == characterHullLayer)
                {
                    bool inside = zone.IsPointInsideMapZone(position);

                    if (zone.triggerType == MapZone.TriggerType.TriggerEnter && inside) return true;
                    if (zone.triggerType == MapZone.TriggerType.TriggerExit && !inside) return true;
                }
            }
            return false;
        }

        public static void ResetZoneInversionDetection()
        {
            zoneInversionDetected = false;
            areOutOfBoundsZonesInverted = false;
        }
    }
}
