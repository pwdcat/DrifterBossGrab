#nullable enable
using System;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    public partial class DrifterBossGrabPlugin
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void SetupRiskOfOptions()
        {
            if (!RooInstalled) return;
            ModSettingsManager.SetModDescription("Allows Drifter to grab bosses, NPCs, and environment objects.", Constants.PluginGuid, Constants.PluginName);
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string assemblyDirectory = System.IO.Path.GetDirectoryName(assembly.Location);
                string iconPath = System.IO.Path.Combine(assemblyDirectory, "icon.png");

                if (File.Exists(iconPath))
                {
                    byte[] array = File.ReadAllBytes(iconPath);
                    Texture2D val = new Texture2D(UI.IconTextureSize, UI.IconTextureSize);
                    UnityEngine.ImageConversion.LoadImage(val, array);
                    ModSettingsManager.SetModIcon(UnityEngine.Sprite.Create(val, new UnityEngine.Rect(UI.IconRectX, UI.IconRectY, UI.IconTextureSize, UI.IconTextureSize), new UnityEngine.Vector2(UI.IconPivotX, UI.IconPivotY)));
                }
                else
                {
                    Log.Warning($"[UI] Mod icon not found at: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[UI] Failed to load mod icon: {ex.Message}");
            }
            AddConfigurationOptions();

            StartCoroutine(DelayedUpdateHudSubTabVisibility());
            StartCoroutine(DelayedUpdateBalanceSubTabVisibility());

            SetupRiskOfOptionsEvents();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedPreset, new ChoiceConfig { name = "Selected Preset", category = "General" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBossGrabbing, new CheckBoxConfig { name = "Enable Boss Grabbing" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableNPCGrabbing, new CheckBoxConfig { name = "Enable NPC Grabbing" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableEnvironmentGrabbing, new CheckBoxConfig { name = "Enable Environment Grabbing" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableLockedObjectGrabbing, new CheckBoxConfig { name = "Enable Locked Object Grabbing" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.ProjectileGrabbingMode, new ChoiceConfig { name = "Projectile Grabbing" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableObjectPersistence, new CheckBoxConfig { name = "Enable Persistence" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableAutoGrab, new CheckBoxConfig { name = "Enable Auto-Grab" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedBosses, new CheckBoxConfig { name = "Persist Bosses" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedNPCs, new CheckBoxConfig { name = "Persist NPCs" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedEnvironmentObjects, new CheckBoxConfig { name = "Persist Environment" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.PersistenceBlacklist, new InputFieldConfig { name = "Persistence Blacklist" }));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.AutoGrabDelay, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Auto-Grab Delay", min = 0f, max = 10f, increment = 0.1f }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.BodyBlacklist, new InputFieldConfig { name = "Grab Blacklist" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableComponentTypes, new InputFieldConfig { name = "Grabbable Components", category = "General" }));
            ModSettingsManager.AddOption(new DrifterBossGrabMod.Config.UI.ComponentChooserOption(PluginConfig.Instance.ComponentChooserDummyEntry, "Component Chooser", "Click to load and toggle components in the GrabbableComponentTypes list.", "General"));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.ComponentChooserSortModeEntry, new ChoiceConfig { name = "Chooser Sort Mode", category = "General" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.RecoveryObjectBlacklist, new InputFieldConfig { name = "Recovery Blacklist" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableKeywordBlacklist, new InputFieldConfig { name = "Keyword Blacklist" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDebugLogs, new CheckBoxConfig { name = "Enable Debug Logs" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableConfigSync, new CheckBoxConfig { name = "Enable Config Sync" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BottomlessBagEnabled, new CheckBoxConfig { name = "Enable Bottomless Bag" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.AddedCapacity, new InputFieldConfig { name = "Extra Bag Capacity" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableStockRefreshClamping, new CheckBoxConfig { name = "Refresh Clamping" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableSuccessiveGrabStockRefresh, new CheckBoxConfig { name = "Successive Grab Refresh" }));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.CycleCooldown, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Cycle Cooldown", min = 0f, max = 1f, increment = 0.01f }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PlayAnimationOnCycle, new CheckBoxConfig { name = "Play Cycle Animation" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMouseWheelScrolling, new CheckBoxConfig { name = "Mouse Wheel Scrolling" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.InverseMouseWheelScrolling, new CheckBoxConfig { name = "Invert Scrolling" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.AutoPromoteMainSeat, new CheckBoxConfig { name = "Auto-Promote Main Seat" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PrioritizeMainSeat, new CheckBoxConfig { name = "Prioritize Main Seat" }));

            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedBalanceSubTab, new ChoiceConfig { name = "Balance Filter", category = "Balance" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBalance, new CheckBoxConfig { name = "Enable Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SlotScalingFormula, new InputFieldConfig { name = "Slot Scaling Formula" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassCapacityFormula, new InputFieldConfig { name = "Mass Capacity Formula" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SlamDamageFormula, new InputFieldConfig { name = "Slam Damage Formula" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MovespeedPenaltyFormula, new InputFieldConfig { name = "Speed Penalty Formula" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.StateCalculationMode, new ChoiceConfig { name = "State Calculation" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.AoEDamageDistribution, new ChoiceConfig { name = "AoE Damage" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.OverencumbranceMax, new FloatFieldConfig { name = "Max Overencumbrance (%)" }));

            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedFlag, new ChoiceConfig { name = "Flag", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SelectedFlagMultiplier, new InputFieldConfig { name = "Multiplier", category = "Balance" }));

            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.SearchRadiusMultiplier, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Grab Range Multiplier", min = 1f, max = 100f, increment = 0.1f }));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.BreakoutTimeMultiplier, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Breakout Time Multiplier" }));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.Instance.MaxSmacks, new IntSliderConfig { name = "Max Hits Before Breakout" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MaxLaunchSpeed, new InputFieldConfig { name = "Max Launch Speed" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.BagScaleCap, new InputFieldConfig { name = "Bag Visual Size Cap" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassCap, new InputFieldConfig { name = "Bagged Entity Mass Cap" }));

            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedHudElement, new ChoiceConfig { name = "HUD Filter", category = "Hud" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableCarouselHUD, new CheckBoxConfig { name = "Enable Carousel HUD" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSpacing, new FloatFieldConfig { name = "Vertical Spacing" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselAnimationDuration, new FloatFieldConfig { name = "Animation Duration" }));

            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotX, new FloatFieldConfig { name = "Main Slot X Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotY, new FloatFieldConfig { name = "Main Slot Y Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotScale, new FloatFieldConfig { name = "Main Slot Scale" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotOpacity, new FloatFieldConfig { name = "Main Slot Opacity" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowIcon, new CheckBoxConfig { name = "Show Icon (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowWeightIcon, new CheckBoxConfig { name = "Show Weight Icon (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowName, new CheckBoxConfig { name = "Show Name (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowHealthBar, new CheckBoxConfig { name = "Show Health (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowSlotNumber, new CheckBoxConfig { name = "Show Slot # (Main)" }));

            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotX, new FloatFieldConfig { name = "Side Slot X Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotY, new FloatFieldConfig { name = "Side Slot Y Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotScale, new FloatFieldConfig { name = "Side Slot Scale" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotOpacity, new FloatFieldConfig { name = "Side Slot Opacity" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowIcon, new CheckBoxConfig { name = "Show Icon (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowWeightIcon, new CheckBoxConfig { name = "Show Weight Icon (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowName, new CheckBoxConfig { name = "Show Name (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowHealthBar, new CheckBoxConfig { name = "Show Health (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowSlotNumber, new CheckBoxConfig { name = "Show Slot # (Side)" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDamagePreview, new CheckBoxConfig { name = "Enable Damage Preview" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.DamagePreviewColor, new ColorOptionConfig { name = "Damage Preview Color" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UseNewWeightIcon, new CheckBoxConfig { name = "Use New Weight Icon" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.WeightDisplayMode, new ChoiceConfig { name = "Weight Display Mode" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ScaleWeightColor, new CheckBoxConfig { name = "Scale Weight Color" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ShowTotalMassOnWeightIcon, new CheckBoxConfig { name = "Show Total Mass" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ShowOverencumberIcon, new CheckBoxConfig { name = "Show Overencumbered Icon" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMassCapacityUI, new CheckBoxConfig { name = "Enable Capacity UI" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIPositionX, new FloatFieldConfig { name = "Capacity UI X Pos" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIPositionY, new FloatFieldConfig { name = "Capacity UI Y Pos" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIScale, new FloatFieldConfig { name = "Capacity UI Scale" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableSeparators, new CheckBoxConfig { name = "Enable Separators" }));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.GradientIntensity, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Gradient Intensity", min = 0f, max = 1f, increment = 0.05f }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorStart, new ColorOptionConfig { name = "Gradient Color Start" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorMid, new ColorOptionConfig { name = "Gradient Color Mid" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorEnd, new ColorOptionConfig { name = "Gradient Color End" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorStart, new ColorOptionConfig { name = "Overencumbrance Start" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorMid, new ColorOptionConfig { name = "Overencumbrance Mid" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorEnd, new ColorOptionConfig { name = "Overencumbrance End" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBaggedObjectInfo, new CheckBoxConfig { name = "Enable Stats Panel" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.BaggedObjectInfoX, new FloatFieldConfig { name = "Stats Panel X Pos" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.BaggedObjectInfoY, new FloatFieldConfig { name = "Stats Panel Y Pos" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.BaggedObjectInfoScale, new FloatFieldConfig { name = "Stats Panel Scale" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.BaggedObjectInfoColor, new ColorOptionConfig { name = "Stats Panel Color" }));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void SetupRiskOfOptionsEvents()
        {
            if (!RooInstalled) return;
            try
            {
                var harmony = new Harmony(Constants.PluginGuid + ".roo_ui");
                var targetMethod = AccessTools.Method(typeof(RiskOfOptions.Components.Panel.ModOptionPanelController), "LoadOptionListFromCategory");
                if (targetMethod != null)
                {
                    var postfixMethod = AccessTools.Method(typeof(DrifterBossGrabPlugin), nameof(OnRooCategoryLoaded));
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfixMethod));
                }
                else
                {
                    Log.Warning("[RiskOfOptions] Failed to find LoadOptionListFromCategory method in RiskOfOptions.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RiskOfOptions] Exception while patching RiskOfOptions: {ex}");
            }
        }

        private static void OnRooCategoryLoaded(string modGuid)
        {
            if (modGuid == Constants.PluginGuid && Instance != null)
            {
                Instance.StartCoroutine(DelayedUpdateRooVisibility());
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateRooVisibility()
        {
            yield return new UnityEngine.WaitForEndOfFrame();
            if (Instance != null)
            {
                Instance.UpdateHudSubTabVisibility();
                Instance.UpdateBalanceSubTabVisibility();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void RefreshFloatFieldUI(ConfigEntry<float> configEntry)
        {
            if (!RooInstalled) return;

            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.FLOAT_FIELD".Replace(" ", "_").ToUpper();

            var floatFields = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSettingsFloatField>(UnityEngine.FindObjectsSortMode.None);

            foreach (var floatField in floatFields)
            {
                if (floatField.settingToken == expectedToken)
                {
                    var newValue = configEntry.Value;

                    var valueTextField = typeof(RiskOfOptions.Components.Options.ModSettingsNumericField<float>)
                        .GetField("valueText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (valueTextField != null)
                    {
                        var inputField = valueTextField.GetValue(floatField) as TMPro.TMP_InputField;

                        if (inputField != null)
                        {
                            var formatStringField = typeof(RiskOfOptions.Components.Options.ModSettingsNumericField<float>)
                                .GetField("formatString", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                            string formatString = formatStringField?.GetValue(floatField) as string ?? "F2";

                            var separatorProperty = typeof(RiskOfOptions.Components.Options.ModSetting)
                                .GetProperty("Separator", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                            System.Globalization.CultureInfo cultureInfo;
                            if (separatorProperty != null)
                            {
                                var separator = (RiskOfOptions.Options.DecimalSeparator)separatorProperty.GetValue(null);
                                cultureInfo = separator.GetCultureInfo();
                            }
                            else
                            {
                                cultureInfo = System.Globalization.CultureInfo.InvariantCulture;
                            }

                            var formattedValue = string.Format(cultureInfo, formatString, newValue);
                            inputField.text = formattedValue;
                        }
                    }

                    break;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void RefreshStringInputFieldUI(ConfigEntry<string> configEntry)
        {
            if (!RooInstalled) return;

            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.STRING_INPUT_FIELD".Replace(" ", "_").ToUpper();

            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);

            foreach (var setting in allSettings)
            {
                if (setting.settingToken == expectedToken)
                {
                    var go = setting.gameObject;
                    if (go != null && go.activeSelf)
                    {
                        go.SetActive(false);
                        go.SetActive(true);
                    }

                    break;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void RefreshCheckBoxUI(ConfigEntry<bool> configEntry)
        {
            if (!RooInstalled) return;

            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.CHECKBOX".Replace(" ", "_").ToUpper();

            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);

            foreach (var setting in allSettings)
            {
                if (setting.settingToken == expectedToken)
                {
                    var go = setting.gameObject;
                    if (go != null && go.activeSelf)
                    {
                        go.SetActive(false);
                        go.SetActive(true);
                    }
                    return;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateHudSubTabVisibility()
        {
            if (!RooInstalled) return;

            var selectedSubTab = PluginConfig.Instance.SelectedHudElement.Value;
            UpdateSubTabVisibility(
                selectedSubTab,
                PluginConfig.HudSettingToSubTab,
                (settingToken, subTabs) => selectedSubTab == HudElementType.All || System.Array.IndexOf(subTabs, selectedSubTab) >= 0
            );
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateBalanceSubTabVisibility()
        {
            if (!RooInstalled) return;

            var selectedSubTab = PluginConfig.Instance.SelectedBalanceSubTab.Value;

            UpdateSubTabVisibility(
                selectedSubTab,
                PluginConfig.BalanceSettingToSubTab,
                (settingToken, subTabs) => selectedSubTab == BalanceSubTabType.All || System.Array.IndexOf(subTabs, selectedSubTab) >= 0
            );
        }

        private void UpdateSubTabVisibility<T>(T selectedSubTab, System.Collections.Generic.Dictionary<string, T[]> settingToSubTabMap, System.Func<string, T[], bool> shouldShowPredicate)
        {
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            int matchedCount = 0;
            int showCount = 0;
            int hideCount = 0;
            int hudSettingsCount = 0;
            int balanceSettingsCount = 0;

            foreach (var setting in allSettings)
            {
                if (!string.IsNullOrEmpty(setting.settingToken))
                {
                    bool foundInDict = settingToSubTabMap.TryGetValue(setting.settingToken, out var subTabs);

                    if (setting.settingToken.Contains(".HUD."))
                    {
                        hudSettingsCount++;
                    }
                    else if (setting.settingToken.Contains(".BALANCE."))
                    {
                        balanceSettingsCount++;
                    }

                    if (foundInDict)
                    {
                        matchedCount++;
                        bool shouldShow = shouldShowPredicate(setting.settingToken, subTabs);

                        var canvasGroup = setting.GetComponent<UnityEngine.CanvasGroup>();
                        if (canvasGroup == null)
                        {
                            canvasGroup = setting.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                        }

                        var layoutElement = setting.GetComponent<UnityEngine.UI.LayoutElement>();
                        if (layoutElement == null)
                        {
                            layoutElement = setting.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                        }

                        if (shouldShow)
                        {
                            canvasGroup.alpha = 1f;
                            canvasGroup.blocksRaycasts = true;
                            layoutElement.ignoreLayout = false;
                            showCount++;
                        }
                        else
                        {
                            canvasGroup.alpha = 0f;
                            canvasGroup.blocksRaycasts = false;
                            layoutElement.ignoreLayout = true;
                            hideCount++;
                        }
                    }
                }
            }
        }
    }
}
