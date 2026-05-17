#nullable enable
using BepInEx.Configuration;
using UnityEngine;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Balance;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitBalanceConfig(ConfigFile cfg)
        {
            Instance.EnableBalance = cfg.Bind("Balance", "EnableBalance", false, "Enable mass and penalty systems.");
            Instance.MassCapacityFormula = cfg.Bind("Balance", "MassCapacityFormula", "C * MC", "Formula for mass capacity limit. Supported: H (Max HP), L (Level), C (Stocks), MC (Mass Cap config), S (Stage).");
            Instance.MovespeedPenaltyFormula = cfg.Bind("Balance", "MovespeedPenaltyFormula", "0", "Formula for movement speed penalty. Supported: T (Total Mass), M (Mass Cap limit), C (Total Cap), H (Max HP), L (Level), MC (Mass Cap config), S (Stage).");

            Instance.SlamDamageFormula = cfg.Bind("Balance", "SlamDamageFormula",
                "BASE_COEF + (MASS_SCALING * BM / MC)",
                "Formula for slam damage coefficient. Supported: BASE_COEF, MASS_SCALING, BM (Bagged Mass), MC (Mass Cap).");
            Instance.StateCalculationMode = cfg.Bind("Balance", "StateCalculationMode", DrifterBossGrabMod.StateCalculationMode.Current, "State calculation mode for stats.");
            Instance.AoEDamageDistribution = cfg.Bind("Balance", "AoEDamageDistribution", AoEDamageMode.Full, "Mode for AoE damage distribution.");
            Instance.OverencumbranceMax = cfg.Bind("Balance", "OverencumbranceMax", 100.0f, "Maximum overencumbrance percentage.");

            Instance.BreakoutTimeMultiplier = cfg.Bind("Balance", "BreakoutTimeMultiplier", 1.0f, "Multiplier for breakout time.");
            Instance.MaxSmacks = cfg.Bind("Balance", "MaxSmacks", 3, new ConfigDescription("Hits before breakout.", new AcceptableValueRange<int>(1, 100)));
            Instance.MaxLaunchSpeed = cfg.Bind("Balance", "MaxLaunchSpeed", "30", "Maximum launch speed for breakout.");
            Instance.BagScaleCap = cfg.Bind("Balance", "BagScaleCap", "1", "Bag visual size cap.");
            Instance.MassCap = cfg.Bind("Balance", "MassCap", "700", "Mass cap for caught entities.");

            WireBalanceEventHandlers();
        }

        private static void WireBalanceEventHandlers()
        {
            Instance.MassCapacityFormula.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.MassCapacityFormula.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MassCapacityFormula: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.StateCalculationMode.SettingChanged += (sender, args) =>
            {
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateState(bagController);
                }
            };

            Instance.MovespeedPenaltyFormula.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.MovespeedPenaltyFormula.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MovespeedPenaltyFormula: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculatePenalty(bagController);
                }
            };

            Instance.BagScaleCap.SettingChanged += (sender, args) =>
            {
                Instance.RefreshCachedConfigStrings();
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MassCap.SettingChanged += (sender, args) =>
            {
                Instance.RefreshCachedConfigStrings();
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.SlamDamageFormula.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.SlamDamageFormula.Value);
                if (error != null)
                {
                    Log.Warning($"[PluginConfig] Invalid SlamDamageFormula: {error}");
                }

                var overlays = UnityEngine.Object.FindObjectsByType<UI.DamagePreviewOverlay>(FindObjectsSortMode.None);
                foreach (var overlay in overlays)
                {
                    overlay.InvalidateCache();
                }
            };
        }
    }
}
