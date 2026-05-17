#nullable enable
using BepInEx.Configuration;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitBottomlessBagConfig(ConfigFile cfg)
        {
            Instance.BottomlessBagEnabled = cfg.Bind("Bottomless Bag", "EnableBottomlessBag",
                false,
                "Store multiple objects and cycle through them.");
            Instance.EnableStockRefreshClamping = cfg.Bind("Bottomless Bag", "EnableStockRefreshClamping", false, "Clamp stock refresh to empty slots.");
            Instance.EnableSuccessiveGrabStockRefresh = cfg.Bind("Bottomless Bag", "EnableSuccessiveGrabStockRefresh", false, "Refresh stock only after a successful grab at 0.");
            Instance.CycleCooldown = cfg.Bind("Bottomless Bag", "CycleCooldown", 0.2f, "Cooldown between passenger cycles.");
            Instance.PlayAnimationOnCycle = cfg.Bind("Bottomless Bag", "PlayAnimationOnCycle", false, "Play grab animation when cycling.");
            Instance.EnableMouseWheelScrolling = cfg.Bind("Bottomless Bag", "EnableMouseWheelScrolling", true, "Cycle passengers via mouse wheel.");
            Instance.InverseMouseWheelScrolling = cfg.Bind("Bottomless Bag", "InverseMouseWheelScrolling", false, "Invert mouse wheel cycle direction.");
            Instance.AutoPromoteMainSeat = cfg.Bind("Bottomless Bag", "AutoPromoteMainSeat", false, "Auto-promote next object when main is removed.");
            Instance.PrioritizeMainSeat = cfg.Bind("Bottomless Bag", "PrioritizeMainSeat", false, "New objects go to main seat first.");
            Instance.SlotScalingFormula = cfg.Bind("Bottomless Bag", "SlotScalingFormula", "C + 2", "Formula for total bag slots. Supported: H (Max HP), L (Level), C (Stocks), MC (Mass Cap), S (Stage). Set to INF for infinite.");

            if (Instance.BottomlessBagEnabled.Value && !Instance.EnableCarouselHUD.Value)
            {
                Instance.EnableCarouselHUD.Value = true;
            }

            WireBottomlessBagEventHandlers();
        }

        private static void WireBottomlessBagEventHandlers()
        {
            Instance.BottomlessBagEnabled.SettingChanged += (sender, args) =>
            {
                if (Instance.BottomlessBagEnabled.Value && !Instance.EnableCarouselHUD.Value)
                {
                    Instance.EnableCarouselHUD.Value = true;
                }
            };

            Instance.SlotScalingFormula.SettingChanged += (sender, args) =>
            {
                Instance.RefreshCachedConfigStrings();
                var error = Balance.FormulaParser.Validate(Instance.SlotScalingFormula.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid SlotScalingFormula: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    Balance.CapacityScalingSystem.RecalculateCapacity(bagController);
                    Balance.CapacityScalingSystem.RecalculateState(bagController);
                }
            };
        }
    }
}
