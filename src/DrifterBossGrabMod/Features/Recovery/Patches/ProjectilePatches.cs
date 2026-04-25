#nullable enable
using System;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
namespace DrifterBossGrabMod.Patches
{
    public static class ProjectilePatches
    {
        public static bool IsSurvivorProjectile(ProjectileController projectileController)
        {
            if (projectileController == null || projectileController.owner == null)
            {
                return false;
            }
            var characterBody = projectileController.owner.GetComponent<CharacterBody>();
            bool isPlayerControlled = characterBody != null && characterBody.isPlayerControlled;
            return isPlayerControlled;
        }
        [HarmonyPatch(typeof(ProjectileController), "Start")]
        public class ProjectileController_Start_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ProjectileController __instance)
            {
                if (__instance == null || __instance.gameObject == null)
                {
                    return;
                }
                var projectileMode = PluginConfig.Instance.ProjectileGrabbingMode.Value;
                if (projectileMode == ProjectileGrabbingMode.None)
                {
                    return;
                }
                bool isSurvivorProjectile = IsSurvivorProjectile(__instance);
                if (projectileMode == ProjectileGrabbingMode.SurvivorOnly && !isSurvivorProjectile)
                {
                    return;
                }
                bool isBlacklisted = PluginConfig.IsBlacklisted(__instance.gameObject.name);
                if (isBlacklisted)
                {
                    return;
                }
                var existingSoa = __instance.gameObject.GetComponent<SpecialObjectAttributes>();
                if (existingSoa != null)
                {
                    return;
                }
                var healthComponent = __instance.gameObject.GetComponent<HealthComponent>();
                if (healthComponent != null)
                {
                    return;
                }
                var characterBody = __instance.gameObject.GetComponent<CharacterBody>();
                if (characterBody != null)
                {
                    return;
                }
                Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToProjectile(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(ProjectileController), "OnDestroy")]
        public class ProjectileController_OnDestroy_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(ProjectileController __instance)
            {
                if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
                if (__instance == null) return;

                // Check if it's one of our tracked or survivor projectiles
                bool isSurvivor = IsSurvivorProjectile(__instance);
                string name = __instance.name;
                if (isSurvivor || name.Contains("Teleporter") || name.Contains("Shrine") || name.Contains("ThrownObjectProjectile"))
                {
                    var pos = __instance.transform.position;
                    Log.Info($"[Recovery] DESTRUCTION TRACE: {name} | Pos: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}) | Owner: {(__instance.owner != null ? __instance.owner.name : "null")}");
                }
            }
        }
    }
}
