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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" IsSurvivorProjectile: projectileController or owner is null");
                }
                return false;
            }
            // Check CharacterBody directly on the owner GameObject
            var characterBody = projectileController.owner.GetComponent<CharacterBody>();
            bool isPlayerControlled = characterBody != null && characterBody.isPlayerControlled;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" IsSurvivorProjectile: owner={projectileController.owner.name}, hasCharacterBody={characterBody != null}, isPlayerControlled={isPlayerControlled}");
            }
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" ProjectileController_Start: __instance or gameObject is null");
                    }
                    return;
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" ProjectileController_Start: Processing projectile {__instance.gameObject.name}");
                }
                // Check if projectile grabbing is enabled
                if (!PluginConfig.Instance.EnableProjectileGrabbing.Value)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" ProjectileController_Start: Projectile grabbing is disabled");
                    }
                    return;
                }
                // Check if we should only grab survivor projectiles
                bool isSurvivorProjectile = IsSurvivorProjectile(__instance);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" ProjectileController_Start: Is survivor projectile: {isSurvivorProjectile}");
                }
                if (PluginConfig.Instance.ProjectileGrabbingSurvivorOnly.Value && !isSurvivorProjectile)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" ProjectileController_Start: Skipping non-survivor projectile (survivor-only mode enabled)");
                    }
                    return;
                }
                // Check if projectile is blacklisted
                bool isBlacklisted = PluginConfig.IsBlacklisted(__instance.gameObject.name);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" ProjectileController_Start: Is blacklisted: {isBlacklisted}");
                }
                if (isBlacklisted)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" ProjectileController_Start: Skipping blacklisted projectile");
                    }
                    return;
                }
                // Check if already has SpecialObjectAttributes
                var existingSoa = __instance.gameObject.GetComponent<SpecialObjectAttributes>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" ProjectileController_Start: Already has SpecialObjectAttributes: {existingSoa != null}");
                    if (existingSoa != null)
                    {
                        Log.Info($" ProjectileController_Start: SOA grabbable: {existingSoa.grabbable}, bestName: {existingSoa.bestName}");
                    }
                }
                // Add SpecialObjectAttributes to make the projectile grabbable
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" ProjectileController_Start: Calling AddSpecialObjectAttributesToProjectile");
                }
                Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToProjectile(__instance.gameObject);
                // Check if it worked
                var newSoa = __instance.gameObject.GetComponent<SpecialObjectAttributes>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    string projectileType = PluginConfig.Instance.ProjectileGrabbingSurvivorOnly.Value ? "survivor" : "any";
                    Log.Info($" Added SpecialObjectAttributes to {projectileType} projectile: {__instance.gameObject.name}, success: {newSoa != null}");
                    if (newSoa != null)
                    {
                        Log.Info($" SOA details - grabbable: {newSoa.grabbable}, mass: {newSoa.massOverride}, durability: {newSoa.maxDurability}, bestName: {newSoa.bestName}");
                    }
                }
            }
        }
    }
}
