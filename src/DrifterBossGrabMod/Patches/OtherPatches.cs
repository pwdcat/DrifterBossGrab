using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using EntityStates.CaptainSupplyDrop;

namespace DrifterBossGrabMod.Patches
{
    public static class OtherPatches
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

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Zone inversion detection: Player at {playerPosition} is {(playerInsideAnyOutOfBounds ? "inside" : "outside")} {outOfBoundsCount} OutOfBounds zones. Zones are {(areOutOfBoundsZonesInverted ? "inverted" : "normal")}.");
                }
            }
            else
            {
                // No OutOfBounds zones found
                areOutOfBoundsZonesInverted = false;
                zoneInversionDetected = true;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No OutOfBounds zones found, assuming normal zone logic.");
                }
            }
        }

        public static void ResetZoneInversionDetection()
        {
            zoneInversionDetected = false;
            areOutOfBoundsZonesInverted = false;
        }

        private static void RecoverObject(ThrownObjectProjectileController thrownController, GameObject passenger, Vector3 projectilePos)
        {
            // Get the player who threw the projectile
            var projectileController = Traverse.Create(thrownController).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
            GameObject owner = projectileController?.owner;
            if (owner != null)
            {
                Vector3 playerPos = owner.transform.position;
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Recovering {passenger.name} to player position {playerPos}");
                }

                // Restore states
                var grabbedState = passenger.GetComponent<GrabbedObjectState>();
                if (grabbedState != null)
                {
                    grabbedState.RestoreAllStates();
                }

                // Teleport passenger to a position in front of the player
                Vector3 teleportPos = owner.transform.position + owner.transform.forward * 4f + Vector3.up * 2f;
                passenger.transform.position = teleportPos;

                // Optionally reset rotation to upright if configured
                if (PluginConfig.EnableUprightRecovery.Value)
                {
                    passenger.transform.rotation = Quaternion.identity;
                }

                // Destroy the projectile
                UnityEngine.Object.Destroy(thrownController.gameObject);
            }
        }

        [HarmonyPatch(typeof(HackingMainState), "FixedUpdate")]
        public class HackingMainState_FixedUpdate_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(HackingMainState __instance)
            {
                // Update the search origin to follow the beacon's current position
                var traverse = Traverse.Create(__instance);
                var field = __instance.GetType().GetField("sphereSearch", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var sphereSearch = (SphereSearch)field.GetValue(__instance);
                    var transform = traverse.Property("transform").GetValue<Transform>();
                    if (sphereSearch != null && transform != null && sphereSearch.origin != transform.position)
                    {
                        sphereSearch.origin = transform.position;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "ImpactBehavior")]
        public class ThrownObjectProjectileController_ImpactBehavior_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    GameObject passengerObj = __instance.Networkpassenger;
                    Log.Info($"{Constants.LogPrefix} ThrownObjectProjectileController.ImpactBehavior called - Passenger: {passengerObj?.name ?? "null"}");
                }

                // Get the passenger from the projectile
                GameObject passenger = __instance.Networkpassenger;
                if (passenger != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile passenger: {passenger.name}");
                    }

                    // Check if persistence window is active and mark thrown object for persistence
                    if (PersistenceManager.IsPersistenceWindowActive() && PluginConfig.EnableObjectPersistence.Value)
                    {
                        PersistenceManager.MarkThrownObjectForPersistence(passenger);

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Thrown object {passenger.name} marked for persistence during active window");
                        }
                    }

                    // Check if the passenger has our state storage component
                    var grabbedState = passenger.GetComponent<GrabbedObjectState>();
                    if (grabbedState != null)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Projectile impacted - restoring states for {passenger.name}");
                        }
                        // Restore all the stored states
                        grabbedState.RestoreAllStates();
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No GrabbedObjectState found on passenger {passenger.name}");
                        }
                    }

                    // Also check if projectile impacted while out of bounds (fallback recovery)
                    CheckAndRecoverProjectile(__instance, null);
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile has no passenger");
                    }
                }
            }

            private static void CheckAndRecoverProjectile(ThrownObjectProjectileController thrownController, MapZoneChecker? zoneChecker)
            {
                GameObject passenger = thrownController.Networkpassenger;
                if (passenger == null || passenger.GetComponent<GrabbedObjectState>() == null)
                {
                    return;
                }

                // Only recover objects, not characters/enemies/bosses
                if (passenger.GetComponent<CharacterBody>() != null)
                {
                    return;
                }

                // Check if object is blacklisted from recovery
                if (PluginConfig.IsRecoveryBlacklisted(passenger.name))
                {
                    return;
                }

                Vector3 projectilePos = thrownController.transform.position;
                MapZone[] mapZones = UnityEngine.Object.FindObjectsByType<MapZone>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} CheckAndRecoverProjectile: Found {mapZones.Length} MapZone objects at position {projectilePos}");
                }

                bool isInAnySafeZone = false;
                int outOfBoundsCount = 0;
                int characterHullLayer = LayerMask.NameToLayer("CollideWithCharacterHullOnly");

                foreach (MapZone zone in mapZones)
                {
                    if (zone.zoneType == MapZone.ZoneType.OutOfBounds && zone.gameObject.layer == characterHullLayer)
                    {
                        outOfBoundsCount++;
                        bool isInside = zone.IsPointInsideMapZone(projectilePos);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} OutOfBounds zone (CollideWithCharacterHullOnly) found, projectile inside: {isInside}");
                        }
                        // If zones are inverted, being inside means out of bounds
                        // If zones are normal, being inside means safe
                        if (areOutOfBoundsZonesInverted ? !isInside : isInside)
                        {
                            isInAnySafeZone = true;
                        }
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Total OutOfBounds (CollideWithCharacterHullOnly) zones: {outOfBoundsCount}, isInAnySafeZone: {isInAnySafeZone} (zones {(areOutOfBoundsZonesInverted ? "inverted" : "normal")})");
                }

                // If no out of bounds zones are defined, use fallback recovery based on height
                if (outOfBoundsCount == 0)
                {
                    // Fallback: recover if projectile is below a reasonable map floor level
                    const float abyssThreshold = -1000f;
                    if (projectilePos.y < abyssThreshold)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No OutOfBounds zones found, but projectile is below abyss threshold ({abyssThreshold}), recovering {passenger.name}");
                        }
                        RecoverObject(thrownController, passenger, projectilePos);
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No OutOfBounds zones found and projectile is above abyss threshold, skipping recovery");
                        }
                    }
                    return;
                }

                // If projectile is NOT in any safe zone, it's out of bounds and should be recovered
                if (!isInAnySafeZone)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile impacted OUTSIDE safe zones at {projectilePos}, recovering {passenger.name}");
                    }
                    RecoverObject(thrownController, passenger, projectilePos);
                }
            }
        }

        [HarmonyPatch(typeof(MapZoneChecker), "FixedUpdate")]
        public class MapZoneChecker_FixedUpdate_Patch
        {
            private static readonly float checkInterval = 5f; // Check every 5 seconds
            private static System.Collections.Generic.Dictionary<MapZoneChecker, float> lastCheckTimes = new System.Collections.Generic.Dictionary<MapZoneChecker, float>();

            [HarmonyPostfix]
            public static void Postfix(MapZoneChecker __instance)
            {
                // Throttle checks per projectile instance to every 5 seconds
                if (!lastCheckTimes.ContainsKey(__instance))
                {
                    lastCheckTimes[__instance] = 0f;
                }

                if (Time.time - lastCheckTimes[__instance] < checkInterval)
                {
                    return;
                }
                lastCheckTimes[__instance] = Time.time;

                var thrownController = __instance.GetComponent<ThrownObjectProjectileController>();
                if (thrownController != null && thrownController.Networkpassenger != null &&
                    thrownController.Networkpassenger.GetComponent<GrabbedObjectState>() != null)
                {
                    // Untrack the thrown object from persistence tracking
                    PersistenceObjectsTracker.UntrackBaggedObject(thrownController.Networkpassenger);

                    // Only recover objects, not characters/enemies/bosses
                    if (thrownController.Networkpassenger.GetComponent<CharacterBody>() == null)
                    {
                        // Check if object is blacklisted from recovery
                        if (!PluginConfig.IsRecoveryBlacklisted(thrownController.Networkpassenger.name))
                        {
                            CheckAndRecoverProjectileZoneChecker(thrownController, __instance);
                        }
                    }
                }
            }


            private static void CheckAndRecoverProjectileZoneChecker(ThrownObjectProjectileController thrownController, MapZoneChecker zoneChecker)
            {
                Vector3 projectilePos = thrownController.transform.position;
                MapZone[] mapZones = UnityEngine.Object.FindObjectsByType<MapZone>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} CheckAndRecoverProjectileZoneChecker: Found {mapZones.Length} MapZone objects at position {projectilePos}");
                }

                bool isInAnySafeZone = false;
                int outOfBoundsCount = 0;
                int characterHullLayer = LayerMask.NameToLayer("CollideWithCharacterHullOnly");

                foreach (MapZone zone in mapZones)
                {
                    if (zone.zoneType == MapZone.ZoneType.OutOfBounds && zone.gameObject.layer == characterHullLayer)
                    {
                        outOfBoundsCount++;
                        bool isInside = zone.IsPointInsideMapZone(projectilePos);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} OutOfBounds zone (CollideWithCharacterHullOnly) found, projectile inside: {isInside}");
                        }
                        // If zones are inverted, being inside means out of bounds (not safe)
                        // If zones are normal, being inside means safe
                        if (areOutOfBoundsZonesInverted ? !isInside : isInside)
                        {
                            isInAnySafeZone = true;
                        }
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Total OutOfBounds (CollideWithCharacterHullOnly) zones: {outOfBoundsCount}, isInAnySafeZone: {isInAnySafeZone} (zones {(areOutOfBoundsZonesInverted ? "inverted" : "normal")})");
                }

                // If no out of bounds zones are defined, use fallback recovery based on height
                if (outOfBoundsCount == 0)
                {
                    // Fallback: recover if projectile is below a reasonable map floor level
                    const float abyssThreshold = -100f;
                    if (projectilePos.y < abyssThreshold)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No OutOfBounds zones found, but projectile is below abyss threshold ({abyssThreshold}), recovering {thrownController.Networkpassenger.name}");
                        }
                        RecoverObject(thrownController, thrownController.Networkpassenger, projectilePos);
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No OutOfBounds zones found and projectile is above abyss threshold, skipping recovery");
                        }
                    }
                    return;
                }

                // If projectile is NOT in any safe zone, it's out of bounds and should be recovered
                if (!isInAnySafeZone)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile at {projectilePos} is OUTSIDE all safe zones, recovering {thrownController.Networkpassenger.name}");
                    }
                    RecoverObject(thrownController, thrownController.Networkpassenger, projectilePos);
                }
            }
        }
    }
}