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
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    public static class ProjectileRecoveryPatches
    {
        // Constants for projectile recovery positioning.
        public static class ProjectileRecovery
        {
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
        
        // Track the bag controller that last held an object, so we can recover it even after it's ejected.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, DrifterBagController> lastKnownOwners = new System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, DrifterBagController>();

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

        public static void RecoverObject(GameObject passenger)
        {
            if (passenger == null) return;

            // Get the DrifterBagController associated with this object.
            DrifterBagController? bagController = null;
            var specialAttrs = passenger.GetComponent<SpecialObjectAttributes>();
            if (specialAttrs != null)
            {
                // Try to find the bag controller via the attributes or by searching.
                foreach (var controller in BagPatches.GetAllControllers())
                {
                    if (BagHelpers.IsBaggedObject(controller, passenger))
                    {
                        bagController = controller;
                        break;
                    }
                }
            }

            // Fallback: If we couldn't find the bag controller via direct search
            if (bagController == null)
            {
                lastKnownOwners.TryGetValue(passenger, out bagController);
            }

            if (bagController == null || bagController.characterBody == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Warning($"[Recovery] Could not find bag owner/destination for {passenger.name}");
                return;
            }

            // Log recovery
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[Recovery] Recovering {passenger.name} for {bagController.characterBody.name}");
            }

            // Determine teleport position
            // Log details if debug enabled
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[Recovery] Attempting to recover bagged object: {passenger.name}");
            }

            Vector3 teleportPos = bagController.characterBody.corePosition + bagController.characterBody.transform.forward * ProjectileRecovery.TeleportForwardDistance + Vector3.up * ProjectileRecovery.TeleportUpDistance;
            
            if (Run.instance)
            {
                teleportPos = Run.instance.FindSafeTeleportPosition(bagController.characterBody, bagController.transform, 0f, 100f);
            }

            // Properly remove/eject the object from the bag tracking
            BagPassengerManager.RemoveBaggedObject(bagController, passenger);
            
            // Teleport and restore state
            passenger.transform.position = teleportPos;
            RemoveFromProjectileState(passenger);

            // Restore physics and model state
            var bagState = BagPatches.GetState(bagController);
            if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(passenger, out var states))
            {
                BodyColliderCache.RestoreMovementColliders(states);
                bagState.DisabledCollidersByObject.Remove(passenger, out _);
            }

            var modelLocator = passenger.GetComponent<ModelLocator>();
            if (modelLocator != null)
            {
                var state = BaggedObjectPatches.LoadObjectState(bagController, passenger);
                modelLocator.autoUpdateModelTransform = state != null ? state.originalAutoUpdateModelTransform : true;
                modelLocator.dontDetatchFromParent = true;
            }

            // Clear throw operation flag
            lock (_throwTrackingLock)
            {
                if (_objectsUndergoingThrow.Contains(passenger))
                {
                    _objectsUndergoingThrow.Remove(passenger);
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
            // Track this object as being in projectile state
            lock (_projectileStateLock) { projectileStateObjects.Add(passenger); }

            // Switch to Layer 0 (Default)
            int targetLayer = 0; 
            __instance.gameObject.layer = targetLayer;
            foreach (var transform in __instance.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = targetLayer;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                var colliders = ReflectionCache.ThrownObjectProjectileController.MyColliders?.GetValue(__instance) as Collider[];
                var collDisabled = (bool)(ReflectionCache.ThrownObjectProjectileController.CollidersDisabled?.GetValue(__instance) ?? false);
                var countdown = (float)(ReflectionCache.ThrownObjectProjectileController.DisableCollidersCountdown?.GetValue(__instance) ?? 0f);
                
                Log.Debug($"[Recovery] STARTUP TRACE: {passengerName}");
                Log.Debug($" - Layer: {__instance.gameObject.layer} ({UnityEngine.LayerMask.LayerToName(__instance.gameObject.layer)})");
                Log.Debug($" - Colliders Count: {colliders?.Length ?? 0}");
                Log.Debug($" - _collidersDisabled: {collDisabled}");
                Log.Debug($" - _disableCollidersCountdown: {countdown}");

                // Full Physics Collision Matrix Trace (32 Layers)
                System.Text.StringBuilder collisionMap = new System.Text.StringBuilder();
                collisionMap.Append($" - Projectile Matrix (Layer {__instance.gameObject.layer}): ");
                for (int i = 0; i < 32; i++)
                {
                    if (!UnityEngine.Physics.GetIgnoreLayerCollision(__instance.gameObject.layer, i))
                        collisionMap.Append($"{i} ");
                }
                Log.Debug(collisionMap.ToString());

                // Potential MapZone Matrix Trace (Layer 24 was identified as TriggerZone)
                System.Text.StringBuilder zoneMap = new System.Text.StringBuilder();
                zoneMap.Append(" - TriggerZone Matrix (Layer 24): ");
                for (int i = 0; i < 32; i++)
                {
                    if (!UnityEngine.Physics.GetIgnoreLayerCollision(24, i))
                        zoneMap.Append($"{i} ");
                }
                Log.Debug(zoneMap.ToString());
            }

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
                        // Store last known owner for recovery purposes if it falls OOB
                        lastKnownOwners.Remove(passenger);
                        lastKnownOwners.Add(passenger, bagController);

                        if (NetworkServer.active)
                        {
                            // Remove from bag tracking
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
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
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[ProcessThrownObject] SERVER: Removed {passengerName} (netId={passengerNetId.netId.Value}) from network state");
                            }

                            // Send explicit bag state update to all clients IMMEDIATELY
                            // This ensures clients sync up when the throw is processed, not on impact
                            Networking.CycleNetworkHandler.SendBagStateUpdate(bagController, passengerNetId.netId, isThrowOperation: true);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[ProcessThrownObject] SERVER: Sent bag state update for thrown {passengerName}");
                        }
                        else
                        {
                            Log.Warning($"[ProcessThrownObject] {passengerName} does not have NetworkIdentity, cannot send state update");
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[ProcessThrownObject] CLIENT: Removing {passengerName} from local tracking");
                            BagPassengerManager.RemoveBaggedObject(bagController, passenger, isDestroying: false, skipStateReset: true);
                            Log.Info($"[ProcessThrownObject] CLIENT: {passengerName} throw processed, carousel updated");
                        }
                        else
                        {
                            BagPassengerManager.RemoveBaggedObject(bagController, passenger, isDestroying: false, skipStateReset: true);
                        }
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

        private static GameObject? FindTrackedObjectInHierarchy(GameObject obj)
        {
            if (obj == null) return null;
            Transform? current = obj.transform;
            while (current != null)
            {
                if (IsInProjectileState(current.gameObject)) return current.gameObject;
                current = current.parent;
            }
            return null;
        }

        [HarmonyPatch(typeof(MapZone), "TryZoneStart")]
        public class MapZone_TryZoneStart_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(MapZone __instance, Collider other)
            {
                if (__instance.zoneType != MapZone.ZoneType.OutOfBounds) return true;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[Recovery] MapZone triggered: {__instance.name} (ZoneLayer: {__instance.gameObject.layer}) | Object: {other.name} | ObjectLayer: {other.gameObject.layer}");

                var body = other.GetComponent<CharacterBody>();
                
                // First try direct check or hierarchy check for character/tracked object
                GameObject? target = (body != null) ? body.gameObject : FindTrackedObjectInHierarchy(other.gameObject);
                
                if (target != null)
                {
                    // Check if this object is in projectile state
                    if (IsInProjectileState(target))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Recovery] Tracked object {target.name} hit OOB zone {__instance.name}");

                        bool isEnemy = body != null && body.teamComponent && body.teamComponent.teamIndex != TeamIndex.Player;
                        
                        // If behavior is set to Kill for enemies, let vanilla handle it
                        if (isEnemy && PluginConfig.Instance.EnemyRecoveryMode.Value == EnemyRecoveryMode.Kill)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[Recovery] Letting vanilla handle OOB for enemy {body!.name} (Kill mode)");
                            return true;
                        }

                        // Intercept and recover
                        RecoverObject(target);
                        return false; // Prevent vanilla teleport/kill
                    }
                }
                else
                {
                    // Generic projectile recovery (e.g. scrap that fell off, or standard thrown object that isn't tracked)
                    var projectileController = other.GetComponent<ProjectileController>() ?? other.GetComponentInParent<ProjectileController>();
                    if (projectileController && !other.GetComponent<CharacterBody>())
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Recovery] Generic Projectile hit MapZone: {other.name} (Parent: {projectileController.name})");

                        RecoverProjectile(projectileController.gameObject);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "ImpactBehavior")]
        public class ThrownObjectProjectileController_ImpactBehavior_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance)
            {
                if (__instance.Networkpassenger != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[Recovery] ThrownObjectProjectileController impacted. Clearing throw state for {__instance.Networkpassenger.name}");

                    lock (_throwTrackingLock)
                    {
                        if (_objectsUndergoingThrow.Contains(__instance.Networkpassenger))
                        {
                            _objectsUndergoingThrow.Remove(__instance.Networkpassenger);
                        }
                    }
                    RemoveFromProjectileState(__instance.Networkpassenger);
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "OnDestroy")]
        public class ThrownObjectProjectileController_OnDestroy_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(ThrownObjectProjectileController __instance)
            {
                if (__instance.Networkpassenger != null)
                {
                    lock (_throwTrackingLock)
                    {
                        if (_objectsUndergoingThrow.Contains(__instance.Networkpassenger))
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[Recovery] ThrownObjectProjectileController destroyed. Safety clearing throw state for {__instance.Networkpassenger.name}");
                            _objectsUndergoingThrow.Remove(__instance.Networkpassenger);
                        }
                    }
                    RemoveFromProjectileState(__instance.Networkpassenger);
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
