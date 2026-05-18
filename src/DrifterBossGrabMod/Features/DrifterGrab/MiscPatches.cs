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

        private static readonly FieldInfo _sphereSearchField = ReflectionCache.HackingMainState.SphereSearch;

        [HarmonyPatch(typeof(HackingMainState), "ScanForTarget")]
        public class HackingMainState_ScanForTarget_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(HackingMainState __instance)
            {

                if (_sphereSearchField != null)
                {
                    var sphereSearch = (SphereSearch)_sphereSearchField.GetValue(__instance);
                    if (sphereSearch != null && __instance.transform != null)
                    {
                        sphereSearch.origin = __instance.transform.position;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "EjectPassengerToFinalPosition")]
        public class ThrownObjectProjectileController_EjectPassengerToFinalPosition_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThrownObjectProjectileController __instance)
            {
                Log.Debug($"[EjectPassenger] CALLED for {__instance.name} | Passenger: {(__instance.Networkpassenger != null ? __instance.Networkpassenger.name : "null")} | Server: {UnityEngine.Networking.NetworkServer.active}");

                return true;
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "CheckForDeadPassenger")]
        public class ThrownObjectProjectileController_CheckForDeadPassenger_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThrownObjectProjectileController __instance)
            {
                try
                {

                    var passenger = __instance.Networkpassenger;
                    if (passenger == null)
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[PassengerPatch] Failed to check passenger: {ex.Message}");
                    return false;
                }
                return true;
            }
        }
    }
}
