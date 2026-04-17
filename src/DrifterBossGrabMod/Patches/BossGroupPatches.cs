using RoR2;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch(typeof(BossGroup))]
    public static class BossGroupPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(BossGroup.OnDefeatedServer))]
        public static void OnDefeatedServerPrefix(BossGroup __instance)
        {
            var members = __instance.combatSquad?.readOnlyMembersList;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BossGroup.DropRewards))]
        public static void DropRewardsPrefix(BossGroup __instance)
        {
            if (!NetworkServer.active) return;
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

            int players = Run.instance ? Run.instance.participatingPlayerCount : -1;
            
            // Collect reflection/private field counts safely if we need them
            var rng = Traverse.Create(__instance).Field("rng").GetValue();
            var bossDrops = Traverse.Create(__instance).Field("bossDrops").GetValue() as System.Collections.IList;
            var bossDropTables = Traverse.Create(__instance).Field("bossDropTables").GetValue() as System.Collections.IList;
            if (rng == null)
            {
                Log.Warning($"[BossGroupDiagnostics] ALERT: RNG is null! Rewards will abort in vanilla code.");
            }
            if (__instance.dropPosition == null)
            {
                Log.Warning($"[BossGroupDiagnostics] ALERT: dropPosition is null! No item will be spawned.");
            }
            
            // fail-safe
            if (__instance.dropTable == null && NetworkServer.active)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Warning($"[BossGroup] BossGroup {__instance.name} has no dropTable! Injecting dtTier2Item fallback.");
                __instance.dropTable = Addressables.LoadAssetAsync<PickupDropTable>("RoR2/Base/Common/dtTier2Item.asset").WaitForCompletion();
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
