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
            Instance.EnableStockRefreshClamping = cfg.Bind("Bottomless Bag", "EnableStockRefreshClamping", false, "Limit Repossess recharge to the remaining bag slots, using the final bag capacity. Keeps at least 1 stock available and does not raise your normal stock limit.");
            Instance.EnableSuccessiveGrabStockRefresh = cfg.Bind("Bottomless Bag", "EnableSuccessiveGrabStockRefresh", false, "Refresh Repossess stocks after a successful grab leaves you at 0.");
            Instance.CycleCooldown = cfg.Bind("Bottomless Bag", "CycleCooldown", 0.2f, "Seconds between passenger switches.");
            Instance.PlayAnimationOnCycle = cfg.Bind("Bottomless Bag", "PlayAnimationOnCycle", false, "Play the grab animation when switching passengers.");
            Instance.EnableMouseWheelScrolling = cfg.Bind("Bottomless Bag", "EnableMouseWheelScrolling", true, "Switch passengers with the mouse wheel.");
            Instance.InverseMouseWheelScrolling = cfg.Bind("Bottomless Bag", "InverseMouseWheelScrolling", false, "Reverse the mouse wheel direction for switching passengers.");
            Instance.AutoPromoteMainSeat = cfg.Bind("Bottomless Bag", "AutoPromoteMainSeat", false, "Select the next passenger when the main passenger is removed.");
            Instance.PrioritizeMainSeat = cfg.Bind("Bottomless Bag", "PrioritizeMainSeat", false, "Make newly grabbed objects the main passenger.");
            Instance.SlotScalingFormula = cfg.Bind("Bottomless Bag", "SlotScalingFormula", "C + 2", "Set the number of bag slots. Variables: H (Max HP), L (Level), C (Stocks), MC (Mass Cap), S (Stage). Use INF for unlimited slots.");

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
