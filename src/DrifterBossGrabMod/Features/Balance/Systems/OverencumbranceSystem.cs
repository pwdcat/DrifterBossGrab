#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Balance
{
    // System for managing overencumbrance penalties
    public static class OverencumbranceSystem
    {
        private const float OverencumbranceDebuffRemovalDelay = Constants.Timeouts.OverencumbranceDebuffRemovalDelay; // Seconds to wait before removing debuff
        private static readonly Dictionary<CharacterBody, Coroutine> _overencumbranceTimers = new Dictionary<CharacterBody, Coroutine>();


        public static float CalculateOverencumbrancePercent(float totalMass, float massCapacity)
        {
            if (massCapacity <= 0) return 0f;

            float capacityRatio = totalMass / massCapacity;
            // Overencumbrance cap only applies when balance system is enabled
            float maxOverencumbrancePercent = PluginConfig.Instance.EnableBalance.Value
                ? PluginConfig.Instance.OverencumbranceMax.Value / Constants.Multipliers.PercentageDivisor
                : 0f;
            float overencumbrancePercent = Mathf.Clamp(capacityRatio - Constants.Multipliers.CapacityRatioThreshold, 0f, maxOverencumbrancePercent);

            return overencumbrancePercent;
        }


        public static void ApplyOverencumbrance(CharacterBody body, DrifterBagController bagController)
        {
            if (body == null || bagController == null) return;

            float totalMass = GetTotalMass(bagController);

            float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(bagController);

            float overencumbrancePercent = CalculateOverencumbrancePercent(totalMass, massCapacity);

            if (overencumbrancePercent <= 0)
            {
                // Not overencumbered - start removal timer if debuff is active
                if (body.HasBuff(DLC3Content.Buffs.TransferDebuffOnHit))
                {
                    StartRemovalTimer(body);

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[Overencumbrance] Started removal timer for debuff");
                    }
                }
                return;
            }

            // Overencumbered - remove timer if it exists and apply debuff
            StopRemovalTimer(body);

            // Apply TransferDebuffOnHit debuff (only if not already applied)
            if (!body.HasBuff(DLC3Content.Buffs.TransferDebuffOnHit))
            {
                ApplyTransferDebuff(body);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[Overencumbrance] Applied debuff: Overencumbrance%={overencumbrancePercent:P1}");
                }
            }
        }


        private static void StartRemovalTimer(CharacterBody body)
        {
            if (body == null) return;

            StopRemovalTimer(body);

            Coroutine timerCoroutine = body.StartCoroutine(RemovalTimerCoroutine(body));
            _overencumbranceTimers[body] = timerCoroutine;
        }


        private static void StopRemovalTimer(CharacterBody body)
        {
            if (body == null) return;

            if (_overencumbranceTimers.TryGetValue(body, out Coroutine timerCoroutine))
            {
                if (timerCoroutine != null)
                {
                    body.StopCoroutine(timerCoroutine);
                    _overencumbranceTimers.Remove(body);

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[Overencumbrance] Stopped removal timer for {body.name}");
                    }
                }
            }
        }


        private static IEnumerator RemovalTimerCoroutine(CharacterBody body)
        {
            yield return new WaitForSeconds(OverencumbranceDebuffRemovalDelay);

            // Remove debuff if the body still has it and is no longer overencumbered
            if (body != null && body.HasBuff(DLC3Content.Buffs.TransferDebuffOnHit))
            {
                RemoveTransferDebuff(body);
                _overencumbranceTimers.Remove(body);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[Overencumbrance] Removed debuff after {OverencumbranceDebuffRemovalDelay:F1}s");
                }
            }
        }


        private static void ApplyTransferDebuff(CharacterBody body)
        {
            if (body == null) return;

            BuffDef transferDebuff = DLC3Content.Buffs.TransferDebuffOnHit;

            if (transferDebuff != null)
            {
                // Add debuff (infinite duration, will be removed manually)
                body.AddBuff(transferDebuff);
            }
        }


        private static void RemoveTransferDebuff(CharacterBody body)
        {
            if (body == null) return;

            body.RemoveBuff(DLC3Content.Buffs.TransferDebuffOnHit);
        }


        private static float GetTotalMass(DrifterBagController bagController)
        {
            float totalMass = 0f;

            if (bagController == null) return totalMass;

            var baggedObjects = BagPatches.GetState(bagController).BaggedObjects;
            if (baggedObjects != null)
            {
                foreach (var obj in baggedObjects)
                {
                    if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                    {
                        totalMass += bagController.CalculateBaggedObjectMass(obj);
                    }
                }
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[Overencumbrance] GetTotalMass: {totalMass} (from {baggedObjects?.Count ?? 0} objects)");
            }

            return totalMass;
        }





        public static void CleanupCharacterBody(CharacterBody body)
        {
            if (body != null && _overencumbranceTimers.ContainsKey(body))
            {
                StopRemovalTimer(body);
            }
        }
    }
}
