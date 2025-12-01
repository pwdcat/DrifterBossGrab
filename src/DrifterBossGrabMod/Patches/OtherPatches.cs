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

                bool isInAnySafeZone = false;

                foreach (MapZone zone in mapZones)
                {
                    if (zone.zoneType == MapZone.ZoneType.OutOfBounds && zone.IsPointInsideMapZone(projectilePos))
                    {
                        isInAnySafeZone = true;
                        break;
                    }
                }

                // If projectile is NOT in any safe zone, it's out of bounds and should be recovered
                if (!isInAnySafeZone)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile impacted OUTSIDE safe zones at {projectilePos}, recovering {passenger.name}");
                    }

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

                bool isInAnySafeZone = false;

                foreach (MapZone zone in mapZones)
                {
                    if (zone.zoneType == MapZone.ZoneType.OutOfBounds && zone.IsPointInsideMapZone(projectilePos))
                    {
                        isInAnySafeZone = true;
                        break;
                    }
                }

                // If projectile is NOT in any safe zone, it's out of bounds and should be recovered
                if (!isInAnySafeZone)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile at {projectilePos} is OUTSIDE all safe zones, recovering {thrownController.Networkpassenger.name}");
                    }

                    // Get the player who threw the projectile
                    var projectileController = Traverse.Create(thrownController).Field("projectileController").GetValue<RoR2.Projectile.ProjectileController>();
                    GameObject owner = projectileController?.owner;
                    if (owner != null)
                    {
                        Vector3 playerPos = owner.transform.position;
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Recovering {thrownController.Networkpassenger.name} to player position {playerPos}");
                        }

                        // Restore states
                        var grabbedState = thrownController.Networkpassenger.GetComponent<GrabbedObjectState>();
                        if (grabbedState != null)
                        {
                            grabbedState.RestoreAllStates();
                        }

                        // Teleport passenger to a position in front of the player
                        Vector3 teleportPos = owner.transform.position + owner.transform.forward * 4f + Vector3.up * 2f;
                        thrownController.Networkpassenger.transform.position = teleportPos;

                        // Optionally reset rotation to upright if configured
                        if (PluginConfig.EnableUprightRecovery.Value)
                        {
                            thrownController.Networkpassenger.transform.rotation = Quaternion.identity;
                        }

                        // Destroy the projectile
                        UnityEngine.Object.Destroy(thrownController.gameObject);
                    }
                }
            }
        }
    }
}