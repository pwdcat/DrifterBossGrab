using RoR2;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch(typeof(BossGroup))]
    public static class BossGroupPatches
    {

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BossGroup.DropRewards))]
        public static void DropRewardsPrefix(BossGroup __instance)
        {
            if (!NetworkServer.active || !TeleporterSafetyPatches.IsRestored(__instance)) return;
            var modelLocator = __instance.GetComponent<ModelLocator>() ?? __instance.GetComponentInParent<ModelLocator>();
            if (modelLocator != null && modelLocator.modelTransform != null)
            {
                var pivot = SearchForPivot(modelLocator.modelTransform, "HologramPivot");
                if (pivot != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[BossGroupPatches] Redirecting rewards for {__instance.name} to {pivot.name} at {pivot.position}");
                    __instance.dropPosition = pivot;
                }
            }

            if (__instance.dropTable == null)
                __instance.dropTable = LegacyResourcesAPI.Load<PickupDropTable>("DropTables/dtTier2Item");
            var rng = ReflectionCache.BossGroup.rng;
            if (rng != null && rng.GetValue(__instance) == null)
            {
                ulong seed = Run.instance?.bossRewardRng != null ? Run.instance.bossRewardRng.nextUlong : (ulong)System.DateTime.Now.Ticks;
                rng.SetValue(__instance, new Xoroshiro128Plus(seed));
            }
        }

        private static Transform? SearchForPivot(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = SearchForPivot(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
