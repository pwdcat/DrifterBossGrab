#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.HudOverlay;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Networking;

namespace DrifterBossGrabMod.Patches
{
    public static class ProjectileRecoveryPatches
    {
        // Constants for projectile recovery bounds and positioning.
        public static class ProjectileRecovery
        {
            // Minimum Y-coordinate for projectile recovery. Objects below this value are considered lost and should be recovered.
            public const float MinY = -1000f;

            // Maximum Y-coordinate for projectile recovery. Objects above this value are considered lost and should be recovered.
            public const float MaxY = 5000f;

            // Forward distance offset for teleporting recovered objects in front of the player.
            public const float TeleportForwardDistance = 4f;

            // Upward distance offset for teleporting recovered objects above the player.
            public const float TeleportUpDistance = 2f;

            // Upward distance offset for recovering projectiles to player position.
            public const float RecoveryUpDistance = 2f;
        }

        // Track objects in projectile state (don't count toward capacity).
        internal static readonly HashSet<GameObject> projectileStateObjects = new HashSet<GameObject>();
        private static readonly object _projectileStateLock = new object();
        private static readonly HashSet<GameObject> _objectsUndergoingThrow = new HashSet<GameObject>();
        private static readonly object _throwTrackingLock = new object();

        // Cached reflection fields
        private static readonly FieldInfo _projectileControllerField = ReflectionCache.ThrownObjectProjectileController.ProjectileController;
        private static readonly FieldInfo _vehicleSeatField = ReflectionCache.ThrownObjectProjectileController.VehicleSeat;
        private static readonly MethodInfo _calculatePassengerFinalPositionMethod = ReflectionCache.ThrownObjectProjectileController.CalculatePassengerFinalPosition;
        private static readonly FieldInfo _runStickEventField = ReflectionCache.ProjectileStickOnImpact.RunStickEvent;
        private static readonly FieldInfo _alreadyRanStickEventField = ReflectionCache.ProjectileStickOnImpact.AlreadyRanStickEvent;

        public static bool IsUndergoingThrowOperation(GameObject obj)
        {
            if (obj == null) return false;

            lock (_throwTrackingLock)
            {
                return _objectsUndergoingThrow.Contains(obj);
            }
        }

        public static void RecoverObject(ThrownObjectProjectileController thrownController, GameObject passenger, Vector3 projectilePos)
        {
            // Get the player who threw the projectile
            var projectileController = _projectileControllerField?.GetValue(thrownController) as RoR2.Projectile.ProjectileController;
            if (projectileController == null)
            {
                Log.Error($"[GrabPatch] Failed to get projectileController from {thrownController.GetType().Name}");
                return;
            }
            GameObject? owner = projectileController.owner;
            if (owner != null)
            {
                // Find the bag controller and properly remove/eject the object
                var ownerBody = owner.GetComponent<CharacterBody>();
                if (ownerBody != null)
                {
                    var bagController = ownerBody.GetComponent<DrifterBagController>();
                    if (bagController != null)
                    {
                        if (passenger != null)
                        {
                            BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                        }
                    }
                }

                Vector3 playerPos = owner.transform.position;
                string passengerName;
                if (passenger == null)
                {
                    Log.Warning($"[GrabPatch] Passenger is null, cannot recover object");
                    return;
                }
                passengerName = passenger.name;
                // Removed GrabbedObjectState restoration - testing if SpecialObjectAttributes handles this automatically
                // Teleport passenger to a position in front of the player
                Vector3 teleportPos = owner.transform.position + owner.transform.forward * ProjectileRecovery.TeleportForwardDistance + Vector3.up * ProjectileRecovery.TeleportUpDistance;
                if (passenger != null) passenger.transform.position = teleportPos;

                // Clear projectile state tracking so the object can be cycled if re-grabbed
                if (passenger != null) RemoveFromProjectileState(passenger);

                // Restore original state if ModelStatePreserver exists (renderers, colliders)
                var modelStatePreserver = passenger?.GetComponent<ModelStatePreserver>();
                if (modelStatePreserver != null)
                {
                    // Use restoreParent=true for recovery to put the object back exactly as it was
                    modelStatePreserver.RestoreOriginalState(true);
                    UnityEngine.Object.Destroy(modelStatePreserver);
                }

                // Destroy the projectile
                UnityEngine.Object.Destroy(thrownController.gameObject);

                // Clear throw operation flag since throw was aborted
                if (passenger != null)
                {
                    lock (_throwTrackingLock)
                    {
                        if (_objectsUndergoingThrow.Contains(passenger))
                        {
                            _objectsUndergoingThrow.Remove(passenger);
                            Log.Info($"[RecoverObject] {passenger.name} throw operation aborted (recovered) - now grabbable");
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "OnSyncPassenger")]
        public class ThrownObjectProjectileController_OnSyncPassenger_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance, GameObject passengerObject)
            {
                if (passengerObject == null)
                {
                    Log.Warning("[ThrownObjectProjectileController_OnSyncPassenger] passengerObject is null");
                    return;
                }

                // Validate that the passenger object is ready
                if (!NetworkUtils.ValidateObjectReady(passengerObject))
                {
                    Log.Warning($"[ThrownObjectProjectileController_OnSyncPassenger] {passengerObject.name} is not ready for network operations");
                    return;
                }

                // Check if we've already processed this passenger
                lock (_projectileStateLock)
                {
                    if (projectileStateObjects.Contains(passengerObject))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[ThrownObjectProjectileController_OnSyncPassenger] {passengerObject.name} already processed, skipping");
                        return;
                    }
                }

                // Log the sync operation
                NetworkUtils.LogNetworkOperation("ThrownObjectProjectileController_OnSyncPassenger", passengerObject, NetworkServer.active, new Dictionary<string, object>
                {
                    { "projectile", __instance != null ? __instance.name : "null" }
                });

                if (__instance != null)
                {
                    ProcessThrownObject(__instance, passengerObject!);
                }
            }
        }

