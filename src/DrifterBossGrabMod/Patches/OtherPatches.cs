using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.UI;
using RoR2.HudOverlay;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.CaptainSupplyDrop;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class OtherPatches
    {
        // Track objects that are currently in projectile (airborne) state
        // These objects should not count toward bag capacity
        internal static readonly System.Collections.Generic.HashSet<GameObject> projectileStateObjects = new System.Collections.Generic.HashSet<GameObject>();

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

        public static bool IsInProjectileState(GameObject obj)
        {
            return obj != null && projectileStateObjects.Contains(obj);
        }

        public static int GetProjectileStateCount(DrifterBagController controller)
        {
            if (controller == null) return 0;
            
            int count = 0;
            foreach (var obj in projectileStateObjects)
            {
                if (obj != null)
                {
                    // Check if this object belongs to the given controller
                    // We need to check if the object was tracked by this controller
                    if (BagPatches.IsBaggedObject(controller, obj))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public static void RemoveFromProjectileState(GameObject obj)
        {
            if (obj != null)
            {
                projectileStateObjects.Remove(obj);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [RemoveFromProjectileState] Removed {obj.name} from projectile tracking, remaining: {projectileStateObjects.Count}");
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "Awake")]
        public class ThrownObjectProjectileController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance)
            {
                // Try to get the passenger from the projectile
                GameObject passenger = GetPassenger(__instance);
                
                if (passenger != null)
                {
                    ProcessThrownObject(__instance, passenger);
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [ThrownObjectProjectileController.Awake] Projectile has no passenger (will try in Start)");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "Start")]
        public class ThrownObjectProjectileController_Start_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance)
            {
                // Try to get the passenger from the projectile
                GameObject passenger = GetPassenger(__instance);
                
                if (passenger != null)
                {
                    // Check if we've already processed this passenger (avoid double-processing)
                    if (projectileStateObjects.Contains(passenger))
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [ThrownObjectProjectileController.Start] Passenger {passenger.name} already processed, skipping");
                        }
                        return;
                    }
                    
                    ProcessThrownObject(__instance, passenger);
                }
            }
        }

        private static GameObject GetPassenger(ThrownObjectProjectileController controller)
        {
            // First try Networkpassenger (synced field)
            GameObject passenger = controller.Networkpassenger;
            if (passenger == null)
            {
                // Try to get passenger from the private field via reflection
                var passengerField = typeof(ThrownObjectProjectileController).GetField("passenger", BindingFlags.NonPublic | BindingFlags.Instance);
                passenger = passengerField?.GetValue(controller) as GameObject;
            }
            return passenger;
        }

        private static void ProcessThrownObject(ThrownObjectProjectileController __instance, GameObject passenger)
        {
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
                Log.Info($"{Constants.LogPrefix} [ProcessThrownObject] Object {passengerName} is now a projectile (airborne) - removing from bag tracking");
            }

            // Track this object as being in projectile state
            projectileStateObjects.Add(passenger);

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [ProcessThrownObject] projectileStateObjects count: {projectileStateObjects.Count}");
            }

            // Get the DrifterBagController to remove from tracking
            var projectileController = Traverse.Create(__instance).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
            GameObject owner = projectileController?.owner;
            if (owner != null)
            {
                var bagController = owner.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        int effectiveCapacity = BagPatches.GetUtilityMaxStock(bagController);
                        int currentCount = BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list) ? list.Count : 0;
                        Log.Info($"{Constants.LogPrefix} [ProcessThrownObject] Before RemoveBaggedObject: bag has {currentCount} objects, capacity: {effectiveCapacity}");
                    }
                    
                    // Remove from bag tracking - object is now airborne
                    BagPatches.RemoveBaggedObject(bagController, passenger);
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        int effectiveCapacity = BagPatches.GetUtilityMaxStock(bagController);
                        int currentCount = BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list) ? list.Count : 0;
                        Log.Info($"{Constants.LogPrefix} [ProcessThrownObject] After RemoveBaggedObject: bag has {currentCount} objects, capacity: {effectiveCapacity}");
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

                                // Get the DrifterBagController to clean up tracking
                                var projController = Traverse.Create(__instance).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
                                GameObject owner = projController?.owner;
                                if (owner != null)
                                {
                                    var bagController = owner.GetComponent<DrifterBagController>();
                                    if (bagController != null)
                                    {
                                        // Only remove from bag tracking if it's still tracked
                                        // (it may have already been removed in ThrownObjectProjectileController.Awake)
                                        if (BagPatches.IsBaggedObject(bagController, passenger))
                                        {
                                            if (PluginConfig.EnableDebugLogs.Value)
                                            {
                                                Log.Info($"{Constants.LogPrefix} Calling RemoveBaggedObject for ejected passenger {passengerName}");
                                            }
                                            Patches.BagPatches.RemoveBaggedObject(bagController, passenger);
                                        }
                                        else if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} Skipping RemoveBaggedObject - {passengerName} already removed from tracking");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (UnityEngine.Networking.NetworkServer.active)
                                {
                                    vehicleSeat.EjectPassenger();

                                    // Get the DrifterBagController to clean up tracking
                                    var projController = Traverse.Create(__instance).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
                                    GameObject owner = projController?.owner;
                                    if (owner != null)
                                    {
                                        var bagController = owner.GetComponent<DrifterBagController>();
                                        if (bagController != null)
                                        {
                                            // Only remove from bag tracking if it's still tracked
                                            // (it may have already been removed in ThrownObjectProjectileController.Awake)
                                            if (BagPatches.IsBaggedObject(bagController, passenger))
                                            {
                                                if (PluginConfig.EnableDebugLogs.Value)
                                                {
                                                    Log.Info($"{Constants.LogPrefix} Calling RemoveBaggedObject for ejected passenger {passengerName}");
                                                }
                                                Patches.BagPatches.RemoveBaggedObject(bagController, passenger);
                                            }
                                            else if (PluginConfig.EnableDebugLogs.Value)
                                            {
                                                Log.Info($"{Constants.LogPrefix} Skipping RemoveBaggedObject - {passengerName} already removed from tracking");
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Additionally, manually restore all colliders for SpecialObjectAttributes objects
                        // This handles colliders disabled by the grabbing code that VehicleSeat ejection doesn't restore
                        // Behaviors are restored by VehicleSeat ejection
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Manually restoring all colliders for {passengerName}");

                            // Debug ModelLocator state
                            var modelLocator = passenger.GetComponent<ModelLocator>();
                            if (modelLocator != null)
                            {
                                if (modelLocator.modelTransform != null)
                                {
                                    var modelColliders = modelLocator.modelTransform.GetComponentsInChildren<Collider>(true);
                                    Log.Info($"{Constants.LogPrefix} Model has {modelColliders.Length} colliders");
                                }
                            }
                            else
                            {
                                Log.Info($"{Constants.LogPrefix} No ModelLocator found on {passengerName}");
                            }
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

                    // Special handling for ProjectileStickOnImpact components (fixes EngiBubbleShield and Engie mines position reset)
                    var stickOnImpactComponents = passenger.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
                    foreach (var stickComponent in stickOnImpactComponents)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Resetting ProjectileStickOnImpact state for {passengerName}");
                        }

                        // Call Detach() to clear stored victim/position data that causes position reset
                        stickComponent.Detach();

                        // Reset event flags to prevent stick event from firing inappropriately
                        // Use reflection to access private fields
                        var runStickEventField = typeof(RoR2.Projectile.ProjectileStickOnImpact).GetField("runStickEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var alreadyRanStickEventField = typeof(RoR2.Projectile.ProjectileStickOnImpact).GetField("alreadyRanStickEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (runStickEventField != null)
                            runStickEventField.SetValue(stickComponent, false);
                        if (alreadyRanStickEventField != null)
                            alreadyRanStickEventField.SetValue(stickComponent, false);

                        // Re-enable the component so it can function normally for future impacts
                        stickComponent.enabled = true;

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Reset and re-enabled ProjectileStickOnImpact for {passengerName}");
                        }
                    }

                    // Restore ModelLocator state if ModelStatePreserver component exists
                    var modelStatePreserver = passenger.GetComponent<ModelStatePreserver>();
                    if (modelStatePreserver != null)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Restoring ModelLocator state for {passengerName}");
                        }
                        modelStatePreserver.RestoreOriginalState();
                        UnityEngine.Object.Destroy(modelStatePreserver);
                    }

                    // Also check if projectile impacted while out of bounds (fallback recovery)
                    CheckAndRecoverProjectile(__instance, null);
                    
                    // Remove from projectile state tracking - object has landed
                    projectileStateObjects.Remove(passenger);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [ImpactBehavior] Removed {passengerName} from projectile state tracking, remaining: {projectileStateObjects.Count}");
                    }
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

        [HarmonyPatch(typeof(RoR2.Projectile.ProjectileFuse), "FixedUpdate")]
        public class ProjectileFuse_FixedUpdate_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ProjectileFuse __instance)
            {
                // If the component is disabled (e.g., projectile is grabbed), don't decrement the fuse
                if (!__instance.enabled)
                {
                    return false; // Skip the original method
                }
                return true; // Continue with original method
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