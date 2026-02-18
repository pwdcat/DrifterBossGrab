using HarmonyLib;
using RoR2;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.UI;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch(typeof(DrifterBagController), nameof(DrifterBagController.CmdDamageBaggedObject))]
    public class CmdDamageBaggedObject_AoE
    {
        [HarmonyPrefix]
        public static void Prefix(DrifterBagController __instance, ref float damageCoef, out float __state)
        {
            // Default state to original damageCoef
            __state = damageCoef;

            if (!NetworkServer.active) return;
            // Only apply AoE slam damage when EnableBalance is true
            if (!PluginConfig.Instance.EnableBalance.Value) return;
            if (!PluginConfig.Instance.EnableAoESlamDamage.Value) return;
            // Only active in 'All' mode as per requirements
            if (PluginConfig.Instance.StateCalculationMode.Value != StateCalculationMode.All) return;

            var bagState = BagPatches.GetState(__instance);
            var baggedObjects = bagState.BaggedObjects;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[AoESlamDamage] Prefix: Coef={damageCoef}, Count={(baggedObjects?.Count ?? 0)}, DistMode={PluginConfig.Instance.AoEDamageDistribution.Value}");
            }

            if (baggedObjects == null || baggedObjects.Count <= 1) return;

            // Handle Split distribution
            if (PluginConfig.Instance.AoEDamageDistribution.Value == AoEDamageMode.Split)
            {
                damageCoef /= baggedObjects.Count;
                __state = damageCoef;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AoESlamDamage] Split mode enabled. Split Coef: {__state} (Original/{baggedObjects.Count})");
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(DrifterBagController __instance, float __state)
        {
            if (!NetworkServer.active) return;
            // Only apply AoE slam damage when EnableBalance is true
            if (!PluginConfig.Instance.EnableBalance.Value) return;
            if (!PluginConfig.Instance.EnableAoESlamDamage.Value) return;
            if (PluginConfig.Instance.StateCalculationMode.Value != StateCalculationMode.All) return;

            var bagState = BagPatches.GetState(__instance);
            var baggedObjects = bagState.BaggedObjects;
            if (baggedObjects == null) return;

            var mainSeat = BagPatches.GetMainSeatObject(__instance);
            var drifterBody = __instance.GetComponent<CharacterBody>();

            // Use the effective coefficient passed from Prefix (modified if Split, original if Full)
            float effectiveCoef = __state;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[AoESlamDamage] Postfix: EffectiveCoef={effectiveCoef}, StateCoef={__state}");
            }

            if (effectiveCoef <= 0f) return;

            int hitCount = 0;
            var objectsToDamage = new List<GameObject>(baggedObjects);

            foreach (var obj in objectsToDamage)
            {
                // Skip the object in the main seat as vanilla handles it
                if (obj == null || ReferenceEquals(obj, mainSeat)) continue;

                // Double check against vehicleSeat
                if (__instance.vehicleSeat && __instance.vehicleSeat.hasPassenger && ReferenceEquals(obj, __instance.vehicleSeat.NetworkpassengerBodyObject)) continue;

                // Check for SpecialObjectAttributes (Durability)
                var specializedAttributes = obj.GetComponent<SpecialObjectAttributes>();
                bool isDurabilityObject = specializedAttributes != null;

                // Handle Split Logic
                if (PluginConfig.Instance.AoEDamageDistribution.Value == AoEDamageMode.Split)
                {
                    if (isDurabilityObject)
                    {
                        float chance = 1f / ((float)baggedObjects.Count);
                        if (UnityEngine.Random.value > chance)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[AoESlamDamage] Split RNG: {obj.name} SKIPPED (Chance={chance:F2})");
                            continue; // Skip damage
                        }
                        else
                        {
                             if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[AoESlamDamage] Split RNG: {obj.name} HIT (Chance={chance:F2})");
                        }
                    }
                }

                ApplyDamageToObject(__instance, drifterBody, obj, effectiveCoef);
                hitCount++;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value && hitCount > 0)
            {
                Log.Info($"[AoESlamDamage] Applied AoE damage to {hitCount} additional objects with coef {effectiveCoef}");
            }

            // Invalidate damage preview cache when slam damage is applied
            DamagePreviewOverlay.InvalidateAllCaches();
        }

        private static void ApplyDamageToObject(DrifterBagController controller, CharacterBody drifterBody, GameObject targetObject, float damageCoef)
        {
            if (!targetObject) return;

            var body = targetObject.GetComponent<CharacterBody>();
            if (body)
            {
                if (drifterBody && body.healthComponent)
                {
                    DamageInfo damageInfo = new DamageInfo
                    {
                        attacker = controller.gameObject,
                        crit = drifterBody.RollCrit(),
                        damage = drifterBody.damage * damageCoef,
                        position = body.footPosition,
                        inflictor = controller.gameObject,
                        damageType = DamageTypeExtended.DrifterBag,
                        damageColorIndex = DamageColorIndex.Default
                    };
                    body.healthComponent.TakeDamage(damageInfo);

                    // Debug Log for JunkCube
                     if (PluginConfig.Instance.EnableDebugLogs.Value && targetObject.GetComponent<JunkCubeController>())
                    {
                        Log.Info($"[AoESlamDamage] Dealt force-damage to JunkCube {targetObject.name}");
                    }
                }
                return;
            }

            var attributes = targetObject.GetComponent<SpecialObjectAttributes>();
            if (attributes)
            {
                if (attributes.durability <= 1)
                {
                    var junkController = controller.GetComponent<JunkController>();
                    if (junkController)
                    {
                        junkController.CallCmdGenerateJunkQuantity(attributes.transform.position, 4);
                    }
                    attributes.Networkdurability = 0;
                     NetworkServer.Destroy(targetObject);
                }
                else
                {
                   attributes.Networkdurability = (int)((byte)(attributes.durability - 1));
                }
                return;
            }
        }
    }
}
