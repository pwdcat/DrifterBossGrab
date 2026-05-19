#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod.Config
{

    // ========================================================================================
    // PRESET DEFINITIONS
    // ========================================================================================
    public static class PresetDefinitions
    {
        public static readonly Dictionary<PresetType, Dictionary<string, object>> Presets = new()
        {

            // ========================================================================================
            // VANILLA PRESET
            // ========================================================================================
            [PresetType.Vanilla] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = false,
                ["General.EnableNPCGrabbing"] = false,
                ["General.EnableEnvironmentGrabbing"] = false,
                ["General.EnableLockedObjectGrabbing"] = false,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.None,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                ["General.GrabbableKeywordBlacklist"] = "Master,Controller",
                ["General.ComponentChooserSortMode"] = ComponentChooserSortMode.ByFrequency,
                ["General.ComponentChooserDummy"] = ComponentChooserDummy.SelectToToggle,

                ["Recovery.EnableRecoveryFeature"] = false,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Kill,
                ["Recovery.RecoverBaggedBosses"] = false,
                ["Recovery.RecoverBaggedNPCs"] = false,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = false,

                ["Persistence.EnableObjectPersistence"] = false,
                ["Persistence.EnableAutoGrab"] = false,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.PersistenceBlacklist"] = "",
                ["Persistence.AutoGrabDelay"] = 1.0f,

                ["BottomlessBag.EnableBottomlessBag"] = false,
                ["BottomlessBag.EnableStockRefreshClamping"] = false,
                ["BottomlessBag.EnableSuccessiveGrabStockRefresh"] = false,
                ["BottomlessBag.CycleCooldown"] = 0.2f,
                ["BottomlessBag.PlayAnimationOnCycle"] = false,
                ["BottomlessBag.EnableMouseWheelScrolling"] = true,
                ["BottomlessBag.InverseMouseWheelScrolling"] = false,
                ["BottomlessBag.AutoPromoteMainSeat"] = true,
                ["BottomlessBag.PrioritizeMainSeat"] = false,

                ["Hud.EnableCarouselHUD"] = false,
                ["Hud.CarouselSpacing"] = 45.0f,
                ["Hud.CarouselAnimationDuration"] = 0.4f,
                ["Hud.CenterSlotX"] = 25.0f,
                ["Hud.CenterSlotY"] = 50.0f,
                ["Hud.CenterSlotScale"] = 1.0f,
                ["Hud.CenterSlotOpacity"] = 1.0f,
                ["Hud.CenterSlotShowIcon"] = true,
                ["Hud.CenterSlotShowWeightIcon"] = true,
                ["Hud.CenterSlotShowName"] = true,
                ["Hud.CenterSlotShowHealthBar"] = true,
                ["Hud.CenterSlotShowSlotNumber"] = true,
                ["Hud.SideSlotX"] = 20.0f,
                ["Hud.SideSlotY"] = 5.0f,
                ["Hud.SideSlotScale"] = 0.8f,
                ["Hud.SideSlotOpacity"] = 0.3f,
                ["Hud.SideSlotShowIcon"] = true,
                ["Hud.SideSlotShowWeightIcon"] = true,
                ["Hud.SideSlotShowName"] = true,
                ["Hud.SideSlotShowHealthBar"] = true,
                ["Hud.SideSlotShowSlotNumber"] = true,
                ["Hud.EnableDamagePreview"] = false,
                ["Hud.DamagePreviewColor"] = new Color(1f, 0.15f, 0.15f, 0.8f),
                ["Hud.UseNewWeightIcon"] = false,
                ["Hud.WeightDisplayMode"] = WeightDisplayMode.Multiplier,
                ["Hud.ScaleWeightColor"] = true,
                ["Hud.ShowTotalMassOnWeightIcon"] = false,
                ["Hud.ShowOverencumberIcon"] = false,
                ["Hud.EnableMassCapacityUI"] = false,
                ["Hud.MassCapacityUIPositionX"] = -20.0f,
                ["Hud.MassCapacityUIPositionY"] = 0.0f,
                ["Hud.MassCapacityUIScale"] = 0.8f,
                ["Hud.EnableSeparators"] = true,
                ["Hud.GradientIntensity"] = 1.0f,
                ["Hud.CapacityGradientColorStart"] = new Color(0.0f, 1.0f, 0.0f, 1.0f),
                ["Hud.CapacityGradientColorMid"] = new Color(1.0f, 1.0f, 0.0f, 1.0f),
                ["Hud.CapacityGradientColorEnd"] = new Color(1.0f, 0.0f, 0.0f, 1.0f),
                ["Hud.OverencumbranceGradientColorStart"] = new Color(0f, 1.0f, 1.0f, 1.0f),
                ["Hud.OverencumbranceGradientColorMid"] = new Color(0.0f, 0.0f, 0.5f, 1.0f),
                ["Hud.OverencumbranceGradientColorEnd"] = new Color(0.0f, 0.0f, 1.0f, 1.0f),
                ["Hud.EnableBaggedObjectInfo"] = false,
                ["Hud.BaggedObjectInfoX"] = 450.0f,
                ["Hud.BaggedObjectInfoY"] = 85.0f,
                ["Hud.BaggedObjectInfoScale"] = 1.0f,
                ["Hud.BaggedObjectInfoColor"] = new Color(1f, 1f, 1f, 0.9f),

                ["Balance.EnableBalance"] = false,
                ["Balance.BreakoutTimeMultiplier"] = 1.0f,
                ["Balance.MaxSmacks"] = 3,
                ["Balance.MaxLaunchSpeed"] = "30",
                ["General.SearchRadiusMultiplier"] = 1.0f,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["BottomlessBag.SlotScalingFormula"] = "0",
                ["Balance.MassCapacityFormula"] = "",
                ["Character Flags.EliteFlagMultiplier"] = "1",
                ["Character Flags.BossFlagMultiplier"] = "1",
                ["Character Flags.ChampionFlagMultiplier"] = "1",
                ["Character Flags.PlayerFlagMultiplier"] = "1",
                ["Character Flags.MinionFlagMultiplier"] = "1",
                ["Character Flags.DroneFlagMultiplier"] = "1",
                ["Character Flags.MechanicalFlagMultiplier"] = "1",
                ["Character Flags.VoidFlagMultiplier"] = "1",
                ["Character Flags.AllFlagMultiplier"] = "1",
                ["Balance.OverencumbranceMax"] = 0.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.MovespeedPenaltyFormula"] = "0",
                ["Balance.BagScaleCap"] = "1",
                ["Balance.MassCap"] = "700",
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // INTENDED PRESET
            // ========================================================================================
            [PresetType.Intended] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = false,
                ["General.EnableEnvironmentGrabbing"] = false,
                ["General.EnableLockedObjectGrabbing"] = false,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.None,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                ["General.GrabbableKeywordBlacklist"] = "Master,Controller",
                ["General.ComponentChooserSortMode"] = ComponentChooserSortMode.ByFrequency,
                ["General.ComponentChooserDummy"] = ComponentChooserDummy.SelectToToggle,

                ["Recovery.EnableRecoveryFeature"] = false,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Kill,
                ["Recovery.RecoverBaggedBosses"] = false,
                ["Recovery.RecoverBaggedNPCs"] = false,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = false,

                ["Persistence.EnableObjectPersistence"] = false,
                ["Persistence.PersistenceBlacklist"] = "",

                ["BottomlessBag.EnableBottomlessBag"] = false,

                ["Hud.EnableCarouselHUD"] = false,
                ["Hud.EnableDamagePreview"] = false,
                ["Hud.EnableMassCapacityUI"] = false,
                ["Hud.EnableBaggedObjectInfo"] = false,

                ["Balance.EnableBalance"] = false,
                ["Balance.BreakoutTimeMultiplier"] = 1.0f,
                ["Balance.MaxSmacks"] = 3,
                ["Balance.MaxLaunchSpeed"] = "30",
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["BottomlessBag.SlotScalingFormula"] = "C + 2",
                ["Balance.MassCapacityFormula"] = "",
                ["Balance.OverencumbranceMax"] = 0.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.MovespeedPenaltyFormula"] = "clamp((T/M) * 0.25, 0, 0.5)",
                ["Balance.BagScaleCap"] = "1",
                ["Balance.MassCap"] = "700",
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // DEFAULT PRESET
            // ========================================================================================
            [PresetType.Default] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = true,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.SurvivorOnly,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                ["General.GrabbableKeywordBlacklist"] = "Master,Controller",
                ["General.ComponentChooserSortMode"] = ComponentChooserSortMode.ByFrequency,
                ["General.ComponentChooserDummy"] = ComponentChooserDummy.SelectToToggle,

                ["Recovery.EnableRecoveryFeature"] = true,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Recover,
                ["Recovery.RecoverBaggedBosses"] = true,
                ["Recovery.RecoverBaggedNPCs"] = true,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = true,

                ["Persistence.EnableObjectPersistence"] = true,
                ["Persistence.EnableAutoGrab"] = true,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.PersistenceBlacklist"] = "",
                ["BottomlessBag.EnableBottomlessBag"] = true,
                ["BottomlessBag.EnableStockRefreshClamping"] = true,
                ["BottomlessBag.EnableSuccessiveGrabStockRefresh"] = true,
                ["Hud.EnableCarouselHUD"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableBaggedObjectInfo"] = true,
                ["Hud.ShowTotalMassOnWeightIcon"] = false,
                ["Hud.ShowOverencumberIcon"] = false,
                ["Balance.EnableBalance"] = false,
                ["Balance.BreakoutTimeMultiplier"] = 1.0f,
                ["Balance.MaxSmacks"] = 3,
                ["Balance.MaxLaunchSpeed"] = "30",
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["BottomlessBag.SlotScalingFormula"] = "C + 2",
                ["Balance.MassCapacityFormula"] = "",
                ["Balance.OverencumbranceMax"] = 0.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.MovespeedPenaltyFormula"] = "clamp((T/M) * 0.25, 0, 0.5)",
                ["Balance.BagScaleCap"] = "1",
                ["Balance.MassCap"] = "700",
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // BALANCE PRESET
            // ========================================================================================
            [PresetType.Balance] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = true,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.SurvivorOnly,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                ["General.GrabbableKeywordBlacklist"] = "Master,Controller",
                ["General.ComponentChooserSortMode"] = ComponentChooserSortMode.ByFrequency,
                ["General.ComponentChooserDummy"] = ComponentChooserDummy.SelectToToggle,

                ["Recovery.EnableRecoveryFeature"] = true,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Recover,
                ["Recovery.RecoverBaggedBosses"] = true,
                ["Recovery.RecoverBaggedNPCs"] = true,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = true,

                ["Persistence.EnableObjectPersistence"] = true,
                ["Persistence.EnableAutoGrab"] = true,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.PersistenceBlacklist"] = "",

                ["BottomlessBag.EnableBottomlessBag"] = true,
                ["BottomlessBag.EnableStockRefreshClamping"] = true,
                ["BottomlessBag.EnableSuccessiveGrabStockRefresh"] = true,

                ["Hud.EnableCarouselHUD"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.EnableBaggedObjectInfo"] = true,

                ["Hud.SideSlotShowIcon"] = true,
                ["Hud.SideSlotShowWeightIcon"] = false,

                ["Hud.EnableDamagePreview"] = false,
                ["Hud.DamagePreviewColor"] = new Color(1f, 0.15f, 0.15f, 0.8f),
                ["Hud.UseNewWeightIcon"] = true,
                ["Hud.WeightDisplayMode"] = WeightDisplayMode.KiloGrams,
                ["Hud.ScaleWeightColor"] = true,
                ["Hud.ShowTotalMassOnWeightIcon"] = true,

                ["Balance.EnableBalance"] = true,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Split,
                ["BottomlessBag.SlotScalingFormula"] = "INF",
                ["Balance.MassCapacityFormula"] = "700*C + H*S*0.1 + L*50",
                ["Character Flags.EliteFlagMultiplier"] = "1",
                ["Character Flags.BossFlagMultiplier"] = "1",
                ["Character Flags.ChampionFlagMultiplier"] = "1",
                ["Character Flags.PlayerFlagMultiplier"] = "1",
                ["Character Flags.MinionFlagMultiplier"] = "1",
                ["Character Flags.DroneFlagMultiplier"] = "1",
                ["Character Flags.MechanicalFlagMultiplier"] = "1",
                ["Character Flags.VoidFlagMultiplier"] = "1",
                ["Character Flags.AllFlagMultiplier"] = "H/max(B,1)",
                ["Balance.OverencumbranceMax"] = 100.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.All,

                ["Balance.MovespeedPenaltyFormula"] = "clamp((T/M) * 0.25, 0, 0.5)",
                ["Balance.BagScaleCap"] = "1",
                ["Balance.MassCap"] = "999",
                ["Balance.MaxLaunchSpeed"] = "30",
                ["Balance.BreakoutTimeMultiplier"] = 1f,
                ["Balance.SearchRadiusMultiplier"] = 1.0f,
                ["Balance.MaxSmacks"] = 3,
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // MINIMAL PRESET
            // ========================================================================================
            [PresetType.Minimal] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = true,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.None,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                ["General.GrabbableKeywordBlacklist"] = "Master,Controller",
                ["General.ComponentChooserSortMode"] = ComponentChooserSortMode.ByFrequency,
                ["General.ComponentChooserDummy"] = ComponentChooserDummy.SelectToToggle,

                ["Recovery.EnableRecoveryFeature"] = false,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Kill,
                ["Recovery.RecoverBaggedBosses"] = false,
                ["Recovery.RecoverBaggedNPCs"] = false,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = false,

                ["Persistence.EnableObjectPersistence"] = false,
                ["Persistence.PersistenceBlacklist"] = "",
                ["BottomlessBag.EnableBottomlessBag"] = false,
                ["BottomlessBag.SlotScalingFormula"] = "C + 2",
                ["Hud.EnableCarouselHUD"] = false,
                ["Hud.EnableDamagePreview"] = false,
                ["Hud.EnableMassCapacityUI"] = false,
                ["Hud.EnableBaggedObjectInfo"] = false,
                ["Balance.EnableBalance"] = false,
                ["Balance.BreakoutTimeMultiplier"] = 1.0f,
                ["Balance.MaxSmacks"] = 3,
                ["Balance.MaxLaunchSpeed"] = "30",
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["BottomlessBag.SlotScalingFormula"] = "C + 2",
                ["Balance.MassCapacityFormula"] = "",
                ["Balance.OverencumbranceMax"] = 0.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.MovespeedPenaltyFormula"] = "clamp((T/M) * 0.25, 0, 0.5)",
                ["Balance.BagScaleCap"] = "1",
                ["Balance.MassCap"] = "700",
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // HARDCORE PRESET
            // ========================================================================================
            [PresetType.Hardcore] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = false,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = false,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.None,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                ["General.GrabbableKeywordBlacklist"] = "Master,Controller",
                ["General.ComponentChooserSortMode"] = ComponentChooserSortMode.ByFrequency,
                ["General.ComponentChooserDummy"] = ComponentChooserDummy.SelectToToggle,

                ["Recovery.EnableRecoveryFeature"] = false,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Kill,
                ["Recovery.RecoverBaggedBosses"] = false,
                ["Recovery.RecoverBaggedNPCs"] = false,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = false,

                ["Persistence.EnableObjectPersistence"] = false,
                ["Persistence.PersistenceBlacklist"] = "",

                ["BottomlessBag.EnableBottomlessBag"] = true,
                ["BottomlessBag.EnableStockRefreshClamping"] = true,
                ["BottomlessBag.EnableSuccessiveGrabStockRefresh"] = false,

                ["Hud.EnableCarouselHUD"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableBaggedObjectInfo"] = true,

                ["Balance.EnableBalance"] = true,
                ["Balance.BreakoutTimeMultiplier"] = 0.7f,
                ["Balance.MaxSmacks"] = 3,
                ["Balance.MaxLaunchSpeed"] = "50",
                ["Balance.SearchRadiusMultiplier"] = 1.0f,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["BottomlessBag.SlotScalingFormula"] = "max(0, (C + floor(S/2)) - floor(L/5) - floor(H/2000))",
                ["Balance.MassCapacityFormula"] = "max(100, 700 + S*100 - L*50 - H/20)",
                ["Character Flags.EliteFlagMultiplier"] = "1.25",
                ["Character Flags.BossFlagMultiplier"] = "2",
                ["Character Flags.ChampionFlagMultiplier"] = "2",
                ["Character Flags.PlayerFlagMultiplier"] = "1",
                ["Character Flags.MinionFlagMultiplier"] = "1",
                ["Character Flags.DroneFlagMultiplier"] = "1",
                ["Character Flags.MechanicalFlagMultiplier"] = "1.5",
                ["Character Flags.VoidFlagMultiplier"] = "1",
                ["Character Flags.AllFlagMultiplier"] = "H/max(B,1)",
                ["Balance.OverencumbranceMax"] = 75.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.All,
                ["Balance.MovespeedPenaltyFormula"] = "clamp((T/M) * 0.34, 0, 0.99)",
                ["Balance.BagScaleCap"] = "1",
                ["Balance.MassCap"] = "INF",
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // CAVEMAN PRESET
            // ========================================================================================
            [PresetType.Caveman] = new Dictionary<string, object>
            {

                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = true,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.AllProjectiles,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.BodyBlacklist"] = "",
                ["General.RecoveryObjectBlacklist"] = "",
                ["General.GrabbableComponentTypes"] = "MeshRenderer",
                ["General.GrabbableKeywordBlacklist"] = "",

                ["Recovery.EnableRecoveryFeature"] = true,
                ["Recovery.EnemyRecoveryMode"] = EnemyRecoveryMode.Recover,
                ["Recovery.RecoverBaggedBosses"] = true,
                ["Recovery.RecoverBaggedNPCs"] = true,
                ["Recovery.RecoverBaggedEnvironmentObjects"] = true,

                ["Persistence.EnableObjectPersistence"] = true,
                ["Persistence.EnableAutoGrab"] = true,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.PersistenceBlacklist"] = "",

                ["BottomlessBag.EnableBottomlessBag"] = true,
                ["BottomlessBag.EnableStockRefreshClamping"] = false,
                ["BottomlessBag.EnableSuccessiveGrabStockRefresh"] = true,

                ["Hud.EnableCarouselHUD"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableBaggedObjectInfo"] = true,

                ["Balance.EnableBalance"] = true,
                ["Balance.BreakoutTimeMultiplier"] = 100.0f,
                ["Balance.MaxSmacks"] = 100,
                ["Balance.MaxLaunchSpeed"] = "INF",
                ["Balance.SearchRadiusMultiplier"] = 50.0f,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["BottomlessBag.SlotScalingFormula"] = "INF",
                ["Balance.MassCapacityFormula"] = "",
                ["Character Flags.EliteFlagMultiplier"] = "1",
                ["Character Flags.BossFlagMultiplier"] = "1",
                ["Character Flags.ChampionFlagMultiplier"] = "1",
                ["Character Flags.PlayerFlagMultiplier"] = "1",
                ["Character Flags.MinionFlagMultiplier"] = "1",
                ["Character Flags.DroneFlagMultiplier"] = "1",
                ["Character Flags.MechanicalFlagMultiplier"] = "1",
                ["Character Flags.VoidFlagMultiplier"] = "1",
                ["Character Flags.AllFlagMultiplier"] = "1",
                ["Balance.OverencumbranceMax"] = 0.0f,
                ["Balance.StateCalculationMode"] = StateCalculationMode.All,
                ["Balance.MovespeedPenaltyFormula"] = "0",
                ["Balance.BagScaleCap"] = "INF",
                ["Balance.MassCap"] = "INF",
                ["Balance.SlamDamageFormula"] = "BASE_COEF + (MASS_SCALING * BM / MC)",
            },

            // ========================================================================================
            // CUSTOM PRESET
            // ========================================================================================
            [PresetType.Custom] = new Dictionary<string, object>(),
        };
    }
}
