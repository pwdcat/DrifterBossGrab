using System.Collections.Generic;
using UnityEngine;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.Config
{
    // Hardcoded preset definitions for DrifterBossGrabMod.
    public static class PresetDefinitions
    {
        public static readonly Dictionary<PresetType, Dictionary<string, object>> Presets = new()
        {
            // Vanilla: All features disabled, vanilla behavior
            [PresetType.Vanilla] = new Dictionary<string, object>
            {
                // General settings
                ["General.EnableBossGrabbing"] = false,
                ["General.EnableNPCGrabbing"] = false,
                ["General.EnableEnvironmentGrabbing"] = false,
                ["General.EnableLockedObjectGrabbing"] = false,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.None,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableComponentAnalysisLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.MassMultiplier"] = "1",

                // Skill settings
                ["Skill.SearchRangeMultiplier"] = 1.0f,
                ["Skill.ForwardVelocityMultiplier"] = 1.0f,
                ["Skill.UpwardVelocityMultiplier"] = 1.0f,
                ["Skill.BreakoutTimeMultiplier"] = 1.0f,
                ["Skill.MaxSmacks"] = 3,
                ["Skill.MassMultiplier"] = "1",

                // Persistence settings
                ["Persistence.EnableObjectPersistence"] = false,
                ["Persistence.EnableAutoGrab"] = false,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.AutoGrabDelay"] = 1.0f,

                // Bottomless Bag settings
                ["BottomlessBag.EnableBottomlessBag"] = false,
                ["BottomlessBag.BaseCapacity"] = 0,
                ["BottomlessBag.EnableStockRefreshClamping"] = false,
                ["BottomlessBag.CycleCooldown"] = 0.2f,
                ["BottomlessBag.EnableMouseWheelScrolling"] = true,
                ["BottomlessBag.InverseMouseWheelScrolling"] = false,
                ["BottomlessBag.AutoPromoteMainSeat"] = true,

                // HUD settings
                ["Hud.EnableCarouselHUD"] = false,
                ["Hud.CarouselSpacing"] = 45.0f,
                ["Hud.CarouselCenterOffsetX"] = 25.0f,
                ["Hud.CarouselCenterOffsetY"] = 50.0f,
                ["Hud.CarouselSideOffsetX"] = 20.0f,
                ["Hud.CarouselSideOffsetY"] = 5.0f,
                ["Hud.CarouselSideScale"] = 0.8f,
                ["Hud.CarouselSideOpacity"] = 0.3f,
                ["Hud.CarouselAnimationDuration"] = 0.4f,
                ["Hud.BagUIShowIcon"] = true,
                ["Hud.BagUIShowWeight"] = true,
                ["Hud.BagUIShowName"] = true,
                ["Hud.BagUIShowHealthBar"] = true,
                ["Hud.EnableDamagePreview"] = false,
                ["Hud.DamagePreviewColor"] = new Color(1f, 0.15f, 0.15f, 0.8f),
                ["Hud.UseNewWeightIcon"] = false,
                ["Hud.WeightDisplayMode"] = WeightDisplayMode.Multiplier,
                ["Hud.ScaleWeightColor"] = true,
                ["Hud.EnableMassCapacityUI"] = false,
                ["Hud.MassCapacityUIPositionX"] = -20.0f,
                ["Hud.MassCapacityUIPositionY"] = 0.0f,
                ["Hud.MassCapacityUIScale"] = 0.8f,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableGradient"] = true,
                ["Hud.GradientIntensity"] = 1.0f,

                // Balance settings
                ["Balance.EnableBalance"] = false,
                ["Balance.EnableAoESlamDamage"] = false,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["Balance.CapacityScalingMode"] = CapacityScalingMode.IncreaseCapacity,
                ["Balance.CapacityScalingType"] = ScalingType.Exponential,
                ["Balance.CapacityScalingBonusPerCapacity"] = 100.0f,
                ["Balance.HealthPerExtraSlot"] = 0.0f,
                ["Balance.LevelsPerExtraSlot"] = 0,
                ["Balance.EnableOverencumbrance"] = true,
                ["Balance.OverencumbranceMaxPercent"] = 100.0f,
                ["Balance.UncapCapacity"] = false,
                ["Balance.ToggleMassCapacity"] = true,
                ["Balance.StateCalculationModeEnabled"] = false,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.AllModeMassMultiplier"] = 1.0f,
                ["Balance.MinMovespeedPenalty"] = 0.0f,
                ["Balance.MaxMovespeedPenalty"] = 0.5f,
                ["Balance.FinalMovespeedPenaltyLimit"] = 0.8f,
                ["Balance.UncapBagScale"] = false,
                ["Balance.UncapMass"] = false,
            },

            // Intended: Boss grab only
            [PresetType.Intended] = new Dictionary<string, object>
            {
                // General settings
                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = false,
                ["General.EnableEnvironmentGrabbing"] = false,
                ["General.EnableLockedObjectGrabbing"] = false,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.None,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableComponentAnalysisLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.MassMultiplier"] = "1",

                // Skill settings
                ["Skill.SearchRangeMultiplier"] = 1.0f,
                ["Skill.ForwardVelocityMultiplier"] = 1.0f,
                ["Skill.UpwardVelocityMultiplier"] = 1.0f,
                ["Skill.BreakoutTimeMultiplier"] = 1.0f,
                ["Skill.MaxSmacks"] = 3,
                ["Skill.MassMultiplier"] = "1",

                // Persistence settings
                ["Persistence.EnableObjectPersistence"] = false,
                ["Persistence.EnableAutoGrab"] = false,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.AutoGrabDelay"] = 1.0f,

                // Bottomless Bag settings
                ["BottomlessBag.EnableBottomlessBag"] = false,
                ["BottomlessBag.BaseCapacity"] = 0,
                ["BottomlessBag.EnableStockRefreshClamping"] = false,
                ["BottomlessBag.CycleCooldown"] = 0.2f,
                ["BottomlessBag.EnableMouseWheelScrolling"] = true,
                ["BottomlessBag.InverseMouseWheelScrolling"] = false,
                ["BottomlessBag.AutoPromoteMainSeat"] = true,

                // HUD settings
                ["Hud.EnableCarouselHUD"] = false,
                ["Hud.CarouselSpacing"] = 45.0f,
                ["Hud.CarouselCenterOffsetX"] = 25.0f,
                ["Hud.CarouselCenterOffsetY"] = 50.0f,
                ["Hud.CarouselSideOffsetX"] = 20.0f,
                ["Hud.CarouselSideOffsetY"] = 5.0f,
                ["Hud.CarouselSideScale"] = 0.8f,
                ["Hud.CarouselSideOpacity"] = 0.3f,
                ["Hud.CarouselAnimationDuration"] = 0.4f,
                ["Hud.BagUIShowIcon"] = true,
                ["Hud.BagUIShowWeight"] = true,
                ["Hud.BagUIShowName"] = true,
                ["Hud.BagUIShowHealthBar"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.DamagePreviewColor"] = new Color(1f, 0.15f, 0.15f, 0.8f),
                ["Hud.UseNewWeightIcon"] = false,
                ["Hud.WeightDisplayMode"] = WeightDisplayMode.Multiplier,
                ["Hud.ScaleWeightColor"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.MassCapacityUIPositionX"] = -20.0f,
                ["Hud.MassCapacityUIPositionY"] = 0.0f,
                ["Hud.MassCapacityUIScale"] = 0.8f,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableGradient"] = true,
                ["Hud.GradientIntensity"] = 1.0f,

                // Balance settings
                ["Balance.EnableBalance"] = false,
                ["Balance.EnableAoESlamDamage"] = false,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["Balance.CapacityScalingMode"] = CapacityScalingMode.IncreaseCapacity,
                ["Balance.CapacityScalingType"] = ScalingType.Exponential,
                ["Balance.CapacityScalingBonusPerCapacity"] = 100.0f,
                ["Balance.HealthPerExtraSlot"] = 0.0f,
                ["Balance.LevelsPerExtraSlot"] = 0,
                ["Balance.EnableOverencumbrance"] = true,
                ["Balance.OverencumbranceMaxPercent"] = 100.0f,
                ["Balance.UncapCapacity"] = false,
                ["Balance.ToggleMassCapacity"] = true,
                ["Balance.StateCalculationModeEnabled"] = false,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.AllModeMassMultiplier"] = 1.0f,
                ["Balance.MinMovespeedPenalty"] = 0.0f,
                ["Balance.MaxMovespeedPenalty"] = 0.5f,
                ["Balance.FinalMovespeedPenaltyLimit"] = 0.8f,
                ["Balance.UncapBagScale"] = false,
                ["Balance.UncapMass"] = false,
            },

            // Default: All features in DrifterGrabFeature + bottomless bag and persistence
            [PresetType.Default] = new Dictionary<string, object>
            {
                // General settings
                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = true,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.SurvivorOnly,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableComponentAnalysisLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.MassMultiplier"] = "1",

                // Skill settings
                ["Skill.SearchRangeMultiplier"] = 1.0f,
                ["Skill.ForwardVelocityMultiplier"] = 1.0f,
                ["Skill.UpwardVelocityMultiplier"] = 1.0f,
                ["Skill.BreakoutTimeMultiplier"] = 1.0f,
                ["Skill.MaxSmacks"] = 3,
                ["Skill.MassMultiplier"] = "1",

                // Persistence settings
                ["Persistence.EnableObjectPersistence"] = true,
                ["Persistence.EnableAutoGrab"] = true,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.AutoGrabDelay"] = 1.0f,

                // Bottomless Bag settings
                ["BottomlessBag.EnableBottomlessBag"] = true,
                ["BottomlessBag.BaseCapacity"] = 2,
                ["BottomlessBag.EnableStockRefreshClamping"] = true,
                ["BottomlessBag.CycleCooldown"] = 0.2f,
                ["BottomlessBag.EnableMouseWheelScrolling"] = true,
                ["BottomlessBag.InverseMouseWheelScrolling"] = false,
                ["BottomlessBag.AutoPromoteMainSeat"] = true,

                // HUD settings
                ["Hud.EnableCarouselHUD"] = true,
                ["Hud.CarouselSpacing"] = 45.0f,
                ["Hud.CarouselCenterOffsetX"] = 25.0f,
                ["Hud.CarouselCenterOffsetY"] = 50.0f,
                ["Hud.CarouselSideOffsetX"] = 20.0f,
                ["Hud.CarouselSideOffsetY"] = 5.0f,
                ["Hud.CarouselSideScale"] = 0.8f,
                ["Hud.CarouselSideOpacity"] = 0.3f,
                ["Hud.CarouselAnimationDuration"] = 0.4f,
                ["Hud.BagUIShowIcon"] = true,
                ["Hud.BagUIShowWeight"] = true,
                ["Hud.BagUIShowName"] = true,
                ["Hud.BagUIShowHealthBar"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.DamagePreviewColor"] = new Color(1f, 0.15f, 0.15f, 0.8f),
                ["Hud.UseNewWeightIcon"] = false,
                ["Hud.WeightDisplayMode"] = WeightDisplayMode.Multiplier,
                ["Hud.ScaleWeightColor"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.MassCapacityUIPositionX"] = -20.0f,
                ["Hud.MassCapacityUIPositionY"] = 0.0f,
                ["Hud.MassCapacityUIScale"] = 0.8f,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableGradient"] = true,
                ["Hud.GradientIntensity"] = 1.0f,

                // Balance settings
                ["Balance.EnableBalance"] = false,
                ["Balance.EnableAoESlamDamage"] = false,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Full,
                ["Balance.CapacityScalingMode"] = CapacityScalingMode.IncreaseCapacity,
                ["Balance.CapacityScalingType"] = ScalingType.Exponential,
                ["Balance.CapacityScalingBonusPerCapacity"] = 100.0f,
                ["Balance.HealthPerExtraSlot"] = 0.0f,
                ["Balance.LevelsPerExtraSlot"] = 0,
                ["Balance.EnableOverencumbrance"] = true,
                ["Balance.OverencumbranceMaxPercent"] = 100.0f,
                ["Balance.UncapCapacity"] = false,
                ["Balance.ToggleMassCapacity"] = true,
                ["Balance.StateCalculationModeEnabled"] = false,
                ["Balance.StateCalculationMode"] = StateCalculationMode.Current,
                ["Balance.AllModeMassMultiplier"] = 1.0f,
                ["Balance.MinMovespeedPenalty"] = 0.0f,
                ["Balance.MaxMovespeedPenalty"] = 0.5f,
                ["Balance.FinalMovespeedPenaltyLimit"] = 0.8f,
                ["Balance.UncapBagScale"] = false,
                ["Balance.UncapMass"] = false,
            },

            // Balance: Default + balance features
            [PresetType.Balance] = new Dictionary<string, object>
            {
                // General settings
                ["General.EnableBossGrabbing"] = true,
                ["General.EnableNPCGrabbing"] = true,
                ["General.EnableEnvironmentGrabbing"] = true,
                ["General.EnableLockedObjectGrabbing"] = true,
                ["General.ProjectileGrabbingMode"] = ProjectileGrabbingMode.SurvivorOnly,
                ["General.EnableDebugLogs"] = false,
                ["General.EnableComponentAnalysisLogs"] = false,
                ["General.EnableConfigSync"] = true,
                ["General.MassMultiplier"] = "1",

                // Skill settings
                ["Skill.SearchRangeMultiplier"] = 1.0f,
                ["Skill.ForwardVelocityMultiplier"] = 1.0f,
                ["Skill.UpwardVelocityMultiplier"] = 1.0f,
                ["Skill.BreakoutTimeMultiplier"] = 1.0f,
                ["Skill.MaxSmacks"] = 3,
                ["Skill.MassMultiplier"] = "1",

                // Persistence settings
                ["Persistence.EnableObjectPersistence"] = true,
                ["Persistence.EnableAutoGrab"] = true,
                ["Persistence.PersistBaggedBosses"] = true,
                ["Persistence.PersistBaggedNPCs"] = true,
                ["Persistence.PersistBaggedEnvironmentObjects"] = true,
                ["Persistence.AutoGrabDelay"] = 1.0f,

                // Bottomless Bag settings
                ["BottomlessBag.EnableBottomlessBag"] = true,
                ["BottomlessBag.BaseCapacity"] = 2,
                ["BottomlessBag.EnableStockRefreshClamping"] = true,
                ["BottomlessBag.CycleCooldown"] = 0.2f,
                ["BottomlessBag.EnableMouseWheelScrolling"] = true,
                ["BottomlessBag.InverseMouseWheelScrolling"] = false,
                ["BottomlessBag.AutoPromoteMainSeat"] = true,

                // HUD settings
                ["Hud.EnableCarouselHUD"] = true,
                ["Hud.CarouselSpacing"] = 45.0f,
                ["Hud.CarouselCenterOffsetX"] = 25.0f,
                ["Hud.CarouselCenterOffsetY"] = 50.0f,
                ["Hud.CarouselSideOffsetX"] = 20.0f,
                ["Hud.CarouselSideOffsetY"] = 5.0f,
                ["Hud.CarouselSideScale"] = 0.8f,
                ["Hud.CarouselSideOpacity"] = 0.3f,
                ["Hud.CarouselAnimationDuration"] = 0.4f,
                ["Hud.BagUIShowIcon"] = true,
                ["Hud.BagUIShowWeight"] = true,
                ["Hud.BagUIShowName"] = true,
                ["Hud.BagUIShowHealthBar"] = true,
                ["Hud.EnableDamagePreview"] = true,
                ["Hud.DamagePreviewColor"] = new Color(1f, 0.15f, 0.15f, 0.8f),
                ["Hud.UseNewWeightIcon"] = false,
                ["Hud.WeightDisplayMode"] = WeightDisplayMode.Multiplier,
                ["Hud.ScaleWeightColor"] = true,
                ["Hud.EnableMassCapacityUI"] = true,
                ["Hud.MassCapacityUIPositionX"] = -20.0f,
                ["Hud.MassCapacityUIPositionY"] = 0.0f,
                ["Hud.MassCapacityUIScale"] = 0.8f,
                ["Hud.EnableSeparators"] = true,
                ["Hud.EnableGradient"] = true,
                ["Hud.GradientIntensity"] = 1.0f,

                // Balance settings
                ["Balance.EnableBalance"] = true,
                ["Balance.EnableAoESlamDamage"] = true,
                ["Balance.AoEDamageDistribution"] = AoEDamageMode.Split,
                ["Balance.CapacityScalingMode"] = CapacityScalingMode.IncreaseCapacity,
                ["Balance.CapacityScalingType"] = ScalingType.Exponential,
                ["Balance.CapacityScalingBonusPerCapacity"] = 100.0f,
                ["Balance.HealthPerExtraSlot"] = 100.0f,
                ["Balance.LevelsPerExtraSlot"] = 3,
                ["Balance.EnableOverencumbrance"] = true,
                ["Balance.OverencumbranceMaxPercent"] = 100.0f,
                ["Balance.UncapCapacity"] = false,
                ["Balance.ToggleMassCapacity"] = true,
                ["Balance.StateCalculationModeEnabled"] = true,
                ["Balance.StateCalculationMode"] = StateCalculationMode.All,
                ["Balance.AllModeMassMultiplier"] = 1.0f,
                ["Balance.MinMovespeedPenalty"] = 0.0f,
                ["Balance.MaxMovespeedPenalty"] = 0.5f,
                ["Balance.FinalMovespeedPenaltyLimit"] = 0.8f,
                ["Balance.UncapBagScale"] = false,
                ["Balance.UncapMass"] = true,
            },

            // Custom: Placeholder for user-modified settings
            [PresetType.Custom] = new Dictionary<string, object>(),
        };
    }
}
