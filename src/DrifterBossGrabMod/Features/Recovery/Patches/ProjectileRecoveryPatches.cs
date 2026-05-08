#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.HudOverlay;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Networking;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // PROJECTILE RECOVERY PATCHES
    // ========================================================================================

    public static class ProjectileRecoveryPatches
    {
        public static class ProjectileRecovery
        {
            public const float TeleportForwardDistance = 4f;
            public const float TeleportUpDistance = 2f;
            public const float RecoveryUpDistance = 2f;
        }

        internal static readonly HashSet<GameObject> projectileStateObjects = new HashSet<GameObject>();
        private static readonly object _projectileStateLock = new object();

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, DrifterBagController> lastKnownOwners = new System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, DrifterBagController>();

        // Cached reflection fields
        private static readonly FieldInfo _projectileControllerField = ReflectionCache.ThrownObjectProjectileController.ProjectileController;
        private static readonly MethodInfo _calculatePassengerFinalPositionMethod = ReflectionCache.ThrownObjectProjectileController.CalculatePassengerFinalPosition;

        // ========================================================================================
        // RECOVERY CORE
        // ========================================================================================

        public static bool RecoverObject(GameObject passenger)
        {
            if (passenger == null) return false;

            // Find the bag controller for this passenger
            DrifterBagController? bagController = null;
            foreach (var controller in API.DrifterBagAPI.GetAllControllers())
            {
                if (BagHelpers.IsBaggedObject(controller, passenger))
                {
                    bagController = controller;
                    break;
                }
            }

            // Fallback: If we couldn't find the bag controller via direct search
            if (bagController == null)
            {
                lastKnownOwners.TryGetValue(passenger, out bagController);
            }

            // Determine if we should perform mod-side recovery/kill
            bool canRecover = PluginConfig.Instance.EnableRecoveryFeature.Value && !PluginConfig.IsRecoveryBlacklisted(passenger.name);
            bool shouldKill = false;
            var characterBody = passenger.GetComponent<CharacterBody>();

            if (characterBody != null)
            {
                bool isEnemy = characterBody.teamComponent && characterBody.teamComponent.teamIndex != TeamIndex.Player;

                // Type-specific toggles
                if (characterBody.isBoss || characterBody.isChampion)
                {
                    if (!PluginConfig.Instance.RecoverBaggedBosses.Value) canRecover = false;
                }
                else
                {
                    if (!PluginConfig.Instance.RecoverBaggedNPCs.Value) canRecover = false;
                }

                if (isEnemy && PluginConfig.Instance.EnemyRecoveryMode.Value == EnemyRecoveryMode.Kill)
                {
                    shouldKill = true;
                }
            }
            else
            {
                if (!PluginConfig.Instance.RecoverBaggedEnvironmentObjects.Value) canRecover = false;
            }

            Log.DebugIfEnabled("[Recovery] Handling OOB/Orphan cleanup for {0} (shouldKill={1}, canRecover={2})",
                passenger.name, shouldKill, canRecover);

            bool modHandledTeleportOrKill = false;

            if (shouldKill)
            {
                Log.DebugIfEnabled("[Recovery] Killing {0} (Kill mode)", passenger.name);
                characterBody?.healthComponent?.Suicide();
                modHandledTeleportOrKill = true;
            }
            else if (canRecover)
            {
                if (bagController != null && bagController.characterBody != null)
                {
                    Vector3 teleportPos = bagController.characterBody.corePosition + bagController.characterBody.transform.forward * ProjectileRecovery.TeleportForwardDistance + Vector3.up * ProjectileRecovery.TeleportUpDistance;
                    if (Run.instance)
                    {
                        teleportPos = Run.instance.FindSafeTeleportPosition(bagController.characterBody, bagController.transform, 0f, 100f);
                    }
                    Log.DebugIfEnabled("[Recovery] Teleporting {0} to safe spot {1}", passenger.name, teleportPos);
                    passenger.transform.position = teleportPos;
                    modHandledTeleportOrKill = true;
                }
            }

            // Always perform state cleanup regardless of whether we teleported/killed
            RemoveFromProjectileState(passenger);

            if (bagController != null)
            {
                BagPassengerManager.RemoveBaggedObject(bagController, passenger);
            }

            // Restore state and components (re-enables teleporter interaction, hurtboxes, etc.)
            BaggedObjectStatePatches.PerformPassengerRestoration(bagController, passenger, force: true);

            return modHandledTeleportOrKill;
        }

        // ========================================================================================
        // THROWN OBJECT PATCHES
        // ========================================================================================

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "OnSyncPassenger")]
        public class ThrownObjectProjectileController_OnSyncPassenger_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance, GameObject passengerObject)
            {
                if (passengerObject == null)
                {
                    Log.DebugIfEnabled("[ThrownObjectProjectileController_OnSyncPassenger] passengerObject is null");
                    return;
                }

                // Validate that the passenger object is ready
                if (!NetworkUtils.ValidateObjectReady(passengerObject))
                {
                    Log.DebugIfEnabled($"[ThrownObjectProjectileController_OnSyncPassenger] {passengerObject.name} is not ready for network operations");
                    return;
                }

                // Check if we've already processed this passenger
                lock (_projectileStateLock)
                {
                    if (projectileStateObjects.Contains(passengerObject))
                    {
                        Log.DebugIfEnabled("[ThrownObjectProjectileController_OnSyncPassenger] {0} already processed, skipping", passengerObject.name);
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

            // Track this object as being in projectile state
            lock (_projectileStateLock) { projectileStateObjects.Add(passenger); }
            var proxyObj = new GameObject("MapZoneProxy");
            proxyObj.transform.SetParent(__instance.transform, false);
            proxyObj.transform.localPosition = Vector3.zero;
            proxyObj.layer = 0;

            var trigger = proxyObj.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.0f;

            // Ignore collision with the passenger (the enemy being thrown).
            var passengerColliders = passenger.GetComponentsInChildren<Collider>();
            var projectileColliders = __instance.GetComponentsInChildren<Collider>();
            foreach (var pc in projectileColliders)
            {
                if (pc == null) continue;
                foreach (var passC in passengerColliders)
                {
                    if (passC == null) continue;
                    Physics.IgnoreCollision(pc, passC, true);
                }
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
                        Log.DebugIfEnabled("[ProcessThrownObject] server: Removing {0} from bag tracking (throw operation)", passengerName);

                        // Restore hitboxes/state before launching (crucial for additional seats)
                        BaggedObjectStatePatches.PerformPassengerRestoration(bagController, passenger, force: true);

                        BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                        PersistenceObjectsTracker.UntrackBaggedObject(passenger, isDestroying: false);

                        // Get NetworkIdentity of thrown object
                        var passengerNetId = passenger.GetComponent<UnityEngine.Networking.NetworkIdentity>();
                        if (passengerNetId != null)
                        {
                            // Explicitly remove from network controller's bagged IDs before sending message
                            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                            if (netController != null)
                            {
                                netController.RemoveBaggedObjectId(passengerNetId.netId);
                                Log.DebugIfEnabled("[ProcessThrownObject] server: Removed {0} (netId={1}) from network state", passengerName, passengerNetId.netId.Value);
                            }

                            Networking.CycleNetworkHandler.SendBagStateUpdate(bagController, passengerNetId.netId, isThrowOperation: true);
                            Log.DebugIfEnabled("[ProcessThrownObject] server: Sent bag state update for thrown {0}", passengerName);
                        }
                        else
                        {
                            Log.DebugIfEnabled($"[ProcessThrownObject] {passengerName} does not have NetworkIdentity, cannot send state update");
                        }
                    }
                    else
                    {
                        BaggedObjectStatePatches.PerformPassengerRestoration(bagController, passenger, force: true);
                        BagPassengerManager.RemoveBaggedObject(bagController, passenger, isDestroying: false, skipStateReset: true, preserveStateDuringThrow: true);
                        PersistenceObjectsTracker.UntrackBaggedObject(passenger, isDestroying: false);
                    }
                }
                else
                {
                    Log.DebugIfEnabled($"[ProcessThrownObject] Owner does not have DrifterBagController component");
                }
            }
            else
            {
                Log.DebugIfEnabled($"[ProcessThrownObject] Projectile owner is null");
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

        // ========================================================================================
        // MAP ZONE PATCHES
        // ========================================================================================

        [HarmonyPatch(typeof(MapZone), "TryZoneStart")]
        public class MapZone_TryZoneStart_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(MapZone __instance, Collider other)
            {
                if (__instance.zoneType != MapZone.ZoneType.OutOfBounds) return true;

                Log.DebugIfEnabled("[Recovery] MapZone triggered: {0} (ZoneLayer: {1}) | Object: {2} | ObjectLayer: {3}",
                    __instance.name, __instance.gameObject.layer, other.name, other.gameObject.layer);

                var body = other.GetComponent<CharacterBody>();

                // First try direct check or hierarchy check for character/tracked object
                GameObject? target = (body != null) ? body.gameObject : FindTrackedObjectInHierarchy(other.gameObject);

                if (target != null)
                {
                    // Check if this object is in projectile state
                    if (IsInProjectileState(target))
                    {
                        Log.DebugIfEnabled("[Recovery] Tracked object {0} hit OOB zone {1}", target.name, __instance.name);

                        if (PluginConfig.IsRecoveryBlacklisted(target.name))
                        {
                            Log.DebugIfEnabled("[Recovery] {0} is blacklisted from recovery, letting vanilla handle", target.name);
                            return true;
                        }

                        // Let RecoverObject handle the logic (killing, teleporting, or just cleanup)
                        bool modHandled = RecoverObject(target);

                        // If the mod performed a teleport or kill, prevent vanilla from doing it again.
                        // If the mod only performed cleanup (because recovery is disabled for this type),
                        // return true to let vanilla handle the OOB event.
                        return !modHandled;
                    }
                }
                else
                {
                    // Generic projectile recovery (e.g. scrap that fell off, or standard thrown object that isn't tracked)
                    var projectileController = other.GetComponent<ProjectileController>() ?? other.GetComponentInParent<ProjectileController>();
                    if (projectileController && !other.GetComponent<CharacterBody>())
                    {
                        Log.DebugIfEnabled("[Recovery] Generic Projectile hit MapZone: {0} (Parent: {1})", other.name, projectileController.name);

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

                    RemoveFromProjectileState(__instance.Networkpassenger);

                    // Restore state and components (re-enables teleporter interaction, hurtboxes, etc.)
                    BaggedObjectStatePatches.PerformPassengerRestoration(null, __instance.Networkpassenger, force: true);
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

                    RemoveFromProjectileState(__instance.Networkpassenger);
                }
            }
        }

        // ========================================================================================
        // STATE MANAGEMENT
        // ========================================================================================

        public static bool IsInProjectileState(GameObject? obj)
        {
            if (obj == null) return false;

            lock (_projectileStateLock)
            {
                return projectileStateObjects.Contains(obj);
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

