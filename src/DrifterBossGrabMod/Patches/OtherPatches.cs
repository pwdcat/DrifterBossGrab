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
        // Track objects in projectile state (don't count toward capacity).
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
        private static void RecoverObject(ThrownObjectProjectileController thrownController, GameObject passenger, Vector3 projectilePos)
        {
            // Get the player who threw the projectile
            var projectileController = Traverse.Create(thrownController).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
            GameObject? owner = projectileController?.owner;
            if (owner != null)
            {
                // Find the bag controller and properly remove/eject the object
                var ownerBody = owner.GetComponent<CharacterBody>();
                if (ownerBody != null)
                {
                    var bagController = ownerBody.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($" [RecoverObject] Removing {passenger?.name ?? "null"} from bag before recovery");
                        if (passenger != null)
                        {
                            BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                        }
                    }
                }

                Vector3 playerPos = owner.transform.position;
                string passengerName = "unknown";
                try
                {
                    if (passenger != null)
                    {
                        passengerName = passenger.name;
                    }
                }
                catch (System.NullReferenceException)
                {
                    passengerName = "corrupted";
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Recovering {passengerName} to player position {playerPos}");
                }
                // Removed GrabbedObjectState restoration - testing if SpecialObjectAttributes handles this automatically
                // Teleport passenger to a position in front of the player
                Vector3 teleportPos = owner.transform.position + owner.transform.forward * 4f + Vector3.up * 2f;
                if (passenger != null) passenger.transform.position = teleportPos;

                // Clear projectile state tracking so the object can be cycled if re-grabbed
                if (passenger != null) RemoveFromProjectileState(passenger);

                // Restore original state if ModelStatePreserver exists (renderers, colliders)
                var modelStatePreserver = passenger?.GetComponent<ModelStatePreserver>();
                if (modelStatePreserver != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($" [RecoverObject] Restoring model state for recovered object {passenger?.name}");
                    // Use restoreParent=true for recovery to put the object back exactly as it was
                    modelStatePreserver.RestoreOriginalState(true);
                    UnityEngine.Object.Destroy(modelStatePreserver);
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
                    if (BagHelpers.IsBaggedObject(controller, obj))
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
            }
        }
        [HarmonyPatch(typeof(ThrownObjectProjectileController), "OnSyncPassenger")]
        public class ThrownObjectProjectileController_OnSyncPassenger_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance, GameObject passengerObject)
            {
                if (passengerObject != null)
                {
                    // Check if we've already processed this passenger (avoid double-processing)
                    if (projectileStateObjects.Contains(passengerObject))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" [ThrownObjectProjectileController.OnSyncPassenger] Passenger {passengerObject.name} already processed, skipping");
                        }
                        return;
                    }
                    ProcessThrownObject(__instance, passengerObject);
                }
            }
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
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [ProcessThrownObject] Object {passengerName} is now a projectile (airborne) - removing from bag tracking");
            }
            // Track this object as being in projectile state
            projectileStateObjects.Add(passenger);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" [ProcessThrownObject] projectileStateObjects count: {projectileStateObjects.Count}");
            }
            // Get the DrifterBagController to remove from tracking
            var projectileController = Traverse.Create(__instance).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
            GameObject? owner = projectileController?.owner;
            if (owner != null)
            {
                var bagController = owner.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
                        int currentCount = BagPatches.GetState(bagController).BaggedObjects?.Count ?? 0;
                        Log.Info($" [ProcessThrownObject] Before RemoveBaggedObject: bag has {currentCount} objects, capacity: {effectiveCapacity}");
                    }
                    // Remove from bag tracking - object is now airborne
                    BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        int effectiveCapacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
                        int currentCount = BagPatches.GetState(bagController).BaggedObjects?.Count ?? 0;
                        Log.Info($" [ProcessThrownObject] After RemoveBaggedObject: bag has {currentCount} objects, capacity: {effectiveCapacity}");
                    }
                }
            }
        }
        // Reflection Cache
        private static readonly FieldInfo _projectileControllerField = AccessTools.Field(typeof(ThrownObjectProjectileController), "projectileController");
        private static readonly FieldInfo _vehicleSeatField = AccessTools.Field(typeof(ThrownObjectProjectileController), "vehicleSeat");
        private static readonly MethodInfo _calculatePassengerFinalPositionMethod = AccessTools.Method(typeof(ThrownObjectProjectileController), "CalculatePassengerFinalPosition");
        private static readonly FieldInfo _runStickEventField = AccessTools.Field(typeof(RoR2.Projectile.ProjectileStickOnImpact), "runStickEvent");
        private static readonly FieldInfo _alreadyRanStickEventField = AccessTools.Field(typeof(RoR2.Projectile.ProjectileStickOnImpact), "alreadyRanStickEvent");

        public static void RecoverProjectile(GameObject projectile)
        {
            if (projectile == null) return;
            var controller = projectile.GetComponent<ProjectileController>();
            if (controller != null && controller.owner != null)
            {
                projectile.transform.position = controller.owner.transform.position + Vector3.up * 2f;
                var rb = projectile.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        public static void CheckAndRecoverProjectile(Component projectileComponent, string source)
        {
            if (projectileComponent == null) return;
            Vector3 pos = projectileComponent.transform.position;

            // Abstraction for both ProjectileController and ThrownObjectProjectileController
            if (pos.y < -1000f || pos.y > 5000f)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[{source}] Projectile {projectileComponent.gameObject.name} out of bounds. Recovering.");

                if (projectileComponent is ThrownObjectProjectileController thrown)
                {
                    GameObject passenger = thrown.Networkpassenger;
                    if (passenger != null && !PluginConfig.IsRecoveryBlacklisted(passenger.name))
                    {
                        RecoverObject(thrown, passenger, pos);
                    }
                }
                else
                {
                    RecoverProjectile(projectileComponent.gameObject);
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "ImpactBehavior")]
        public class ThrownObjectProjectileController_ImpactBehavior_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
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
                    Log.Info($" ThrownObjectProjectileController.ImpactBehavior called - Passenger: {passengerName}");
                }
                // Get passenger.
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Skipping processing of corrupted passenger object");
                        }
                        return;
                    }
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Projectile passenger: {passengerName}");
                    }
                    var vehicleSeat = _vehicleSeatField?.GetValue(__instance) as VehicleSeat;
                    if (vehicleSeat != null && vehicleSeat.hasPassenger)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($" Ejecting passenger {passengerName} from VehicleSeat");
                        if (_calculatePassengerFinalPositionMethod != null)
                        {
                            var parameters = new object?[] { null, null };
                            _calculatePassengerFinalPositionMethod.Invoke(__instance, parameters);
                            Vector3 finalPosition = (Vector3)parameters[0]!;
                            Quaternion finalRotation = (Quaternion)parameters[1]!;

                            // Eject the passenger - Server only to prevent desync validation warnings and position fighting
                            if (UnityEngine.Networking.NetworkServer.active)
                            {
                                vehicleSeat.EjectPassenger(finalPosition, finalRotation);
                            }
                            else
                            {
                                // Manually detach and position to prevent destruction with projectile
                                if (passenger != null)
                                {
                                    passenger.transform.SetParent(null);
                                    passenger.transform.position = finalPosition;
                                    var components = passenger?.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
                                    if (components != null)
                                    {
                                        foreach (var c in components)
                                        {
                                            if (c != null) c.enabled = true;
                                        }
                                    }
                                    if (passenger != null) passenger.transform.rotation = finalRotation;
                                }
                            }
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Successfully ejected passenger {passengerName} to position {finalPosition}");
                            }
                            // Get the DrifterBagController to clean up tracking
                            var projController = Traverse.Create(__instance).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
                            GameObject? owner = projController?.owner;
                            if (owner != null)
                            {
                                var bagController = owner.GetComponent<DrifterBagController>();
                                if (bagController != null)
                                {
                                    // Only remove from bag tracking if it's still tracked
                                    // (it may have already been removed in ThrownObjectProjectileController.Awake)
                                    if (BagHelpers.IsBaggedObject(bagController, passenger))
                                    {
                                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        {
                                            Log.Info($" Calling RemoveBaggedObject for ejected passenger {passengerName}");
                                        }
                                        if (passenger != null) Patches.BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                                    }
                                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    {
                                        Log.Info($" Skipping RemoveBaggedObject - {passengerName} already removed from tracking");
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
                                GameObject? owner = projController?.owner;
                                if (owner != null)
                                {
                                    var bagController = owner.GetComponent<DrifterBagController>();
                                    if (bagController != null)
                                    {
                                        // Only remove from bag tracking if it's still tracked
                                        if (BagHelpers.IsBaggedObject(bagController, passenger))
                                        {
                                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                            {
                                                Log.Info($" Calling RemoveBaggedObject for ejected passenger {passengerName}");
                                            }
                                            if (passenger != null)
                                            {
                                                Patches.BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                                            }
                                        }
                                        else if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        {
                                            Log.Info($" Skipping RemoveBaggedObject - {passengerName} already removed from tracking");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Ensure model is parented to passenger
                    if (passenger != null)
                    {
                        var modelLocator = passenger.GetComponent<ModelLocator>();
                        if (modelLocator != null && modelLocator.modelTransform != null)
                        {
                            if (!modelLocator.modelTransform.IsChildOf(passenger.transform))
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($" [FIX] Re-parenting detached model {modelLocator.modelTransform.name} to {passenger.name}");
                                // Use worldPositionStays=true to preserve world scale and prevent scale compounding
                                modelLocator.modelTransform.SetParent(passenger.transform, true);
                                modelLocator.modelTransform.localPosition = Vector3.zero;
                                modelLocator.modelTransform.localRotation = Quaternion.identity;
                            }
                        }
                    }

                    // Ensure Rigidbody is not kinematic
                    var rb = passenger?.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = false;
                        rb.detectCollisions = true;
                    }

                    var specialAttrs = passenger?.GetComponent<SpecialObjectAttributes>();
                    if (specialAttrs != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Processing SpecialObjectAttributes collision restoration for {passengerName}");
                        }
                    }
                    if (passenger != null)
                    {
                        var stickOnImpactComponents = passenger.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
                        if (stickOnImpactComponents != null)
                        {
                            foreach (var stickComponent in stickOnImpactComponents)
                            {
                                if (stickComponent == null) continue;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Resetting ProjectileStickOnImpact state for {passengerName}");
                                }
                                // Call Detach() to clear stored victim/position data that causes position reset
                                stickComponent.Detach();
                                _runStickEventField?.SetValue(stickComponent, false);
                                _alreadyRanStickEventField?.SetValue(stickComponent, false);

                                // Re-enable the component so it can function normally for future impacts
                                stickComponent.enabled = true;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Reset and re-enabled ProjectileStickOnImpact for {passengerName}");
                                }
                            }
                        }
                    }
                    // Restore ModelLocator state if ModelStatePreserver component exists
                    var modelStatePreserver = passenger?.GetComponent<ModelStatePreserver>();
                    if (modelStatePreserver != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Restoring ModelLocator state for {passengerName}");
                        }
                        // Restore state but SKIP parent restoration - OtherPatches already handles parenting correctly for thrown objects
                        // and restoring parent to the bag (Drifter) would be incorrect on impact
                        modelStatePreserver.RestoreOriginalState(false);
                        UnityEngine.Object.Destroy(modelStatePreserver);
                    }
                    // Also check if projectile impacted while out of bounds (fallback recovery)
                    CheckAndRecoverProjectile(__instance, "ImpactBehavior");
                    // Remove from projectile state tracking - object has landed
                    if (passenger != null) projectileStateObjects.Remove(passenger);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [ImpactBehavior] Removed {passengerName} from projectile state tracking, remaining: {projectileStateObjects.Count}");
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Projectile has no passenger");
                    }
                }
            }

        }
        [HarmonyPatch(typeof(MapZoneChecker), "FixedUpdate")]
        public class MapZoneChecker_FixedUpdate_Patch
        {
            private static readonly float checkInterval = 5f;
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
                            if (thrownController.Networkpassenger != null)
                            {
                                passengerName = thrownController.Networkpassenger.name;
                            }
                        }
                        catch (System.NullReferenceException)
                        {
                            // Passenger object is corrupted, skip processing
                            return;
                        }
                        if (!PluginConfig.IsRecoveryBlacklisted(passengerName))
                        {
                            CheckAndRecoverProjectile(thrownController, "MapZoneChecker");
                        }
                    }
                }
            }

        }
        [HarmonyPatch(typeof(RoR2.Projectile.ProjectileFuse), "FixedUpdate")]
        public class ProjectileFuse_FixedUpdate_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ProjectileFuse __instance)
            {
                // If the component is disabled
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string iconName = (__instance.portraitIcon != null) ? __instance.portraitIcon.name : "null";
                    Log.Info($" SpecialObjectAttributes.Start - Object: {__instance.gameObject.name}, portraitIcon: {iconName}, bestName: {__instance.bestName}");
                    // Log collisionToDisable count and contents
                    var collisionToDisableField = typeof(SpecialObjectAttributes).GetField("collisionToDisable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var collisionToDisable = collisionToDisableField?.GetValue(__instance) as System.Collections.Generic.List<GameObject>;
                    Log.Info($" SpecialObjectAttributes.Start - collisionToDisable count: {collisionToDisable?.Count ?? 0}");
                    if (collisionToDisable != null)
                    {
                        for (int i = 0; i < collisionToDisable.Count; i++)
                        {
                            var go = collisionToDisable[i];
                            if (go != null)
                            {
                                var colliders = go.GetComponentsInChildren<Collider>(true); // include inactive
                                Log.Info($" SpecialObjectAttributes.Start - collisionToDisable[{i}]: {go.name}, colliders found: {colliders.Length}");
                                foreach (var col in colliders)
                                {
                                    Log.Info($"   - Collider: {col.name}, enabled: {col.enabled}, gameObject: {col.gameObject.name}");
                                }
                            }
                        }
                    }
                }
            }
        }

        // Prevent clients from calling EjectPassengerToFinalPosition (server-only)
        [HarmonyPatch(typeof(ThrownObjectProjectileController), "EjectPassengerToFinalPosition")]
        public class ThrownObjectProjectileController_EjectPassengerToFinalPosition_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                // Only allow this function to run on the server
                if (!UnityEngine.Networking.NetworkServer.active)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [ThrownObjectProjectileController.EjectPassengerToFinalPosition] Blocked client call - server only");
                    }
                    return false; // Skip the method on client
                }
                return true; // Allow the method on server
            }
        }

        // Prevent clients from calling EjectPassenger
        [HarmonyPatch(typeof(ThrownObjectProjectileController), "EjectPassenger", new Type[] { })]
        public class ThrownObjectProjectileController_EjectPassenger_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                // Only allow this function to run on the server
                if (!UnityEngine.Networking.NetworkServer.active)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" [ThrownObjectProjectileController.EjectPassenger] Blocked client call - server only");
                    }
                    return false; // Skip the method on client
                }
                return true; // Allow the method on server
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
                    // Ensure ModelStatePreserver is attached before the object is stashed and hidden
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BaggedObject.OnEnter] Checking ModelStatePreserver for {targetObject.name}");
                        Log.Info($"[BaggedObject.OnEnter] EnableObjectPersistence: {PluginConfig.Instance.EnableObjectPersistence.Value}");
                        Log.Info($"[BaggedObject.OnEnter] ModelStatePreserver already exists: {targetObject.GetComponent<ModelStatePreserver>() != null}");
                    }
                    if (PluginConfig.Instance.EnableObjectPersistence.Value && targetObject.GetComponent<ModelStatePreserver>() == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[BaggedObject.OnEnter] Attaching ModelStatePreserver to {targetObject.name} to capture original state (Persistence enabled)");
                        targetObject.AddComponent<ModelStatePreserver>();
                    }
                    else if (!PluginConfig.Instance.EnableObjectPersistence.Value && PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BaggedObject.OnEnter] SKIPPING ModelStatePreserver for {targetObject.name} - Persistence is DISABLED");
                    }

                    var specialAttrs = targetObject.GetComponent<SpecialObjectAttributes>();
                    if (specialAttrs != null)
                    {
                        var colliders = targetObject.GetComponentsInChildren<Collider>(true);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" BaggedObject.OnEnter: Storing {colliders.Length} colliders for {targetObject.name} in SpecialObjectAttributes");
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

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                // Remove original bag UI (Carousel enabled)
                if (PluginConfig.Instance.EnableCarouselHUD.Value)
                {
                    var uiOverlayField = typeof(BaggedObject).GetField("uiOverlayController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var uiOverlayController = uiOverlayField?.GetValue(__instance) as OverlayController;
                    if (uiOverlayController != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[BaggedObject.OnEnter.Postfix] Removing original bag UI overlay - Carousel is enabled");
                        HudOverlayManager.RemoveOverlay(uiOverlayController);
                        uiOverlayField?.SetValue(__instance, null);
                    }
                }
            }
        }

        // Defensive null check for CheckForDeadPassenger
        [HarmonyPatch(typeof(ThrownObjectProjectileController), "CheckForDeadPassenger")]
        public class ThrownObjectProjectileController_CheckForDeadPassenger_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThrownObjectProjectileController __instance)
            {
                try
                {
                    // Skip check if passenger is null/destroyed
                    var passenger = __instance.Networkpassenger;
                    if (passenger == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[CheckForDeadPassenger] Skipping — passenger is null/destroyed");
                        return false;
                    }
                }
                catch (Exception)
                {
                    // If even accessing the passenger reference throws, definitely skip
                    return false;
                }
                return true;
            }
        }
    }
}
