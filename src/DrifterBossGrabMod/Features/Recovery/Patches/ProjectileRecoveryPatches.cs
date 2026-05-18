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
        private static readonly HashSet<GameObject> _objectsUndergoingThrow = new HashSet<GameObject>();
        private static readonly object _throwTrackingLock = new object();

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, DrifterBagController> lastKnownOwners = new System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, DrifterBagController>();

        private static readonly FieldInfo _projectileControllerField = ReflectionCache.ThrownObjectProjectileController.ProjectileController;
        private static readonly MethodInfo _calculatePassengerFinalPositionMethod = ReflectionCache.ThrownObjectProjectileController.CalculatePassengerFinalPosition;

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

            if (PluginConfig.IsRecoveryBlacklisted(passenger.name))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[Recovery] {passenger.name} is blacklisted from recovery, letting vanilla handle");
                return;
            }

            var characterBody = passenger.GetComponent<RoR2.CharacterBody>();
            if (characterBody != null)
            {
                if (characterBody.isBoss || characterBody.isChampion)
                {

                    if (!PluginConfig.Instance.RecoverBaggedBosses.Value)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Recovery] Boss recovery disabled for {passenger.name}, letting vanilla handle");
                        return;
                    }
                }
                else
                {

                    if (!PluginConfig.Instance.RecoverBaggedNPCs.Value)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Recovery] NPC recovery disabled for {passenger.name}, letting vanilla handle");
                        return;
                    }
                }
            }
            else
            {

                if (!PluginConfig.Instance.RecoverBaggedEnvironmentObjects.Value)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[Recovery] Environment object recovery disabled for {passenger.name}, letting vanilla handle");
                    return;
                }
            }

            DrifterBagController? bagController = null;
            var specialAttrs = passenger.GetComponent<SpecialObjectAttributes>();
            if (specialAttrs != null)
            {

                foreach (var controller in BagPatches.GetAllControllers())
                {
                    if (BagHelpers.IsBaggedObject(controller, passenger))
                    {
                        bagController = controller;
                        break;
                    }
                }
            }

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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[Recovery] Recovering {passenger.name} for {bagController.characterBody.name}");
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[Recovery] Attempting to recover bagged object: {passenger.name}");
            }

            Vector3 teleportPos = bagController.characterBody.corePosition + bagController.characterBody.transform.forward * ProjectileRecovery.TeleportForwardDistance + Vector3.up * ProjectileRecovery.TeleportUpDistance;

            if (Run.instance)
            {
                teleportPos = Run.instance.FindSafeTeleportPosition(bagController.characterBody, bagController.transform, 0f, 100f);
            }

            BagPassengerManager.RemoveBaggedObject(bagController, passenger);

            passenger.transform.position = teleportPos;
            RemoveFromProjectileState(passenger);

            var bagState = BagPatches.GetState(bagController);
            if (bagState != null && bagState.DisabledCollidersByObject.TryGetValue(passenger, out var states))
            {
                BodyColliderCache.RestoreMovementColliders(states);
                bagState.DisabledCollidersByObject.Remove(passenger, out _);
            }

            lock (_throwTrackingLock)
            {
                if (_objectsUndergoingThrow.Contains(passenger))
                {
                    _objectsUndergoingThrow.Remove(passenger);
                }
            }
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
                    Log.Warning("[ThrownObjectProjectileController_OnSyncPassenger] passengerObject is null");
                    return;
                }

                if (!NetworkUtils.ValidateObjectReady(passengerObject))
                {
                    Log.Warning($"[ThrownObjectProjectileController_OnSyncPassenger] {passengerObject.name} is not ready for network operations");
                    return;
                }

                lock (_projectileStateLock)
                {
                    if (projectileStateObjects.Contains(passengerObject))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[ThrownObjectProjectileController_OnSyncPassenger] {passengerObject.name} already processed, skipping");
                        return;
                    }
                }

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

            lock (_throwTrackingLock)
            {
                _objectsUndergoingThrow.Add(passenger);
            }

            lock (_projectileStateLock) { projectileStateObjects.Add(passenger); }

            int targetLayer = 0;
            __instance.gameObject.layer = targetLayer;
            foreach (var transform in __instance.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = targetLayer;
            }

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

                    lastKnownOwners.Remove(passenger);
                    lastKnownOwners.Add(passenger, bagController);

                    if (NetworkServer.active)
                    {

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[ProcessThrownObject] SERVER: Removing {passengerName} from bag tracking (throw operation)");

                        BaggedObjectStatePatches.PerformPassengerRestoration(bagController, passenger);

                        BagPassengerManager.RemoveBaggedObject(bagController, passenger);
                        PersistenceObjectsTracker.UntrackBaggedObject(passenger, isDestroying: false);

                        var passengerNetId = passenger.GetComponent<UnityEngine.Networking.NetworkIdentity>();
                        if (passengerNetId != null)
                        {

                            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                            if (netController != null)
                            {
                                netController.RemoveBaggedObjectId(passengerNetId.netId);
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[ProcessThrownObject] SERVER: Removed {passengerName} (netId={passengerNetId.netId.Value}) from network state");
                            }

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
                        var bagState = BagPatches.GetState(bagController);
                        bool alreadyRemoved = bagState.BaggedObjects == null || !bagState.BaggedObjects.Contains(passenger);
                        if (!alreadyRemoved)
                        {
                            BaggedObjectStatePatches.PerformPassengerRestoration(bagController, passenger);
                            BagPassengerManager.RemoveBaggedObject(bagController, passenger, isDestroying: false, skipStateReset: true, preserveStateDuringThrow: true);
                        }
                        PersistenceObjectsTracker.UntrackBaggedObject(passenger, isDestroying: false);
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

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[Recovery] MapZone triggered: {__instance.name} (ZoneLayer: {__instance.gameObject.layer}) | Object: {other.name} | ObjectLayer: {other.gameObject.layer}");

                var body = other.GetComponent<CharacterBody>();

                GameObject? target = (body != null) ? body.gameObject : FindTrackedObjectInHierarchy(other.gameObject);

                if (target != null)
                {

                    if (IsInProjectileState(target))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Recovery] Tracked object {target.name} hit OOB zone {__instance.name}");

                        if (PluginConfig.IsRecoveryBlacklisted(target.name))
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[Recovery] {target.name} is blacklisted from recovery, letting vanilla handle");
                            return true;
                        }

                        bool isEnemy = body != null && body.teamComponent && body.teamComponent.teamIndex != TeamIndex.Player;

                        if (isEnemy && PluginConfig.Instance.EnemyRecoveryMode.Value == EnemyRecoveryMode.Kill)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[Recovery] Letting vanilla handle OOB for enemy {body!.name} (Kill mode)");
                            return true;
                        }

                        if (body != null)
                        {
                            if (body.isBoss || body.isChampion)
                            {

                                if (!PluginConfig.Instance.RecoverBaggedBosses.Value)
                                {
                                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        Log.Info($"[Recovery] Boss recovery disabled for {body.name}, letting vanilla handle");
                                    return true;
                                }
                            }
                            else
                            {

                                if (!PluginConfig.Instance.RecoverBaggedNPCs.Value)
                                {
                                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        Log.Info($"[Recovery] NPC recovery disabled for {body.name}, letting vanilla handle");
                                    return true;
                                }
                            }
                        }
                        else
                        {

                            if (!PluginConfig.Instance.RecoverBaggedEnvironmentObjects.Value)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[Recovery] Environment object recovery disabled for {target.name}, letting vanilla handle");
                                return true;
                            }
                        }

                        RecoverObject(target);
                        return false;
                    }
                }
                else
                {

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
            [HarmonyPrefix]
            public static void Prefix(ThrownObjectProjectileController __instance)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value && __instance.Networkpassenger != null)
                {
                    var passenger = __instance.Networkpassenger;
                    Log.Info($"[Impact.Prefix] Projectile: {__instance.name} | Passenger: {passenger.name}");
                    Log.Info($"  Proj Pos: {__instance.transform.position}");
                    Log.Info($"  Pass Pos: {passenger.transform.position}");
                    Log.Info($"  Pass Parent: {(passenger.transform.parent ? passenger.transform.parent.name : "null")}");

                    try
                    {
                        var parameters = new object[2];
                        _calculatePassengerFinalPositionMethod.Invoke(__instance, parameters);
                        var calculatedPos = (Vector3)parameters[0];
                        var calculatedRot = (Quaternion)parameters[1];
                        Log.Info($"  Calculated Final Pos: {calculatedPos}, Rot: {calculatedRot}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"  Failed to invoke CalculatePassengerFinalPosition: {(ex.InnerException != null ? ex.InnerException.Message : ex.Message)}");
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance)
            {
                if (__instance.Networkpassenger != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        var passenger = __instance.Networkpassenger;
                        Log.Info($"[Impact.Postfix] Projectile: {__instance.name} | Passenger: {passenger.name}");
                        Log.Info($"  Final Pass Pos: {passenger.transform.position}");
                        Log.Info($"  Final Pass Parent: {(passenger.transform.parent ? passenger.transform.parent.name : "null")}");
                        Log.Info($"[Recovery] ThrownObjectProjectileController impacted. Clearing throw state for {passenger.name}");
                    }

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

        // ========================================================================================
        // STATE MANAGEMENT
        // ========================================================================================
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
