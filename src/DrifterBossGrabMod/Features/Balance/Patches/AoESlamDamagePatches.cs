#nullable enable
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
            if (PluginConfig.Instance.AoEDamageDistribution.Value == DrifterBossGrabMod.AoEDamageMode.None) return;
            // Only active in 'All' mode as per requirements
            if (PluginConfig.Instance.StateCalculationMode.Value != StateCalculationMode.All) return;

            int count = API.DrifterBagAPI.GetBagCount(__instance);
            Log.DebugIfEnabled("[AoESlamDamage] Prefix: Coef={0}, Count={1}, DistMode={2}", damageCoef, count, PluginConfig.Instance.AoEDamageDistribution.Value);

            if (count <= 1) return;

            // Handle Split distribution
            if (PluginConfig.Instance.AoEDamageDistribution.Value == AoEDamageMode.Split)
            {
                damageCoef /= API.DrifterBagAPI.GetBagCount(__instance);
                __state = damageCoef;

                Log.DebugIfEnabled("[AoESlamDamage] Split mode enabled. Split Coef: {0} (Original/{1})", __state, API.DrifterBagAPI.GetBagCount(__instance));
            }
        }

        [HarmonyPostfix]
        public static void Postfix(DrifterBagController __instance, float __state)
        {
            if (!NetworkServer.active) return;
            // Only apply AoE slam damage when EnableBalance is true
            if (!PluginConfig.Instance.EnableBalance.Value) return;
            if (PluginConfig.Instance.AoEDamageDistribution.Value == DrifterBossGrabMod.AoEDamageMode.None) return;
            if (PluginConfig.Instance.StateCalculationMode.Value != StateCalculationMode.All) return;

            var baggedObjects = API.DrifterBagAPI.GetBaggedObjects(__instance);
            if (baggedObjects.Count == 0) return;

            var mainSeat = API.DrifterBagAPI.GetMainPassenger(__instance);
            var drifterBody = __instance.GetComponent<CharacterBody>();

            // Use the effective coefficient passed from Prefix (modified if Split, original if Full)
            float effectiveCoef = __state;

            Log.DebugIfEnabled("[AoESlamDamage] Postfix: EffectiveCoef={0}, StateCoef={1}", effectiveCoef, __state);

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
                            Log.DebugIfEnabled("[AoESlamDamage] Split RNG: {0} SKIPPED (Chance={1:F2})", obj.name, chance);
                            continue; // Skip damage
                        }
                        else
                        {
                            Log.DebugIfEnabled("[AoESlamDamage] Split RNG: {0} HIT (Chance={1:F2})", obj.name, chance);
                        }
                    }
                }

                ApplyDamageToObject(__instance, drifterBody, obj, effectiveCoef);
                hitCount++;
            }

            if (hitCount > 0)
            {
                Log.DebugIfEnabled("[AoESlamDamage] Applied AoE damage to {0} additional objects with coef {1}", hitCount, effectiveCoef);
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
                    if (targetObject.GetComponent<JunkCubeController>())
                    {
                        Log.DebugIfEnabled("[AoESlamDamage] Dealt force-damage to JunkCube {0}", targetObject.name);
                    }
                }
                return;
            }

            var attributes = targetObject.GetComponent<SpecialObjectAttributes>();
            if (attributes)
            {
                if (attributes.durability <= Constants.Limits.MinDurabilityThreshold)
                {
                    var junkController = controller.GetComponent<JunkController>();
                    if (junkController)
                    {
                        junkController.CallCmdGenerateJunkQuantity(attributes.transform.position, Constants.Limits.DefaultJunkQuantity);
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
