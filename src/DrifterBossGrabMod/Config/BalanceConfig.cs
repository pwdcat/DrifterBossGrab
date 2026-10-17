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
            Instance.EnableBalance = cfg.Bind("Balance", "EnableBalance", false, "Enable bag mass limits and penalties.");
            Instance.MassCapacityFormula = cfg.Bind("Balance", "MassCapacityFormula", "C * MC", "Set the bag's mass limit. Variables: H (Max HP), L (Level), C (Stocks), MC (Mass Cap config), S (Stage).");
            Instance.MovespeedPenaltyFormula = cfg.Bind("Balance", "MovespeedPenaltyFormula", "0", "Set the movement speed penalty. Variables: T (Total Mass), M (Mass Cap limit), C (Total Cap), H (Max HP), L (Level), MC (Mass Cap config), S (Stage).");

            Instance.SlamDamageFormula = cfg.Bind("Balance", "SlamDamageFormula",
                "BASE_COEF + (MASS_SCALING * BM / MC)",
                "Set the slam damage multiplier. Variables: BASE_COEF, MASS_SCALING, BM (Bagged Mass), MC (Mass Cap), N (Passenger Count).");
            Instance.StateCalculationMode = cfg.Bind("Balance", "StateCalculationMode", DrifterBossGrabMod.StateCalculationMode.Current, "Use stats from the selected passenger (Current) or the whole bag (All).");
            Instance.AoEDamageDistribution = cfg.Bind("Balance", "AoEDamageDistribution", AoEDamageMode.Full, "Choose how slams damage other passengers. Requires All stats mode. None: selected passenger only. Full: full damage to each. Split: divide damage between passengers.");
            Instance.OverencumbranceMax = cfg.Bind("Balance", "OverencumbranceMax", 100.0f, "Maximum percentage over the bag's mass limit.");

            Instance.BreakoutTimeFormula = cfg.Bind("Balance", "BreakoutTimeFormula", "", "Set the time between breakout attempts as a multiplier. H/L/C refer to Drifter; S=stage, N=passenger count, T=total mass, PM=passenger mass, PH/PL=passenger max health/level, BT=base interval in seconds. Flags such as ELITE/BOSS refer to the passenger (0 or 1). Passenger health, level and flags are 0 for objects without a character body.");
            Instance.GrabRangeFormula = cfg.Bind("Balance", "GrabRangeFormula", "", "Set the grab range multiplier. H/L/C refer to Drifter; S=stage, N=passenger count, T=total mass. Flag variables are not supported.");
            Instance.MaxSmacksFormula = cfg.Bind("Balance", "MaxSmacksFormula", "", "Set how many hits it takes for the selected passenger to break out. H/L/C refer to Drifter; S=stage, N=passenger count, T=total mass, PM=passenger mass, PH/PL=passenger max health/level. Flags such as ELITE/BOSS refer to the passenger (0 or 1). Passenger health, level and flags are 0 for objects without a character body. Rounded down to a whole number. INF disables hit-based breakout.");
            Instance.MaxLaunchSpeed = cfg.Bind("Balance", "MaxLaunchSpeed", "30", "Maximum speed of passengers when they break out.");
            Instance.BagScaleCap = cfg.Bind("Balance", "BagScaleCap", "1", "Maximum visual size of the bag.");
            Instance.MassCap = cfg.Bind("Balance", "MassCap", "700", "Maximum mass counted for each passenger.");

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
        internal ConfigEntry<string> EliteThrowEffectSelection { get; private set; } = null!;
        private static bool updatingEliteSelection;

        private static void InitEliteThrowEffectsConfig(ConfigFile cfg)
        {
            BalanceSettingToSubTab["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.ELITE_EFFECTS.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Formulas };
            Instance.EliteThrowEffectSelection = cfg.Bind("Hidden", "EliteThrowEffectSelection", "", "Elite effects on throw / grab");
            Instance.EliteThrowEffectSelection.SettingChanged += (_, __) =>
            {
                if (updatingEliteSelection) return;
                updatingEliteSelection = true;
                try { API.EliteThrowEffectsAPI.ApplySelectionIds(Instance.EliteThrowEffectSelection.Value); }
                finally { updatingEliteSelection = false; }
            };
            API.EliteThrowEffectsAPI.Initialize(cfg);
        }

        internal static void RefreshEliteThrowSelection()
        {
            if (updatingEliteSelection || Instance.EliteThrowEffectSelection == null) return;
            updatingEliteSelection = true;
            try { Instance.EliteThrowEffectSelection.Value = API.EliteThrowEffectsAPI.SelectionIds; }
            finally { updatingEliteSelection = false; }
        }
    }
}