        private static void ProcessThrownObject(ThrownObjectProjectileController __instance, GameObject passenger)
        {
            if (passenger == null)
            {
                Log.Error("[ThrowPatch] Passenger is null, cannot process thrown object");
                return;
            }
            string passengerName = passenger.name;

            // Mark object as undergoing throw operation
            lock (_throwTrackingLock)
            {
                _objectsUndergoingThrow.Add(passenger);
            }
            Log.Info($"[ProcessThrownObject] Marked {passengerName} as undergoing throw operation");

            // Track this object as being in projectile state
            lock (_projectileStateLock) { projectileStateObjects.Add(passenger); }

            // Get the DrifterBagController to remove from tracking
            var projectileController = _projectileControllerField?.GetValue(__instance) as RoR2.Projectile.ProjectileController;
            if (projectileController == null)
            {
                Log.Error($"[ThrowPatch] Failed to get projectileController from {__instance.GetType().Name}");
                return;
            }

            GameObject? owner = projectileController.owner;
            if (owner != null)
            {
                var bagController = owner.GetComponent<DrifterBagController>();
                if (bagController != null)
                {
                    if (NetworkServer.active)
                    {
                        // Remove from bag tracking - object is now airborne (server only)
                        Log.Info($"[ProcessThrownObject] SERVER: Removing {passengerName} from bag tracking (throw operation)");
                        BagPassengerManager.RemoveBaggedObject(bagController, passenger);

                        // Get NetworkIdentity of thrown object
                        var passengerNetId = passenger.GetComponent<UnityEngine.Networking.NetworkIdentity>();
                        if (passengerNetId != null)
                        {
                            // Explicitly remove from network controller's bagged IDs before sending message
                            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                            if (netController != null)
                            {
                                netController.RemoveBaggedObjectId(passengerNetId.netId);
                                Log.Info($"[ProcessThrownObject] SERVER: Removed {passengerName} (netId={passengerNetId.netId.Value}) from network state");
                            }

                            // Send explicit bag state update to all clients IMMEDIATELY
                            // This ensures clients sync up when the throw is processed, not on impact
                            Networking.CycleNetworkHandler.SendBagStateUpdate(bagController, passengerNetId.netId, isThrowOperation: true);
                            Log.Info($"[ProcessThrownObject] SERVER: Sent bag state update for thrown {passengerName}");
                        }
                        else
                        {
                            Log.Warning($"[ProcessThrownObject] {passengerName} does not have NetworkIdentity, cannot send state update");
                        }
                    }
                    else
                    {
                        Log.Info($"[ProcessThrownObject] CLIENT: Removing {passengerName} from local tracking");
                        BagPassengerManager.RemoveBaggedObject(bagController, passenger, isDestroying: false, skipStateReset: true);

                        Log.Info($"[ProcessThrownObject] CLIENT: {passengerName} throw processed, carousel updated");
                    }
                }
                else
                {
                    Log.Warning($"[ProcessThrownObject] Owner does not have DrifterBagController component");
                }
            }
            else
            {
                Log.Warning($"[ProcessThrownObject] Projectile owner is null");
            }
        }

