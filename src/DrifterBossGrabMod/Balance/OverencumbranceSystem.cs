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

        // Calculates overencumbrance percentage
        public static float CalculateOverencumbrancePercent(float totalMass, float massCapacity)
        {
            if (massCapacity <= 0) return 0f;

            float capacityRatio = totalMass / massCapacity;
            // Only apply overencumbrance settings when EnableBalance is true
            float maxOverencumbrancePercent = PluginConfig.Instance.EnableBalance.Value
                ? PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor
                : 0f;
            float overencumbrancePercent = Mathf.Clamp(capacityRatio - Constants.Multipliers.CapacityRatioThreshold, 0f, maxOverencumbrancePercent);

            return overencumbrancePercent;
        }

        // Applies overencumbrance debuff to a character body
        public static void ApplyOverencumbrance(CharacterBody body, DrifterBagController bagController)
        {
            if (body == null || bagController == null) return;

            // Get current total mass
            float totalMass = GetTotalMass(bagController);

            // Calculate mass capacity
            float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(bagController);

            // Calculate overencumbrance
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

        // Starts a coroutine to remove the overencumbrance debuff after a delay
        private static void StartRemovalTimer(CharacterBody body)
        {
            if (body == null) return;

            // Stop any existing timer for this body
            StopRemovalTimer(body);

            // Start new timer coroutine
            Coroutine timerCoroutine = body.StartCoroutine(RemovalTimerCoroutine(body));
            _overencumbranceTimers[body] = timerCoroutine;
        }

        // Stops the removal timer for a character body
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

        // Coroutine that waits for the delay then removes the debuff
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

        // Applies TransferDebuffOnHit debuff to a character body
        private static void ApplyTransferDebuff(CharacterBody body)
        {
            if (body == null) return;

            // Get debuff buff def
            BuffDef transferDebuff = DLC3Content.Buffs.TransferDebuffOnHit;

            if (transferDebuff != null)
            {
                // Add debuff (infinite duration, will be removed manually)
                body.AddBuff(transferDebuff);
            }
        }

        // Removes TransferDebuffOnHit debuff from a character body
        private static void RemoveTransferDebuff(CharacterBody body)
        {
            if (body == null) return;

            // Remove debuff
            body.RemoveBuff(DLC3Content.Buffs.TransferDebuffOnHit);
        }

        // Gets total mass of all bagged objects
        private static float GetTotalMass(DrifterBagController bagController)
        {
            float totalMass = 0f;

            if (bagController == null) return totalMass;

            var baggedObjects = BagPatches.GetState(bagController).BaggedObjects;
            if (baggedObjects != null)
            {
                foreach (var obj in baggedObjects)
                {
                    if (obj != null && !OtherPatches.IsInProjectileState(obj))
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

        // Checks if an object can be grabbed based on overencumbrance
        public static bool CanGrabObject(DrifterBagController bagController, float objectMass)
        {
            if (bagController == null) return true;

            // Get current total mass
            float totalMass = GetTotalMass(bagController);

            // Calculate mass capacity
            float massCapacity = Balance.CapacityScalingSystem.CalculateMassCapacity(bagController);

            // Calculate new total mass
            float newTotalMass = totalMass + objectMass;

            // Calculate overencumbrance
            float overencumbrancePercent = CalculateOverencumbrancePercent(newTotalMass, massCapacity);
            // Only apply overencumbrance settings when EnableBalance is true
            float maxOverencumbrancePercent = PluginConfig.Instance.EnableBalance.Value
                ? PluginConfig.Instance.OverencumbranceMaxPercent.Value / Constants.Multipliers.PercentageDivisor
                : 0f;

            // Check if would exceed max overencumbrance
            if (overencumbrancePercent >= maxOverencumbrancePercent)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[Overencumbrance] Cannot grab: Would exceed max overencumbrance (Current={totalMass}, New={newTotalMass}, Capacity={massCapacity})");
                }
                return false;
            }

            return true;
        }

        // Cleans up overencumbrance timers when a character body is destroyed
        public static void CleanupCharacterBody(CharacterBody body)
        {
            if (body != null && _overencumbranceTimers.ContainsKey(body))
            {
                StopRemovalTimer(body);
            }
        }
    }
}
