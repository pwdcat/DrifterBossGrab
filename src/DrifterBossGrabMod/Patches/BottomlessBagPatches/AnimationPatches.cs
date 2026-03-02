using System;
using HarmonyLib;
using EntityStates;
using RoR2;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch]
    public static class AnimationPatches
    {
        [HarmonyPatch(typeof(EntityStates.EntityState), "PlayCrossfade", new Type[] { typeof(string), typeof(string), typeof(string), typeof(float), typeof(float) })]
        [HarmonyPrefix]
        public static bool PlayCrossfade_Prefix(EntityStates.EntityState __instance, string layerName, string animationStateName, string playbackRateParam, float duration, float crossfadeDuration)
        {
            if (DrifterBossGrabPlugin.IsSwappingPassengers && !PluginConfig.Instance.PlayAnimationOnCycle.Value)
            {
                if (__instance is EntityStates.Drifter.Bag.BaggedObject)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[AnimationPatches] Suppressing PlayCrossfade({layerName}, {animationStateName}, {playbackRateParam}, {duration}, {crossfadeDuration}) during cycle.");
                    }
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(EntityStates.EntityState), "PlayCrossfade", new Type[] { typeof(string), typeof(string), typeof(float) })]
        [HarmonyPrefix]
        public static bool PlayCrossfadeShort_Prefix(EntityStates.EntityState __instance, string layerName, string animationStateName, float crossfadeDuration)
        {
            if (DrifterBossGrabPlugin.IsSwappingPassengers && !PluginConfig.Instance.PlayAnimationOnCycle.Value)
            {
                if (__instance is EntityStates.Drifter.Bag.BaggedObject)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[AnimationPatches] Suppressing PlayCrossfade({layerName}, {animationStateName}, {crossfadeDuration}) during cycle.");
                    }
                    return false;
                }
            }
            return true;
        }
    }
}
