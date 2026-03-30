#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using EntityStates.CaptainSupplyDrop;
using UnityEngine;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class MiscPatches
    {
        // Cached reflection fields
        private static readonly FieldInfo _sphereSearchField = ReflectionCache.HackingMainState.SphereSearch;

        [HarmonyPatch(typeof(HackingMainState), "FixedUpdate")]
        public class HackingMainState_FixedUpdate_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(HackingMainState __instance)
            {
                // Update the search origin to follow the beacon's current position
                if (_sphereSearchField != null)
                {
                    var sphereSearch = (SphereSearch)_sphereSearchField.GetValue(__instance);
                    var transform = __instance.transform;
                    if (sphereSearch != null && transform != null && sphereSearch.origin != transform.position)
                    {
                        sphereSearch.origin = transform.position;
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
                    return false; // Skip the method on client
                }
                return true; // Allow the method on server
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
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[PassengerPatch] Failed to check passenger: {ex.Message}\n{ex.StackTrace}");
                    return false;
                }
                return true;
            }
        }
    }
}