        public static void RecoverProjectile(GameObject projectile)
        {
            if (projectile == null) return;
            var controller = projectile.GetComponent<ProjectileController>();
            if (controller != null && controller.owner != null)
            {
                projectile.transform.position = controller.owner.transform.position + Vector3.up * ProjectileRecovery.RecoveryUpDistance;
                var rb = projectile.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        public static void CheckAndRecoverProjectile(Component projectileComponent, string source)
        {
            if (projectileComponent == null) return;
            Vector3 pos = projectileComponent.transform.position;

            // Abstraction for both ProjectileController and ThrownObjectProjectileController
            if (pos.y < ProjectileRecovery.MinY || pos.y > ProjectileRecovery.MaxY)
            {
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
                // Get passenger.
                GameObject passenger = __instance.Networkpassenger;
                if (passenger != null)
                {
                    string passengerName = passenger.name;
                    var vehicleSeat = _vehicleSeatField?.GetValue(__instance) as VehicleSeat;
                    if (vehicleSeat != null && vehicleSeat.hasPassenger)
                    {
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
                            // Get the DrifterBagController to clean up tracking
                            var projController = _projectileControllerField?.GetValue(__instance) as RoR2.Projectile.ProjectileController;
                            if (projController == null)
                            {
                                Log.Error($"[ImpactPatch] Failed to get projectileController from {__instance.GetType().Name}");
                                return;
                            }
                            GameObject? owner = projController.owner;
                            if (owner != null)
                            {
                                var bagController = owner.GetComponent<DrifterBagController>();
                                if (bagController != null)
                                {
                                    // Only remove from bag tracking if it's still tracked
                                    // (it may have already been removed in ThrownObjectProjectileController.Awake)
                                    if (BagHelpers.IsBaggedObject(bagController, passenger))
                                    {
                                        if (passenger != null) Patches.BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                                    }
                                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    {
                                        Log.Info($" Skipping RemoveBaggedObject - {passengerName} already removed from tracking");
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

                    // Restore any disabled colliders for ungrabbable enemies
                    if (passenger != null)
                    {
                        var projController = _projectileControllerField?.GetValue(__instance) as RoR2.Projectile.ProjectileController;
                        if (projController != null && projController.owner != null)
                        {
                            var bagController = projController.owner.GetComponent<DrifterBagController>();
                            if (bagController != null)
                            {
                                var bagState = Patches.BagPatches.GetState(bagController);
                                if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(passenger, out var disabledStates))
                                {
                                    BodyColliderCache.RestoreMovementColliders(disabledStates);
                                    bagState.DisabledCollidersByObject.TryRemove(passenger, out _);
                                }
                            }
                        }
                    }

                    var specialAttrs = passenger?.GetComponent<SpecialObjectAttributes>();
                    if (passenger != null)
                    {
                        var stickOnImpactComponents = passenger.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
                        if (stickOnImpactComponents != null)
                        {
                            foreach (var stickComponent in stickOnImpactComponents)
                            {
                                if (stickComponent == null) continue;
                                // Call Detach() to clear stored victim/position data that causes position reset
                                stickComponent.Detach();
                                _runStickEventField?.SetValue(stickComponent, false);
                                _alreadyRanStickEventField?.SetValue(stickComponent, false);

                                // Re-enable the component so it can function normally for future impacts
                                stickComponent.enabled = true;
                            }
                        }
                    }
                    // Restore ModelLocator state if ModelStatePreserver component exists
                    var modelStatePreserver = passenger?.GetComponent<ModelStatePreserver>();
                    if (modelStatePreserver != null)
                    {
                        // Restore state but skip parent restoration
                        modelStatePreserver.RestoreOriginalState(false);
                        UnityEngine.Object.Destroy(modelStatePreserver);
                    }
                    // Also check if projectile impacted while out of bounds
                    CheckAndRecoverProjectile(__instance, "ImpactBehavior");
                    // Remove from projectile state tracking - object has landed
                    if (passenger != null) lock (_projectileStateLock) { projectileStateObjects.Remove(passenger); }
                    if (passenger != null)
                    {
                        // Only clear if we're still tracking it
                        lock (_throwTrackingLock)
                        {
                            if (_objectsUndergoingThrow.Contains(passenger))
                            {
                                _objectsUndergoingThrow.Remove(passenger);
                                Log.Info($"[ImpactBehavior] {passenger.name} throw operation complete - now grabbable");
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MapZoneChecker), "FixedUpdate")]
        public class MapZoneChecker_FixedUpdate_Patch
        {
            private static readonly float checkInterval = ZoneDetectionPatches.Timing.MapZoneCheckInterval;
            private static readonly Dictionary<MapZoneChecker, float> lastCheckTimes = new Dictionary<MapZoneChecker, float>();

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
                        if (thrownController.Networkpassenger == null)
                        {
                            Log.Warning("[ZonePatch] Passenger is null, skipping recovery check");
                            return;
                        }
                        string passengerName = thrownController.Networkpassenger.name;
                        if (!PluginConfig.IsRecoveryBlacklisted(passengerName))
                        {
                            CheckAndRecoverProjectile(thrownController, "MapZoneChecker");
                        }
                    }
                }
            }
        }

        public static bool IsInProjectileState(GameObject obj)
        {
            if (obj == null) return false;

            lock (_projectileStateLock)
            {
                return projectileStateObjects.Contains(obj) || _objectsUndergoingThrow.Contains(obj);
            }
        }

        public static int GetProjectileStateCount(DrifterBagController controller)
        {
            if (controller == null) return 0;
            int count = 0;
            lock (_projectileStateLock)
            {
                foreach (var obj in projectileStateObjects)
                {
                    if (obj != null && BagHelpers.IsBaggedObject(controller, obj))
                        count++;
                }
            }
            return count;
        }

        public static void RemoveFromProjectileState(GameObject obj)
        {
            if (obj != null)
            {
                lock (_projectileStateLock) { projectileStateObjects.Remove(obj); }
            }
        }
    }
}
