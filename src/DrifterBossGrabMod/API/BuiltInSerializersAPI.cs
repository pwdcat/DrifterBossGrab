#nullable enable
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using EntityStates;
using DrifterBossGrabMod.ProperSave.Serializers;
using DrifterBossGrabMod.ProperSave.Serializers.Plugins;

namespace DrifterBossGrabMod.API
{
    public static class BuiltInSerializersAPI
    {
        public static IObjectSerializerPlugin ForChest() =>
            new ComponentAPISerializer<RoR2.ChestBehavior>(priority: 100)
                .AddAction("isChestOpened", c => c.isChestOpened, (c, v) => c.NetworkisChestOpened = v)
                .AddAction("isCommandChest", c => c.isCommandChest, (c, v) => c.NetworkisCommandChest = v)
                .AddAction("isChestReset", c => c.isChestReset, (c, v) => c.NetworkisChestReset = v)
                .AddAction("dropCount", c => c.dropCount)
                .AddAction("minDropCount", c => c.minDropCount)
                .AddAction("maxDropCount", c => c.maxDropCount)
                .AddCustomAction(CapturePurchaseInteraction, RestorePurchaseInteraction)
                .AddCustomAction(CaptureEntityStateMachine, RestoreEntityStateMachine);

        public static IObjectSerializerPlugin ForDuplicator() =>
            new ComponentAPISerializer<ShopTerminalBehavior>(priority: 100)
                .AddAction("hasBeenPurchased", c => c.hasBeenPurchased, (c, v) => c.NetworkhasBeenPurchased = v)
                .AddAction("hidden", c => c.hidden, (c, v) => c.Networkhidden = v)
                .AddAction("itemTier", c => c.itemTier, asInt: true)
                .AddAction("dropAmount", c => c.dropAmount)
                .AddCustomAction(CapturePickup, RestorePickup)
                .AddCustomAction(CapturePurchaseInteraction, RestorePurchaseInteraction);

        private static void CapturePickup<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var duplicator = component as ShopTerminalBehavior;
            if (duplicator != null)
            {
                state["pickupIndex"] = duplicator.pickup.pickupIndex.ToString();
                state["pickupDecayValue"] = duplicator.pickup.decayValue;
            }
        }

        private static void RestorePickup<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var duplicator = component as ShopTerminalBehavior;
            if (duplicator == null) return;

