using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.UI;
using RoR2.HudOverlay;
using UnityEngine;
using EntityStates.CaptainSupplyDrop;
using EntityStates.Drifter.Bag;

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
                string passengerName = "unknown";
                try
                {
                    passengerName = passenger.name;
                }
                catch (System.NullReferenceException)
                {
                    passengerName = "corrupted";
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Recovering {passengerName} to player position {playerPos}");
                }

                // Removed GrabbedObjectState restoration - testing if SpecialObjectAttributes handles this automatically

                // Teleport passenger to a position in front of the player
                Vector3 teleportPos = owner.transform.position + owner.transform.forward * 4f + Vector3.up * 2f;
                passenger.transform.position = teleportPos;


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
                    string passengerName = "null";
                    try
                    {
                        passengerName = passengerObj?.name ?? "null";
                    }
                    catch (System.NullReferenceException)
                    {
                        passengerName = "corrupted";
                    }
                    Log.Info($"{Constants.LogPrefix} ThrownObjectProjectileController.ImpactBehavior called - Passenger: {passengerName}");
                }

                // Get the passenger from the projectile
                GameObject passenger = __instance.Networkpassenger;
                if (passenger != null)
                {
                    string passengerName = "unknown";
                    try
                    {
                        passengerName = passenger.name;
                    }
                    catch (System.NullReferenceException)
                    {
                        // Passenger object is corrupted, skip processing
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipping processing of corrupted passenger object");
                        }
                        return;
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile passenger: {passengerName}");
                    }

                    // Check if persistence window is active and mark thrown object for persistence
                    if (PersistenceManager.IsPersistenceWindowActive() && PluginConfig.EnableObjectPersistence.Value)
                    {
                        PersistenceManager.MarkThrownObjectForPersistence(passenger);

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Thrown object {passengerName} marked for persistence during active window");
                        }
                    }

                    // For objects with SpecialObjectAttributes (like that wing guy), eject from VehicleSeat and manually restore
                    var specialAttrs = passenger.GetComponent<SpecialObjectAttributes>();
                    if (specialAttrs != null)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Processing SpecialObjectAttributes collision restoration for {passengerName}");
                        }

                        // Eject the passenger from VehicleSeat to restore VehicleSeat-managed colliders/behaviors
                        var vehicleSeatField = typeof(ThrownObjectProjectileController).GetField("vehicleSeat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var vehicleSeat = vehicleSeatField?.GetValue(__instance) as VehicleSeat;

                        if (vehicleSeat != null && vehicleSeat.hasPassenger && vehicleSeat.currentPassengerBody != null)
                        {
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Ejecting passenger {passengerName} from VehicleSeat");
                            }

                            // Calculate final position for ejection
                            var calculateMethod = typeof(ThrownObjectProjectileController).GetMethod("CalculatePassengerFinalPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (calculateMethod != null)
                            {
                                var parameters = new object[] { null, null };
                                calculateMethod.Invoke(__instance, parameters);
                                Vector3 finalPosition = (Vector3)parameters[0];
                                Quaternion finalRotation = (Quaternion)parameters[1];

                                // Eject the passenger
                                vehicleSeat.EjectPassenger(finalPosition, finalRotation);

                                if (PluginConfig.EnableDebugLogs.Value)
                                {
                                    Log.Info($"{Constants.LogPrefix} Successfully ejected passenger {passengerName} to position {finalPosition}");
                                }
                            }
                            else
                            {
                                vehicleSeat.EjectPassenger();
                            }
                        }

                        // Additionally, manually restore all colliders for SpecialObjectAttributes objects
                        // This handles colliders disabled by the grabbing code that VehicleSeat ejection doesn't restore
                        // Behaviors are restored by VehicleSeat ejection
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Manually restoring all colliders for {passengerName}");
                        }

                        var allColliders = passenger.GetComponentsInChildren<Collider>(true);
                        foreach (var collider in allColliders)
                        {
                            collider.enabled = true;
                        }

                        // Ensure Rigidbody is not kinematic
                        var rb = passenger.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.isKinematic = false;
                            rb.detectCollisions = true;
                        }

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Restored {allColliders.Length} colliders for {passengerName}");
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
                if (passenger == null)
                {
                    return;
                }

                // Only recover objects, not characters/enemies/bosses
                if (passenger.GetComponent<CharacterBody>() != null)
                {
                    return;
                }

                // Check if object is blacklisted from recovery
                string passengerName = "unknown";
                try
                {
                    passengerName = passenger.name;
                }
                catch (System.NullReferenceException)
                {
                    // Passenger object is corrupted, skip processing
                    return;
                }

                if (PluginConfig.IsRecoveryBlacklisted(passengerName))
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
                if (thrownController != null && thrownController.Networkpassenger != null)
                {
                    // Untrack the thrown object from persistence tracking
                    PersistenceObjectsTracker.UntrackBaggedObject(thrownController.Networkpassenger);

                    // Only recover objects, not characters/enemies/bosses
                    if (thrownController.Networkpassenger.GetComponent<CharacterBody>() == null)
                    {
                        // Check if object is blacklisted from recovery
                        string passengerName = "unknown";
                        try
                        {
                            passengerName = thrownController.Networkpassenger.name;
                        }
                        catch (System.NullReferenceException)
                        {
                            // Passenger object is corrupted, skip processing
                            return;
                        }

                        if (!PluginConfig.IsRecoveryBlacklisted(passengerName))
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
                        string passengerName = "unknown";
                        try
                        {
                            passengerName = thrownController.Networkpassenger.name;
                        }
                        catch (System.NullReferenceException)
                        {
                            passengerName = "corrupted";
                        }

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No OutOfBounds zones found, but projectile is below abyss threshold ({abyssThreshold}), recovering {passengerName}");
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
                    string passengerName2 = "unknown";
                    try
                    {
                        passengerName2 = thrownController.Networkpassenger.name;
                    }
                    catch (System.NullReferenceException)
                    {
                        passengerName2 = "corrupted";
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile at {projectilePos} is OUTSIDE all safe zones, recovering {passengerName2}");
                    }
                    RecoverObject(thrownController, thrownController.Networkpassenger, projectilePos);
                }
            }
        }

        [HarmonyPatch(typeof(SpecialObjectAttributes), "Start")]
        public class SpecialObjectAttributes_Start_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    string iconName = (__instance.portraitIcon != null) ? __instance.portraitIcon.name : "null";
                    Log.Info($"{Constants.LogPrefix} SpecialObjectAttributes.Start - Object: {__instance.gameObject.name}, portraitIcon: {iconName}, bestName: {__instance.bestName}");

                    // Log collisionToDisable count and contents
                    var collisionToDisableField = typeof(SpecialObjectAttributes).GetField("collisionToDisable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var collisionToDisable = collisionToDisableField?.GetValue(__instance) as System.Collections.Generic.List<GameObject>;
                    Log.Info($"{Constants.LogPrefix} SpecialObjectAttributes.Start - collisionToDisable count: {collisionToDisable?.Count ?? 0}");
                    if (collisionToDisable != null)
                    {
                        for (int i = 0; i < collisionToDisable.Count; i++)
                        {
                            var go = collisionToDisable[i];
                            if (go != null)
                            {
                                var colliders = go.GetComponentsInChildren<Collider>(true); // include inactive
                                Log.Info($"{Constants.LogPrefix} SpecialObjectAttributes.Start - collisionToDisable[{i}]: {go.name}, colliders found: {colliders.Length}");
                                foreach (var col in colliders)
                                {
                                    Log.Info($"{Constants.LogPrefix}   - Collider: {col.name}, enabled: {col.enabled}, gameObject: {col.gameObject.name}");
                                }
                            }
                        }
                    }
                }
            }
        }


        [HarmonyPatch(typeof(BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(BaggedObject __instance)
            {
                // Store colliders in SpecialObjectAttributes before BaggedObject.OnEnter disables them
                var targetObjectField = typeof(BaggedObject).GetField("targetObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var targetObject = targetObjectField?.GetValue(__instance) as GameObject;

                if (targetObject != null)
                {
                    var specialAttrs = targetObject.GetComponent<SpecialObjectAttributes>();
                    if (specialAttrs != null)
                    {
                        var colliders = targetObject.GetComponentsInChildren<Collider>(true);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} BaggedObject.OnEnter: Storing {colliders.Length} colliders for {targetObject.name} in SpecialObjectAttributes");
                        }

                        // Use reflection to access collidersToDisable
                        var collidersToDisableField = typeof(SpecialObjectAttributes).GetField("collidersToDisable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var collidersToDisable = collidersToDisableField?.GetValue(specialAttrs) as System.Collections.Generic.List<Collider>;

                        if (collidersToDisable != null)
                        {
                            foreach (var collider in colliders)
                            {
                                if (!collidersToDisable.Contains(collider))
                                {
                                    collidersToDisable.Add(collider);
                                }
                            }
                        }

                        // Also store behaviors
                        var behavioursToDisableField = typeof(SpecialObjectAttributes).GetField("behavioursToDisable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var behavioursToDisable = behavioursToDisableField?.GetValue(specialAttrs) as System.Collections.Generic.List<MonoBehaviour>;

                        if (behavioursToDisable != null)
                        {
                            var behaviors = targetObject.GetComponentsInChildren<MonoBehaviour>(true);
                            foreach (var behavior in behaviors)
                            {
                                // Only store behaviors that should be disabled
                                if (!behavioursToDisable.Contains(behavior))
                                {
                                    behavioursToDisable.Add(behavior);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}