#nullable enable
using RoR2;
using HarmonyLib;
using DrifterBossGrabMod.Core;
using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    public static class CombatDirectorPatches
    {
        private static readonly HashSet<CombatDirector> _restoringTeleporterDirectors = new HashSet<CombatDirector>();

        public static void MarkTeleporterDirectorAsRestoring(CombatDirector director)
        {
            if (director != null)
            {
                _restoringTeleporterDirectors.Add(director);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Debug($"[CombatDirectorPatches] Marked {director.name} as restoring (total: {_restoringTeleporterDirectors.Count})");
            }
        }

        public static void ClearTeleporterDirectorRestoring(CombatDirector director)
        {
            if (director != null)
            {
                _restoringTeleporterDirectors.Remove(director);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Debug($"[CombatDirectorPatches] Cleared restoring flag for {director.name} (total: {_restoringTeleporterDirectors.Count})");
            }
        }

        [HarmonyPatch(typeof(CombatDirector), "OnDisable")]
        [HarmonyPrefix]
        private static bool OnDisablePrefix(CombatDirector __instance)
        {
            // Check if this is a teleporter director that's being restored from persistence
            if (_restoringTeleporterDirectors.Contains(__instance))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Debug($"[CombatDirectorPatches] Blocking vanilla credit transfer for {__instance.name} (restoring from persistence)");

                // Remove from instances list (part of normal OnDisable behavior)
                CombatDirector.instancesList.Remove(__instance);

                // Skip the original method to prevent credit transfer
                return false;
            }

            // Allow normal vanilla behavior for other directors
            return true;
        }
    }
}