            if (state.TryGetValue("pickupIndex", out var pickupIndexObj) &&
                state.TryGetValue("pickupDecayValue", out var decayValueObj))
            {
                var pickupIndexStr = pickupIndexObj?.ToString();
                if (!string.IsNullOrEmpty(pickupIndexStr))
                {
                    var pickupIndex = PickupCatalog.FindPickupIndex(pickupIndexStr);
                    if (pickupIndex != PickupIndex.none)
                    {
                        var decayValue = decayValueObj is uint u ? u : 0u;
                        duplicator.SetPickup(new UniquePickup
                        {
                            pickupIndex = pickupIndex,
                            decayValue = decayValue
                        });
                    }
                }
            }
        }

        public static IObjectSerializerPlugin ForShrine() =>
            new ComponentAPISerializer<ShrineBehavior>(priority: 90)
                .AddCustomAction(CapturePurchaseInteraction, RestorePurchaseInteraction)
                .AddCustomAction(CaptureEntityStateMachine, RestoreEntityStateMachine);

        public static IObjectSerializerPlugin ForPurchaseInteraction() =>
            new ComponentAPISerializer<PurchaseInteraction>(priority: 70)
                .AddAction("cost", c => c.cost, (c, v) => c.Networkcost = v)
                .AddAction("costType", c => c.costType, asInt: true)
                .AddAction("purchaseAvailable", c => c.available, (c, v) => c.SetAvailable(v))
                .AddAction("purchaseLocked", c => c.lockGameObject);

        public static IObjectSerializerPlugin ForSpecialObjectAttributes()
        {
            var serializer = new ComponentAPISerializer<SpecialObjectAttributes>(priority: 85)
                .AddAction("durability", c => c.durability, (c, v) => c.Networkdurability = v)
                .AddAction("locked", c => c.locked, (c, v) => c.Networklocked = v)
                .AddAction("maxDurability", c => c.maxDurability, (c, v) => c.maxDurability = v)
                .AddAction("grabbable", c => c.grabbable, (c, v) => c.grabbable = v)
                .AddAction("massOverride", c => c.massOverride, (c, v) => c.massOverride = v)
                .AddAction("damageOverride", c => c.damageOverride, (c, v) => c.damageOverride = v)
                .AddAction("hullClassification", c => c.hullClassification, asInt: true)
                .AddAction("orientToFloor", c => c.orientToFloor, (c, v) => c.orientToFloor = v)
                .AddAction("isVoid", c => c.isVoid, (c, v) => c.isVoid = v)
                .AddAction("bestName", c => c.bestName, (c, v) => c.bestName = v);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[BuiltInSerializersAPI] Created SpecialObjectAttributes serializer");
            }

            return serializer;
        }

        public static IObjectSerializerPlugin ForCharacterBody() =>
            new ComponentAPISerializer<CharacterBody>(priority: 110)
                .AddAction("baseMaxHealth", c => c.baseMaxHealth)
                .AddAction("baseMaxShield", c => c.baseMaxShield)
                .AddAction("baseDamage", c => c.baseDamage)
                .AddAction("baseMoveSpeed", c => c.baseMoveSpeed)
                .AddAction("baseAttackSpeed", c => c.baseAttackSpeed)
                .AddAction("baseArmor", c => c.baseArmor)
                .AddAction("level", c => c.level);

        public static IObjectSerializerPlugin ForCharacterMaster() =>
            new ComponentAPISerializer<CharacterMaster>(priority: 115)
                .AddAction("teamIndex", c => c.teamIndex, (c, v) => c.teamIndex = v, asInt: true);

        public static IObjectSerializerPlugin ForJunkCubeController()
        {
            var serializer = new ComponentAPISerializer<JunkCubeController>(priority: 95);
            var _maxActivationCountField = typeof(JunkCubeController).GetField("_maxActivationCount",
                BindingFlags.NonPublic | BindingFlags.Instance);

            serializer.AddAction("ActivationCount", c => c.ActivationCount);
            serializer.AddAction("_maxActivationCount", c =>
            {
                return _maxActivationCountField?.GetValue(c) as int? ?? c.ActivationCount;
            }, (c, v) =>
            {
                _maxActivationCountField?.SetValue(c, v);
            });

            return serializer;
        }

        public static IObjectSerializerPlugin ForHalcyoniteShrineInteractable() =>
            new ComponentAPISerializer<RoR2.HalcyoniteShrineInteractable>(priority: 95)
                .AddAction("interactions", c => c.interactions, (c, v) => c.Networkinteractions = v)
                .AddAction("goldDrained", c => c.goldDrained)
                .AddAction("isDraining", c => c.isDraining, (c, v) => c.NetworkisDraining = v)
                .AddAction("maxTargets", c => c.maxTargets, (c, v) => c.NetworkmaxTargets = v)
                .AddAction("goldMaterialModifier", c => c.goldMaterialModifier, (c, v) => c.NetworkgoldMaterialModifier = v)
                .AddCustomAction(CapturePurchaseInteraction, RestorePurchaseInteraction)
                .AddCustomAction(CaptureEntityStateMachine, RestoreEntityStateMachine);

        public static IObjectSerializerPlugin ForTinkerableObjectAttributes() =>
            new ComponentAPISerializer<RoR2.TinkerableObjectAttributes>(priority: 90)
                .AddAction("tinkers", c => c.tinkers, (c, v) => c.Networktinkers = v)
                .AddAction("maxTinkers", c => c.maxTinkers)
                .AddCustomAction(CapturePurchaseInteraction, RestorePurchaseInteraction);

        public static IObjectSerializerPlugin ForQualityIntegration()
        {
            return new QualityIntegration();
        }

        public static IObjectSerializerPlugin ForGenericComponentSerializer()
        {
            return new GenericComponentSerializerPlugin();
        }

        private static void CapturePurchaseInteraction<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var purchase = component.GetComponent<PurchaseInteraction>();
            if (purchase != null)
            {
                state["purchaseCost"] = purchase.cost;
                state["purchaseCostType"] = (int)purchase.costType;
                state["purchaseAvailable"] = purchase.available;
                state["purchaseLocked"] = purchase.lockGameObject;
            }
        }

        private static void RestorePurchaseInteraction<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var purchase = component.GetComponent<PurchaseInteraction>();
            if (purchase == null) return;

            if (state.TryGetValue("purchaseCost", out var cost))
            {
                purchase.Networkcost = (int)cost;
            }

            if (state.TryGetValue("purchaseAvailable", out var available))
            {
                purchase.SetAvailable((bool)available);
            }

            if (state.TryGetValue("purchaseLocked", out var locked))
            {
                var lockObj = locked as bool?;
                if (lockObj.HasValue)
                {
                    purchase.lockGameObject = lockObj.Value ? purchase.lockGameObject : null;
                }
            }
        }

        private static void CaptureHealthComponent<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var health = component.GetComponent<HealthComponent>();
            if (health == null) return;

            state["healthFraction"] = health.healthFraction;
            state["shieldFraction"] = health.shield > 0f ? health.shield / health.fullHealth : 0f;
            state["barrierFraction"] = health.barrier > 0f ? health.barrier / health.fullHealth : 0f;
        }

        private static void RestoreHealthComponentAndRecalculateStats<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var health = component.GetComponent<HealthComponent>();
            if (health == null) return;

            var body = component as CharacterBody;
            if (body != null)
            {
                body.RecalculateStats();
            }

            if (state.TryGetValue("healthFraction", out var healthFraction))
            {
                health.Networkhealth = Mathf.Clamp01((float)healthFraction) * health.fullHealth;
            }

            if (state.TryGetValue("shieldFraction", out var shieldFraction))
            {
                health.Networkshield = Mathf.Clamp01((float)shieldFraction) * health.fullHealth;
            }

            if (state.TryGetValue("barrierFraction", out var barrierFraction))
            {
                health.Networkbarrier = Mathf.Clamp01((float)barrierFraction) * health.fullHealth;
            }

            if (body != null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CharacterBody] Restored health for {body.name}: health={health.health}, maxHealth={health.fullHealth}, healthFraction={health.healthFraction}");
            }
        }

        private static void CaptureEntityStateMachine<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var stateMachine = component.GetComponent<EntityStateMachine>();
            if (stateMachine != null && stateMachine.state != null)
            {
                var stateType = stateMachine.state.GetType();
                if (stateType != null && !string.IsNullOrEmpty(stateType.FullName))
                {
                    state["EntityStateType"] = stateType.FullName;
                    state["EntityStateMachineName"] = stateMachine.customName;
                }
            }
        }

        private static void RestoreEntityStateMachine<T>(T component, Dictionary<string, object> state) where T : Component
        {
            if (!state.TryGetValue("EntityStateType", out var stateTypeObj) ||
                !state.TryGetValue("EntityStateMachineName", out var machineNameObj))
            {
                return;
            }

            var stateTypeName = stateTypeObj?.ToString();
            var targetMachineName = machineNameObj?.ToString();

            if (string.IsNullOrEmpty(stateTypeName) || string.IsNullOrEmpty(targetMachineName))
            {
                return;
            }

            try
            {
                var allStateMachines = component.GetComponentsInChildren<EntityStateMachine>();
                EntityStateMachine? stateMachine = null;

                if (!string.IsNullOrEmpty(targetMachineName))
                {
                    stateMachine = System.Array.Find(allStateMachines, sm => sm.customName == targetMachineName);
                }

                if (stateMachine == null && allStateMachines.Length > 0)
                {
                    stateMachine = allStateMachines[0];
                }

                if (stateMachine == null)
                {
                    Log.Warning($"[EntityStateMachine] Could not find state machine on {component.gameObject.name}");
                    return;
                }

                Type? stateType = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var types = asm.GetTypes();
                        foreach (var t in types)
                        {
                            if (t.FullName == stateTypeName && typeof(EntityState).IsAssignableFrom(t))
                            {
                                stateType = t;
                                break;
                            }
                        }
                        if (stateType != null) break;
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        continue;
                    }
                }

                if (stateType != null)
                {
                    var newState = EntityStateCatalog.InstantiateState(stateType);
                    if (newState != null)
                    {
                        stateMachine.SetState(newState);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[EntityStateMachine] Restored {component.gameObject.name} to state {stateTypeName}");
                        }
                    }
                }
                else
                {
                    Log.Warning($"[EntityStateMachine] Could not find state type '{stateTypeName}' for {component.gameObject.name}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[EntityStateMachine] Failed to restore state for {component.gameObject.name}: {ex.Message}");
            }
        }
    }
}
