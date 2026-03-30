#nullable enable
using RoR2;

namespace DrifterBossGrabMod.ProperSave.Serializers
{
    // Factory methods for all built-in declarative serializers
    // Each method is ~10-25 lines of pure declaration, eliminating 100-200 lines of boilerplate
    public static class BuiltInSerializers
    {
        // Serializer for ChestBehavior components
        public static IObjectSerializerPlugin ForChest() =>
            new ComponentFieldMap<RoR2.ChestBehavior>(priority: 100)
                .Field(c => c.isChestOpened, restore: (c, v) => c.NetworkisChestOpened = v, key: "isChestOpened")
                .Field(c => c.isCommandChest, restore: (c, v) => c.NetworkisCommandChest = v, key: "isCommandChest")
                .Field(c => c.isChestReset, restore: (c, v) => c.NetworkisChestReset = v, key: "isChestReset")
                .Field(c => c.dropCount, key: "dropCount")
                .Field(c => c.minDropCount, key: "minDropCount")
                .Field(c => c.maxDropCount, key: "maxDropCount")
                .UniquePickup(c => c.currentPickup, restore: (c, p) => c.currentPickup = p)
                .WithPurchaseInteraction()
                .WithEntityStateMachine();

        // Serializer for ShopTerminalBehavior components (Duplicators/Printers)
        public static IObjectSerializerPlugin ForDuplicator() =>
            new ComponentFieldMap<ShopTerminalBehavior>(priority: 100)
                .Field(c => c.hasBeenPurchased, restore: (c, v) => c.NetworkhasBeenPurchased = v, key: "hasBeenPurchased")
                .Field(c => c.hidden, restore: (c, v) => c.Networkhidden = v, key: "hidden")
                .Field(c => c.itemTier, asInt: true, key: "itemTier")
                .Field(c => c.dropAmount, key: "dropAmount")
                .UniquePickup(c => c.pickup, restore: (c, p) => c.SetPickup(p, false))
                .WithPurchaseInteraction();

        // Serializer for ShrineBehavior components
        public static IObjectSerializerPlugin ForShrine() =>
            new ComponentFieldMap<ShrineBehavior>(priority: 90)
                .WithPurchaseInteraction()
                .WithEntityStateMachine();

        // Serializer for PurchaseInteraction components
        public static IObjectSerializerPlugin ForPurchaseInteraction() =>
            new ComponentFieldMap<PurchaseInteraction>(priority: 70)
                .Field(c => c.cost, restore: (c, v) => c.Networkcost = v, key: "cost")
                .Field(c => c.costType, asInt: true, key: "costType")
                .Field(c => c.available, key: "purchaseAvailable", restore: (c, v) => c.SetAvailable(v))
                .Field(c => c.lockGameObject, key: "lockGameObject");

        // Serializer for SpecialObjectAttributes components (grabbable objects)
        public static IObjectSerializerPlugin ForSpecialObjectAttributes()
        {
            var serializer = new ComponentFieldMap<SpecialObjectAttributes>(priority: 85)
                .Field(c => c.durability, restore: (c, v) => c.Networkdurability = v, key: "durability")
                .Field(c => c.locked, restore: (c, v) => c.Networklocked = v, key: "locked")
                .Field(c => c.maxDurability, restore: (c, v) => c.maxDurability = v, key: "maxDurability")
                .Field(c => c.grabbable, restore: (c, v) => c.grabbable = v, key: "grabbable")
                .Field(c => c.massOverride, restore: (c, v) => c.massOverride = v, key: "massOverride")
                .Field(c => c.damageOverride, restore: (c, v) => c.damageOverride = v, key: "damageOverride")
                .Field(c => c.hullClassification, asInt: true, restore: (c, v) => c.hullClassification = v, key: "hullClassification")
                .Field(c => c.orientToFloor, restore: (c, v) => c.orientToFloor = v, key: "orientToFloor")
                .Field(c => c.isVoid, restore: (c, v) => c.isVoid = v, key: "isVoid")
                .Field(c => c.bestName, restore: (c, v) => c.bestName = v, key: "bestName");

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[BuiltInSerializers] Created SpecialObjectAttributes serializer");
            }

            return serializer;
        }

        // Serializer for CharacterBody components (enemy bodies)
        public static IObjectSerializerPlugin ForCharacterBody() =>
            new ComponentFieldMap<CharacterBody>(priority: 110)
                .Field(c => c.baseMaxHealth, key: "baseMaxHealth")
                .Field(c => c.baseMaxShield, key: "baseMaxShield")
                .Field(c => c.baseDamage, key: "baseDamage")
                .Field(c => c.baseMoveSpeed, key: "baseMoveSpeed")
                .Field(c => c.baseAttackSpeed, key: "baseAttackSpeed")
                .Field(c => c.baseArmor, key: "baseArmor")
                .Field(c => c.level, key: "level")
                .WithHealthComponent();

        // Serializer for JunkCubeController components
        // Serializes ActivationCount (durability) and _maxActivationCount
        public static IObjectSerializerPlugin ForJunkCubeController()
        {
            var serializer = new ComponentFieldMap<JunkCubeController>(priority: 95);

            var _maxActivationCountField = typeof(JunkCubeController).GetField("_maxActivationCount",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            serializer.Field(c => c.ActivationCount, key: "ActivationCount");

            serializer.Field(
                getter: c =>
                {
                    return _maxActivationCountField?.GetValue(c) as int? ?? c.ActivationCount;
                },
                key: "_maxActivationCount",
                restore: (c, v) =>
                {
                    _maxActivationCountField?.SetValue(c, v);
                }
            );

            return serializer;
        }

        // Serializer for HalcyoniteShrineInteractable components
        public static IObjectSerializerPlugin ForHalcyoniteShrineInteractable() =>
            new ComponentFieldMap<RoR2.HalcyoniteShrineInteractable>(priority: 95)
                .Field(c => c.interactions, restore: (c, v) => c.Networkinteractions = v, key: "interactions")
                .Field(c => c.goldDrained, key: "goldDrained")
                .Field(c => c.isDraining, restore: (c, v) => c.NetworkisDraining = v, key: "isDraining")
                .Field(c => c.maxTargets, restore: (c, v) => c.NetworkmaxTargets = v, key: "maxTargets")
                .Field(c => c.goldMaterialModifier, restore: (c, v) => c.NetworkgoldMaterialModifier = v, key: "goldMaterialModifier")
                .WithPurchaseInteraction()
                .WithEntityStateMachine();

        // Serializer for TinkerableObjectAttributes components
        public static IObjectSerializerPlugin ForTinkerableObjectAttributes() =>
            new ComponentFieldMap<RoR2.TinkerableObjectAttributes>(priority: 90)
                .Field(c => c.tinkers, restore: (c, v) => c.Networktinkers = v, key: "tinkers")
                .Field(c => c.maxTinkers, key: "maxTinkers")
                .WithPurchaseInteraction();
    }
}
