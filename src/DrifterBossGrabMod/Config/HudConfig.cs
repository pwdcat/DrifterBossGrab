#nullable enable
using BepInEx.Configuration;
using UnityEngine;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitHudConfig(ConfigFile cfg)
        {
            Instance.EnableCarouselHUD = cfg.Bind("Hud", "EnableCarouselHUD", false, "Enable the custom Carousel HUD.");
            Instance.EnableCardReleaseAnimation = cfg.Bind("Hud", "EnableCardReleaseAnimation", true, "Animate released cards flying out of the carousel.");
            Instance.CardReleaseDuration = cfg.Bind("Hud", "CardReleaseDuration", 0.28f, new ConfigDescription("Release animation duration in seconds.", new AcceptableValueRange<float>(0.05f, 0.8f)));
            Instance.CardReleaseDistance = cfg.Bind("Hud", "CardReleaseDistance", 90f, new ConfigDescription("Release travel distance in HUD units.", new AcceptableValueRange<float>(0.0f, 180.0f)));
            Instance.EnableCardDestructionAnimation = cfg.Bind("Hud", "EnableCardDestructionAnimation", true, "Shrink and fade cards when their passenger is destroyed.");
            Instance.CardDestructionDuration = cfg.Bind("Hud", "CardDestructionDuration", 0.18f, new ConfigDescription("Destruction animation duration in seconds.", new AcceptableValueRange<float>(0.05f, 0.8f)));
            Instance.BreakoutWarningStrength = cfg.Bind("Hud", "BreakoutWarningStrength", 5f, new ConfigDescription("Portrait wobble strength before escape attempts. Zero disables movement.", new AcceptableValueRange<float>(0.0f, 10.0f)));
            Instance.EnableBreakoutWarning = cfg.Bind("Hud", "EnableBreakoutWarning", true, "Shake passenger portraits before escape attempts.");
            Instance.EnableCardDamageWiggle = cfg.Bind("Hud", "EnableCardDamageWiggle", true, "Wiggle passenger portraits when bag slams deal damage or reduce durability.");
            Instance.CardDamageWiggleStrength = cfg.Bind("Hud", "CardDamageWiggleStrength", 1f, new ConfigDescription("Damage wiggle strength. Zero disables movement.", new AcceptableValueRange<float>(0f, 10f)));
            Instance.CarouselOrientation = cfg.Bind("Hud", "CarouselOrientation", DrifterBossGrabMod.CarouselOrientation.Vertical, "Layout orientation for carousel items (Vertical or Horizontal).");
            Instance.SelectedCardPlacement = cfg.Bind("Hud", "SelectedCardPlacement", SelectedCardPosition.Center, "Selected card position: Center, Start (top/left), or End (bottom/right).");
            Instance.VisibleCardCount = cfg.Bind("Hud", "VisibleCardCount", 3, new ConfigDescription("Maximum number of visible carousel cards.", new AcceptableValueRange<int>(1, 100)));
            Instance.CarouselSpacing = cfg.Bind("Hud", "CarouselSpacing", 45.0f, "Spacing between carousel items.");
            Instance.CarouselAnimationDuration = cfg.Bind("Hud", "CarouselAnimationDuration", 0.4f, "Duration of carousel animation.");
            Instance.EnableCarouselInactivityFade = cfg.Bind("Hud", "EnableCarouselInactivityFade", false, "Fade out the carousel after a period of inactivity.");
            Instance.CarouselInactivityFadeDelay = cfg.Bind("Hud", "CarouselInactivityFadeDelay", 3.0f, "Seconds of inactivity before the carousel starts fading.");
            Instance.CarouselInactivityFadeDuration = cfg.Bind("Hud", "CarouselInactivityFadeDuration", 0.2f, "Duration of the carousel fade-out animation in seconds.");
            Instance.CarouselInactivityFadeOpacity = cfg.Bind("Hud", "CarouselInactivityFadeOpacity", 0.0f, "Target opacity when the carousel is inactive (0 = hidden).");

            Instance.CenterSlotX = cfg.Bind("Hud", "CenterSlotX", 25.0f, "X position offset for center slot.");
            Instance.CenterSlotY = cfg.Bind("Hud", "CenterSlotY", 50.0f, "Y position offset for center slot.");
            Instance.CenterSlotScale = cfg.Bind("Hud", "CenterSlotScale", 1.0f, "Scale for center slot.");
            Instance.CenterSlotOpacity = cfg.Bind("Hud", "CenterSlotOpacity", 1.0f, "Opacity for center slot.");
            Instance.CenterSlotShowIcon = cfg.Bind("Hud", "CenterSlotShowIcon", true, "Show icon in center slot.");
            Instance.CenterSlotShowBackground = cfg.Bind("Hud", "CenterSlotShowBackground", true, "Show background in center slot.");
            Instance.CenterSlotShowWeightIcon = cfg.Bind("Hud", "CenterSlotShowWeightIcon", true, "Show weight icon in center slot.");
            Instance.CenterSlotShowName = cfg.Bind("Hud", "CenterSlotShowName", true, "Show name in center slot.");
            Instance.CenterSlotShowHealthBar = cfg.Bind("Hud", "CenterSlotShowHealthBar", true, "Show health bar in center slot.");
            Instance.CenterSlotShowSlotNumber = cfg.Bind("Hud", "CenterSlotShowSlotNumber", true, "Show slot number in center slot.");

            Instance.SideSlotX = cfg.Bind("Hud", "SideSlotX", 20.0f, "X position offset for side slots.");
            Instance.SideSlotY = cfg.Bind("Hud", "SideSlotY", 5.0f, "Y position offset for side slots.");
            Instance.SideSlotScale = cfg.Bind("Hud", "SideSlotScale", 0.8f, "Scale for side slots.");
            Instance.SideSlotOpacity = cfg.Bind("Hud", "SideSlotOpacity", 0.3f, "Opacity for side slots.");
            Instance.SideSlotShowIcon = cfg.Bind("Hud", "SideSlotShowIcon", true, "Show icon in side slots.");
            Instance.SideSlotShowBackground = cfg.Bind("Hud", "SideSlotShowBackground", true, "Show background in side slots.");
            Instance.SideSlotShowWeightIcon = cfg.Bind("Hud", "SideSlotShowWeightIcon", true, "Show weight icon in side slots.");
            Instance.SideSlotShowName = cfg.Bind("Hud", "SideSlotShowName", true, "Show name in side slots.");
            Instance.SideSlotShowHealthBar = cfg.Bind("Hud", "SideSlotShowHealthBar", true, "Show health bar in side slots.");
            Instance.SideSlotShowSlotNumber = cfg.Bind("Hud", "SideSlotShowSlotNumber", true, "Show slot number in side slots.");

            Instance.SelectedHudElement = cfg.Bind("Hidden", "SelectedHudElement", HudElementType.All,
                "Select which HUD element group to configure.");

            Instance.EnableBaggedObjectInfo = cfg.Bind("Hud", "EnableBaggedObjectInfo", false, "Show breakout time and extra bag details while holding Info/Tab. Requires Carousel HUD.");
            Instance.BaggedObjectInfoScale = cfg.Bind("Hud", "BaggedObjectInfoScale", 1.0f, "Text scale for integrated carousel details.");
            Instance.BaggedObjectInfoColor = cfg.Bind("Hud", "BaggedObjectInfoColor", new Color(1f, 1f, 1f, 0.9f), "Text color for integrated carousel details.");
            Instance.UseNewWeightIcon = cfg.Bind("Hud", "UseNewWeightIcon", false, "Use the custom weight icon.");
            Instance.WeightDisplayMode = cfg.Bind("Hud", "WeightDisplayMode", DrifterBossGrabMod.WeightDisplayMode.Multiplier, "Mode for weight display.");
            Instance.ScaleWeightColor = cfg.Bind("Hud", "ScaleWeightColor", true, "Scale weight icon color by capacity.");
            Instance.ShowTotalMassOnWeightIcon = cfg.Bind("Hud", "ShowTotalMassOnWeightIcon", false, "Show total bag mass on center slot.");
            Instance.ShowOverencumberIcon = cfg.Bind("Hud", "ShowOverencumberIcon", false, "Show overencumbrance icon.");
            Instance.EnableDamagePreview = cfg.Bind("Hud", "EnableDamagePreview", false, "Show damage preview overlay.");
            Instance.DamagePreviewColor = cfg.Bind("Hud", "DamagePreviewColor", new Color(1f, 0.15f, 0.15f, 0.8f), "Color for damage preview.");
            Instance.SmoothCapacityFill = cfg.Bind("Hud", "SmoothCapacityFill", true, "Smooth changes to capacity and overencumbrance fill.");
            Instance.CapacityFillSmoothTime = cfg.Bind("Hud", "CapacityFillSmoothTime", 0.1f, new ConfigDescription("Fill smoothing time in seconds.", new AcceptableValueRange<float>(0.02f, 0.3f)));
            Instance.EnableCapacityFlashes = cfg.Bind("Hud", "EnableCapacityFlashes", true, "Highlight new fill and flash at capacity and overencumbrance limits.");
            Instance.CapacityFlashStrength = cfg.Bind("Hud", "CapacityFlashStrength", 1f, new ConfigDescription("Flash strength relative to the default.", new AcceptableValueRange<float>(0.0f, 1.0f)));
            Instance.CapacityFlashDuration = cfg.Bind("Hud", "CapacityFlashDuration", 0.32f, new ConfigDescription("Limit flash duration in seconds.", new AcceptableValueRange<float>(0.1f, 0.8f)));
            Instance.EnableMassCapacityUI = cfg.Bind("Hud", "EnableMassCapacityUI", false, "Enable the Mass Capacity UI bar.");
            Instance.MassCapacityUIPositionX = cfg.Bind("Hud", "MassCapacityUIPositionX", -20.0f, "X offset for Mass Capacity UI.");
            Instance.MassCapacityUIPositionY = cfg.Bind("Hud", "MassCapacityUIPositionY", 0.0f, "Y offset for Mass Capacity UI.");
            Instance.MassCapacityUIScale = cfg.Bind("Hud", "MassCapacityUIScale", 0.8f, "Scale for Mass Capacity UI.");
            Instance.EnableSeparators = cfg.Bind("Hud", "EnableSeparators", true, "Show threshold pips on Mass Capacity UI.");
            Instance.GradientIntensity = cfg.Bind("Hud", "GradientIntensity", 1.0f, "Intensity of the gradient color.");

            Instance.CapacityGradientColorStart = cfg.Bind("Hud", "CapacityGradientColorStart", new Color(0.0f, 1.0f, 0.0f, 1.0f), "Start color for standard capacity gradient.");
            Instance.CapacityGradientColorMid = cfg.Bind("Hud", "CapacityGradientColorMid", new Color(1.0f, 1.0f, 0.0f, 1.0f), "Mid color for standard capacity gradient.");
            Instance.CapacityGradientColorEnd = cfg.Bind("Hud", "CapacityGradientColorEnd", new Color(1.0f, 0.0f, 0.0f, 1.0f), "End color for standard capacity gradient.");

            Instance.OverencumbranceGradientColorStart = cfg.Bind("Hud", "OverencumbranceGradientColorStart", new Color(0f, 1.0f, 1.0f, 1.0f), "Start color for overencumbrance gradient.");
            Instance.OverencumbranceGradientColorMid = cfg.Bind("Hud", "OverencumbranceGradientColorMid", new Color(0.0f, 0.0f, 0.5f, 1.0f), "Mid color for overencumbrance gradient.");
            Instance.OverencumbranceGradientColorEnd = cfg.Bind("Hud", "OverencumbranceGradientColorEnd", new Color(0.0f, 0.0f, 1.0f, 1.0f), "End color for overencumbrance gradient.");
            Instance.IsHudEditorEnabled = cfg.Bind("Hidden", "IsHudEditorEnabled", false, "Open the HUD editor. Edit your layout in a run or use sample cards from the menus.");
            Instance.IsHudEditorEnabled.Value = false;

            WireHudEventHandlers();
        }

        private static void WireHudEventHandlers()
        {
            Instance.EnableCarouselHUD.SettingChanged += (sender, args) => UpdateBagUIToggles();

            Instance.CarouselOrientation.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SelectedCardPlacement.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.VisibleCardCount.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CarouselSpacing.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CarouselAnimationDuration.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.EnableCarouselInactivityFade.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CarouselInactivityFadeDelay.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CarouselInactivityFadeDuration.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CarouselInactivityFadeOpacity.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowBackground.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowName.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowHealthBar.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowSlotNumber.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowBackground.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowName.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowHealthBar.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowSlotNumber.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.UseNewWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.WeightDisplayMode.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ScaleWeightColor.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ShowTotalMassOnWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ShowOverencumberIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.DamagePreviewColor.SettingChanged += (sender, args) => UpdateDamagePreviewColors();
            Instance.EnableMassCapacityUI.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIPositionX.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIPositionY.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIScale.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.EnableSeparators.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.GradientIntensity.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.CapacityGradientColorStart.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.CapacityGradientColorMid.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.CapacityGradientColorEnd.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.OverencumbranceGradientColorStart.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.OverencumbranceGradientColorMid.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.OverencumbranceGradientColorEnd.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
        }
    }
}
