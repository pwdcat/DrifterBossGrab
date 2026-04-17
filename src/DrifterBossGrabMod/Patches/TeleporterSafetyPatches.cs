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
        // OutsideInteractableLocker NRE Shield
        [HarmonyPrefix]
        [HarmonyPatch(typeof(OutsideInteractableLocker), nameof(OutsideInteractableLocker.FixedUpdate))]
        public static bool LockerFixedUpdatePrefix(OutsideInteractableLocker __instance)
        {
            if (!NetworkServer.active || __instance == null) return true;

            // Future-proofing kill switch: If Teleporter1 is blacklisted from persistence, skip all custom patches (might consider it as a feature)
            if (PluginConfig.IsPersistenceBlacklisted("Teleporter1"))
            {
                Log.Info("[TeleporterPatches] Skipping patches: Teleporter1 is blacklisted from persistence system.");
                return true;
            }

            // Manual tick logic to catch the MoveNext() crash
            try
            {
                var timerField = ReflectionCache.OutsideInteractableLocker.UpdateTimer;
                var coroutineField = ReflectionCache.OutsideInteractableLocker.CurrentCoroutine;

                if (timerField == null || coroutineField == null) return true;

                object? timerValue = timerField.GetValue(__instance);
                float updateTimer = (timerValue is float f) ? f : 0f;
                updateTimer -= Time.fixedDeltaTime;
                
                if (updateTimer <= 0f)
                {
                    timerField.SetValue(__instance, 0.1f); // updateInterval fallback
                    IEnumerator? enumerator = coroutineField.GetValue(__instance) as IEnumerator;
                    
                    if (enumerator != null)
                    {
                        enumerator.MoveNext();
                    }
                }
                else
                {
                    timerField.SetValue(__instance, updateTimer);
                }
            }
            catch (Exception)
            {
                // The coroutine will try again next tick.
            }

            return false; // Skip vanilla FixedUpdate
        }

        // BossGroup Reward Fallback
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BossGroup), nameof(BossGroup.DropRewards))]
        public static void BossGroupDropRewardsPrefix(BossGroup __instance)
        {
            if (!NetworkServer.active) return;
            if (PluginConfig.IsPersistenceBlacklisted("Teleporter1")) return;

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
