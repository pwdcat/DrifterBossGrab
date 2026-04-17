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
                .AddAction("baseMaxHealth", c => c.baseMaxHealth, (c, v) => c.baseMaxHealth = v)
                .AddAction("baseMaxShield", c => c.baseMaxShield, (c, v) => c.baseMaxShield = v)
                .AddAction("baseRegen", c => c.baseRegen, (c, v) => c.baseRegen = v)
                .AddAction("baseDamage", c => c.baseDamage, (c, v) => c.baseDamage = v)
                .AddAction("baseMoveSpeed", c => c.baseMoveSpeed, (c, v) => c.baseMoveSpeed = v)
                .AddAction("baseAttackSpeed", c => c.baseAttackSpeed, (c, v) => c.baseAttackSpeed = v)
                .AddAction("baseArmor", c => c.baseArmor, (c, v) => c.baseArmor = v)
                .AddAction("baseCrit", c => c.baseCrit, (c, v) => c.baseCrit = v)
                .AddAction("level", c => c.level, (c, v) => c.level = v)
                .AddAction("experience", c => c.experience, (c, v) => c.experience = v)
                .AddAction("bodyFlags", c => c.bodyFlags, (c, v) => c.bodyFlags = v, asInt: true)
                .AddAction("skinIndex", c => c.skinIndex, (c, v) => c.skinIndex = v)
                .AddAction("subtitleNameToken", c => c.subtitleNameToken, (c, v) => c.subtitleNameToken = v)
                .AddCustomAction(CaptureHealthComponent, RestoreHealthComponentAndRecalculateStats);

        public static IObjectSerializerPlugin ForCharacterMaster() =>
            new ComponentAPISerializer<CharacterMaster>(priority: 115)
                .AddAction("teamIndex", c => c.teamIndex, (c, v) => c.teamIndex = v, asInt: true)
                .AddCustomAction(CaptureMasterInventory, RestoreMasterInventory);

        public static IObjectSerializerPlugin ForInventory() =>
            new ComponentAPISerializer<Inventory>(priority: 120)
                .AddCustomAction((c, s) => CaptureInventory(c, s), (c, s) => RestoreInventory(c, s));

        public static IObjectSerializerPlugin ForJunkCubeController()
        {
            var serializer = new ComponentAPISerializer<JunkCubeController>(priority: 95);
            var _maxActivationCountField = typeof(JunkCubeController).GetField("_maxActivationCount",
                BindingFlags.NonPublic | BindingFlags.Instance);

            serializer.AddAction("ActivationCount", c => c.ActivationCount, (c, v) => c.ActivationCount = (int)v);
            serializer.AddAction("_maxActivationCount", c =>
            {
                return _maxActivationCountField?.GetValue(c) as int? ?? c.ActivationCount;
            }, (c, v) =>
            {
                _maxActivationCountField?.SetValue(c, v);
            });
            serializer.AddCustomAction(CaptureJunkCubeHealth, RestoreJunkCubeHealth);

            return serializer;
        }

        private static void CaptureJunkCubeHealth<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var junkCube = component as JunkCubeController;
            if (junkCube == null) return;

            var health = junkCube.GetComponent<HealthComponent>();
            if (health != null)
            {
                state["healthFraction"] = health.healthFraction;
                state["shieldFraction"] = health.shield > 0f ? health.shield / health.fullHealth : 0f;
                state["barrierFraction"] = health.barrier > 0f ? health.barrier / health.fullHealth : 0f;
            }
        }

        private static void RestoreJunkCubeHealth<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var junkCube = component as JunkCubeController;
            if (junkCube == null) return;

            var health = junkCube.GetComponent<HealthComponent>();
            if (health == null) return;

            float savedHealthFraction = 1f;
            if (state.TryGetValue("healthFraction", out var healthFraction))
            {
                savedHealthFraction = (float)healthFraction;
            }

            var body = junkCube.GetComponent<CharacterBody>();
            if (body != null)
            {
                body.RecalculateStats();
            }

            if (health.fullHealth > 0f && savedHealthFraction >= 0f)
            {
                var targetHealth = Mathf.Clamp01(savedHealthFraction) * health.fullHealth;
                targetHealth = Mathf.Clamp(targetHealth, 0f, health.fullHealth);
                health.Networkhealth = targetHealth;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[RestoreJunkCubeHealth] Restored health: {health.health}/{health.fullHealth} (fraction: {savedHealthFraction:F3}, activation: {junkCube.ActivationCount})");
                }
            }

            if (state.TryGetValue("shieldFraction", out var shieldFraction))
            {
                health.Networkshield = Mathf.Clamp01((float)shieldFraction) * health.fullHealth;
            }

            if (state.TryGetValue("barrierFraction", out var barrierFraction))
            {
                health.Networkbarrier = Mathf.Clamp01((float)barrierFraction) * health.fullHealth;
            }
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

            var body = component as CharacterBody;

            state["healthFraction"] = health.healthFraction;
            state["shieldFraction"] = health.shield > 0f ? health.shield / health.fullHealth : 0f;
            state["barrierFraction"] = health.barrier > 0f ? health.barrier / health.fullHealth : 0f;
        }

        private static void RestoreHealthComponentAndRecalculateStats<T>(T component, Dictionary<string, object> state) where T : Component
        {
            var health = component.GetComponent<HealthComponent>();
            if (health == null) return;

            var body = component as CharacterBody;

            if (health.health > health.fullHealth && health.fullHealth > 0f)
            {
                health.health = health.fullHealth;
            }

            if (health.fullHealth <= 0f && body != null)
            {
                body.RecalculateStats();

                if (health.fullHealth <= 0f)
                {
                    return;
                }
            }

            float savedHealthFraction = 1f;
            if (state.TryGetValue("healthFraction", out var healthFraction))
            {
                savedHealthFraction = (float)healthFraction;
            }

            if (body != null)
            {
                body.RecalculateStats();
            }

            if (savedHealthFraction >= 0f && health.fullHealth > 0f)
            {
                var targetHealth = Mathf.Clamp01(savedHealthFraction) * health.fullHealth;
                targetHealth = Mathf.Clamp(targetHealth, 0f, health.fullHealth);
                health.Networkhealth = targetHealth;
            }
            else if (health.fullHealth > 0f)
            {
                health.Networkhealth = Mathf.Min(health.health, health.fullHealth);
            }

            if (state.TryGetValue("shieldFraction", out var shieldFraction))
            {
                health.Networkshield = Mathf.Clamp01((float)shieldFraction) * health.fullHealth;
            }

            if (state.TryGetValue("barrierFraction", out var barrierFraction))
            {
                health.Networkbarrier = Mathf.Clamp01((float)barrierFraction) * health.fullHealth;
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
                        Log.Info($"[EntityStateMachine] Setting state {stateTypeName} on {component.gameObject.name}");
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

        private static void CaptureMasterInventory(CharacterMaster master, Dictionary<string, object> state)
        {
            if (master.inventory != null)
            {
                CaptureInventory(master.inventory, state, "inventory.");
            }
        }

        private static void RestoreMasterInventory(CharacterMaster master, Dictionary<string, object> state)
        {
            if (master.inventory != null)
            {
                RestoreInventory(master.inventory, state, "inventory.");
            }
        }

        private static void CaptureInventory(Inventory inventory, Dictionary<string, object> state, string prefix = "")
        {
            var itemStacks = new List<int>();
            for (var i = 0; i < (int)ItemCatalog.itemCount; i++)
            {
                // Capture ALL items, not just permanent ones, to ensure summoned enemies
                // (like Beetle Guards) keep their full inventory in the bag.
                var count = inventory.GetItemCountPermanent((ItemIndex)i);
                if (count > 0)
                {
                    itemStacks.Add(i);
                    itemStacks.Add(count);
                }
            }
            state[prefix + "itemStacks"] = itemStacks;

            var equipment = inventory.GetEquipment(0, 0);
            if (equipment.equipmentIndex != EquipmentIndex.None)
            {
                state[prefix + "equipmentIndex"] = (int)equipment.equipmentIndex;
                state[prefix + "equipmentCharges"] = (int)equipment.charges;
            }
            
            state[prefix + "infusionBonus"] = (int)inventory.infusionBonus;
        }

        private static void RestoreInventory(Inventory inventory, Dictionary<string, object> state, string prefix = "")
        {
            if (state.TryGetValue(prefix + "itemStacks", out var itemStacksObj) && itemStacksObj is List<int> itemStacksList)
            {
                // Clear existing items
                for (int i = 0; i < (int)ItemCatalog.itemCount; i++)
                {
                    inventory.RemoveItemPermanent((ItemIndex)i, inventory.GetItemCountPermanent((ItemIndex)i));
                }

                for (var i = 0; i < itemStacksList.Count; i += 2)
                {
                    var itemIndex = (ItemIndex)itemStacksList[i];
                    var count = itemStacksList[i + 1];
                    inventory.GiveItemPermanent(itemIndex, count);
                }
            }

            if (state.TryGetValue(prefix + "equipmentIndex", out var eqIndexObj))
            {
                var eqIndex = (EquipmentIndex)Convert.ToInt32(eqIndexObj);
                var charges = state.TryGetValue(prefix + "equipmentCharges", out var eqCharges) ? Convert.ToInt32(eqCharges) : 0;
                inventory.SetEquipmentIndex(eqIndex, false);
                inventory.SetEquipment(new EquipmentState(eqIndex, Run.FixedTimeStamp.now, (byte)charges), 0, 0);
            }

            if (state.TryGetValue(prefix + "infusionBonus", out var infusionBonus))
            {
                inventory.infusionBonus = (uint)Convert.ToInt32(infusionBonus);
            }
        }
    }
}
