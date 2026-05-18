using RoR2;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch]
    public static class TeleporterSafetyPatches
    {

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(OutsideInteractableLocker), "FixedUpdate")]
        public static Exception? LockerFixedUpdateFinalizer(Exception? __exception)
        {
            return null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(OutsideInteractableLocker), "OnDisable")]
        public static Exception? LockerOnDisableFinalizer(Exception? __exception)
        {
            return null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(OutsideInteractableLocker), "UnlockAll")]
        public static Exception? LockerUnlockAllFinalizer(Exception? __exception)
        {
            return null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BossGroup), nameof(BossGroup.DropRewards))]
        public static void BossGroupDropRewardsPrefix(BossGroup __instance)
        {
            if (!NetworkServer.active) return;

            if (ReflectionCache.BossGroup.rng == null) return;

            if (__instance.dropTable == null || ReflectionCache.BossGroup.rng.GetValue(__instance) == null)
            {
                Log.Info($"[BossGroupSafety] {__instance.name} is missing critical reward data: " +
                         $"dropTable={(__instance.dropTable != null)}, " +
                         $"rng={(ReflectionCache.BossGroup.rng.GetValue(__instance) != null)}");

                if (__instance.dropTable == null)
                {
                    __instance.dropTable = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<PickupDropTable>("RoR2/Base/Common/dtTier2Item.asset").WaitForCompletion();
                }

                if (ReflectionCache.BossGroup.rng.GetValue(__instance) == null)
                {
                    ulong seed = (Run.instance?.bossRewardRng != null) ? Run.instance.bossRewardRng.nextUlong : (ulong)System.DateTime.Now.Ticks;
                    ReflectionCache.BossGroup.rng.SetValue(__instance, new Xoroshiro128Plus(seed));
                }

                Log.Info($"[BossGroupSafety] Fallback injection complete for {__instance.name}.");
            }
        }

    }
}
