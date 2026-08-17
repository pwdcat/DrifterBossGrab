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

    // ========================================================================================
    // PLUGIN UI MANAGER
    // ========================================================================================
    public partial class DrifterBossGrabPlugin
    {

        // ========================================================================================
        // RISK OF OPTIONS SETUP
        // ========================================================================================
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
            StartCoroutine(DelayedUpdateBottomlessBagVisibility());

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

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableRecoveryFeature, new CheckBoxConfig { name = "Enable Recovery Feature", category = "Recovery" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.EnemyRecoveryMode, new ChoiceConfig { name = "Enemy Recovery Mode", category = "Recovery" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.RecoverBaggedBosses, new CheckBoxConfig { name = "Recover Bosses", category = "Recovery" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.RecoverBaggedNPCs, new CheckBoxConfig { name = "Recover NPCs", category = "Recovery" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.RecoverBaggedEnvironmentObjects, new CheckBoxConfig { name = "Recover Environment Objects", category = "Recovery" }));

            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.RecoveryObjectBlacklist, new InputFieldConfig { name = "Recovery Blacklist", category = "Recovery" }));

            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableKeywordBlacklist, new InputFieldConfig { name = "Keyword Blacklist" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDebugLogs, new CheckBoxConfig { name = "Enable Debug Logs" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableConfigSync, new CheckBoxConfig { name = "Enable Config Sync" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BottomlessBagEnabled, new CheckBoxConfig { name = "Enable Bottomless Bag", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SlotScalingFormula, new InputFieldConfig { name = "Slot Scaling Formula", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableStockRefreshClamping, new CheckBoxConfig { name = "Refresh Clamping", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableSuccessiveGrabStockRefresh, new CheckBoxConfig { name = "Successive Grab Refresh", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.CycleCooldown, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Cycle Cooldown", category = "Bottomless Bag", min = 0f, max = 1f, increment = 0.01f }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PlayAnimationOnCycle, new CheckBoxConfig { name = "Play Cycle Animation", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMouseWheelScrolling, new CheckBoxConfig { name = "Mouse Wheel Scrolling", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.InverseMouseWheelScrolling, new CheckBoxConfig { name = "Invert Scrolling", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.AutoPromoteMainSeat, new CheckBoxConfig { name = "Auto-Promote Main Seat", category = "Bottomless Bag" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PrioritizeMainSeat, new CheckBoxConfig { name = "Prioritize Main Seat", category = "Bottomless Bag" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBalance, new CheckBoxConfig { name = "Enable Balance", category = "Balance" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedBalanceSubTab, new ChoiceConfig { name = "Balance Filter", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassCapacityFormula, new InputFieldConfig { name = "Mass Capacity Formula", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SlamDamageFormula, new InputFieldConfig { name = "Slam Damage Formula", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MovespeedPenaltyFormula, new InputFieldConfig { name = "Speed Penalty Formula", category = "Balance" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.StateCalculationMode, new ChoiceConfig { name = "State Calculation", category = "Balance" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.AoEDamageDistribution, new ChoiceConfig { name = "AoE Damage", category = "Balance" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.OverencumbranceMax, new FloatFieldConfig { name = "Max Overencumbrance (%)", category = "Balance" }));

            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedFlag, new ChoiceConfig { name = "Flag", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SelectedFlagMultiplier, new InputFieldConfig { name = "Multiplier", category = "Balance" }));

            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.SearchRadiusMultiplier, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Grab Range Multiplier", category = "Balance", min = 1f, max = 100f, increment = 0.1f }));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.BreakoutTimeMultiplier, new RiskOfOptions.OptionConfigs.StepSliderConfig { name = "Breakout Time Multiplier", category = "Balance" }));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.Instance.MaxSmacks, new IntSliderConfig { name = "Max Hits Before Breakout", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MaxLaunchSpeed, new InputFieldConfig { name = "Max Launch Speed", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.BagScaleCap, new InputFieldConfig { name = "Bag Visual Size Cap", category = "Balance" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassCap, new InputFieldConfig { name = "Bagged Entity Mass Cap", category = "Balance" }));

            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedHudElement, new ChoiceConfig { name = "HUD Filter", category = "Hud" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.IsHudEditorEnabled, new CheckBoxConfig { name = "Enable HUD Editor", category = "Hud" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableCarouselHUD, new CheckBoxConfig { name = "Enable Carousel HUD" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.CarouselOrientation, new ChoiceConfig { name = "Carousel Orientation" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSpacing, new FloatFieldConfig { name = "Item Spacing" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselAnimationDuration, new FloatFieldConfig { name = "Animation Duration" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableCarouselInactivityFade, new CheckBoxConfig { name = "Enable Inactivity Fade" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselInactivityFadeDelay, new FloatFieldConfig { name = "Inactivity Fade Delay (s)" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselInactivityFadeDuration, new FloatFieldConfig { name = "Inactivity Fade Duration (s)" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselInactivityFadeOpacity, new FloatFieldConfig { name = "Inactive Opacity" }));

            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotX, new FloatFieldConfig { name = "Main Slot X Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotY, new FloatFieldConfig { name = "Main Slot Y Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotScale, new FloatFieldConfig { name = "Main Slot Scale" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotOpacity, new FloatFieldConfig { name = "Main Slot Opacity" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowIcon, new CheckBoxConfig { name = "Show Icon (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowBackground, new CheckBoxConfig { name = "Show Background (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowWeightIcon, new CheckBoxConfig { name = "Show Weight Icon (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowName, new CheckBoxConfig { name = "Show Name (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowHealthBar, new CheckBoxConfig { name = "Show Health (Main)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowSlotNumber, new CheckBoxConfig { name = "Show Slot # (Main)" }));

            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotX, new FloatFieldConfig { name = "Side Slot X Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotY, new FloatFieldConfig { name = "Side Slot Y Offset" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotScale, new FloatFieldConfig { name = "Side Slot Scale" }));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotOpacity, new FloatFieldConfig { name = "Side Slot Opacity" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowIcon, new CheckBoxConfig { name = "Show Icon (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowBackground, new CheckBoxConfig { name = "Show Background (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowWeightIcon, new CheckBoxConfig { name = "Show Weight Icon (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowName, new CheckBoxConfig { name = "Show Name (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowHealthBar, new CheckBoxConfig { name = "Show Health (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowSlotNumber, new CheckBoxConfig { name = "Show Slot # (Side)" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UseNewWeightIcon, new CheckBoxConfig { name = "Use New Weight Icon" }));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.WeightDisplayMode, new ChoiceConfig { name = "Weight Display Mode" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ScaleWeightColor, new CheckBoxConfig { name = "Scale Weight Color" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ShowTotalMassOnWeightIcon, new CheckBoxConfig { name = "Show Total Mass" }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ShowOverencumberIcon, new CheckBoxConfig { name = "Show Overencumbered Icon" }));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDamagePreview, new CheckBoxConfig { name = "Enable Damage Preview" }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.DamagePreviewColor, new ColorOptionConfig { name = "Damage Preview Color" }));

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

                harmony.CreateClassProcessor(typeof(DrifterBossGrabMod.UI.RiskOfOptionsDummyPatches)).Patch();
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
                Instance.UpdateBottomlessBagVisibility();
                Instance.UpdateBalanceVisibility();
                Instance.UpdateHudVisibility();
                Instance.UpdateRecoveryVisibility();
                Instance.UpdatePersistenceVisibility();
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateBottomlessBagVisibility()
        {
            yield return new UnityEngine.WaitForEndOfFrame();
            if (Instance != null)
            {
                Instance.UpdateBottomlessBagVisibility();
            }
        }

        // ========================================================================================
        // UI REFRESH UTILITIES
        // ========================================================================================
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void RefreshStringInputFieldUI(ConfigEntry<string> configEntry)
        {
            if (!RooInstalled) return;

            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.STRING_INPUT_FIELD".Replace(" ", "_").ToUpper();

            if (configEntry == PluginConfig.Instance.SelectedFlagMultiplier)
            {
                expectedToken = $"{Constants.PluginGuid}.BALANCE.MULTIPLIER.STRING_INPUT_FIELD".Replace(" ", "_").ToUpper();
            }

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

        private bool ShouldHudSettingBeVisible(string token)
        {
            if (PluginConfig.HudSettingToSubTab.TryGetValue(token, out var subTabs))
            {
                var selectedSubTab = PluginConfig.Instance.SelectedHudElement.Value;
                return selectedSubTab == HudElementType.All || System.Array.IndexOf(subTabs, selectedSubTab) >= 0;
            }
            return true;
        }

        private bool ShouldBalanceSettingBeVisible(string token)
        {
            if (PluginConfig.BalanceSettingToSubTab.TryGetValue(token, out var subTabs))
            {
                var selectedSubTab = PluginConfig.Instance.SelectedBalanceSubTab.Value;
                return selectedSubTab == BalanceSubTabType.All || System.Array.IndexOf(subTabs, selectedSubTab) >= 0;
            }
            return true;
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
            UpdateHudVisibility();
            string filterToken = $"{Constants.PluginGuid}.HUD.HUD_FILTER.CHOICE".ToUpper();
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            foreach (var setting in allSettings)
            {
                if (setting.settingToken == filterToken)
                {
                    var callback = setting.GetComponent<OnEnableCallback>();
                    if (callback == null)
                    {
                        callback = setting.gameObject.AddComponent<OnEnableCallback>();
                        callback.Action = () =>
                        {
                            if (PluginConfig.Instance.SelectedHudElement.Value != HudElementType.All)
                            {
                                PluginConfig.Instance.SelectedHudElement.Value = HudElementType.All;
                            }
                            else
                            {
                                UpdateHudSubTabVisibility();
                            }
                        };
                    }
                    break;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateBalanceSubTabVisibility()
        {
            if (!RooInstalled) return;
            var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;
            var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
            if (flagConfig != null)
            {
                PluginConfig.Instance.SelectedFlagMultiplier.Value = flagConfig.Value;
            }

            var selectedSubTab = PluginConfig.Instance.SelectedBalanceSubTab.Value;

            UpdateSubTabVisibility(
                selectedSubTab,
                PluginConfig.BalanceSettingToSubTab,
                (settingToken, subTabs) => selectedSubTab == BalanceSubTabType.All || System.Array.IndexOf(subTabs, selectedSubTab) >= 0
            );
            UpdateBalanceVisibility();
            string filterToken = $"{Constants.PluginGuid}.BALANCE.BALANCE_FILTER.CHOICE".ToUpper();
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            foreach (var setting in allSettings)
            {
                if (setting.settingToken == filterToken)
                {
                    var callback = setting.GetComponent<OnEnableCallback>();
                    if (callback == null)
                    {
                        callback = setting.gameObject.AddComponent<OnEnableCallback>();
                        callback.Action = () =>
                        {
                            if (PluginConfig.Instance.SelectedBalanceSubTab.Value != BalanceSubTabType.All)
                            {
                                PluginConfig.Instance.SelectedBalanceSubTab.Value = BalanceSubTabType.All;
                            }
                            else
                            {
                                UpdateBalanceSubTabVisibility();
                            }
                            var currentFlag = PluginConfig.Instance.SelectedFlag.Value;
                            var currentFlagConfig = PluginConfig.GetFlagMultiplierConfig(currentFlag);
                            if (currentFlagConfig != null)
                            {
                                PluginConfig.Instance.SelectedFlagMultiplier.Value = currentFlagConfig.Value;
                            }
                            RefreshStringInputFieldUI(PluginConfig.Instance.SelectedFlagMultiplier);
                        };
                    }
                    break;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateBottomlessBagVisibility()
        {
            if (!RooInstalled) return;
            UpdateCategoryToggledVisibility("Bottomless Bag", PluginConfig.Instance.BottomlessBagEnabled.Value, "Enable Bottomless Bag");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateBalanceVisibility()
        {
            if (!RooInstalled) return;
            UpdateCategoryToggledVisibility("Balance", PluginConfig.Instance.EnableBalance.Value, "Enable Balance");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateHudVisibility()
        {
            if (!RooInstalled) return;

            UpdateCategoryToggledVisibility("Hud", PluginConfig.Instance.EnableCarouselHUD.Value, "Enable Carousel HUD",
                token => token.Contains("_SLOT_") || token.Contains("SPACING") || token.Contains("DURATION") ||
                         token.Contains("ORIENTATION") || token.Contains("INACTIVITY") || token.Contains("INACTIVE") ||
                         token.Contains("SHOW_ICON") || token.Contains("SHOW_BACKGROUND") || token.Contains("SHOW_WEIGHT") ||
                         token.Contains("SHOW_NAME") || token.Contains("SHOW_HEALTH") || token.Contains("SHOW_SLOT") ||
                         token.Contains("WEIGHT_DISPLAY") || token.Contains("NEW_WEIGHT_ICON") || token.Contains("SCALE_WEIGHT") ||
                         token.Contains("TOTAL_MASS") || token.Contains("OVERENCUMBERED_ICON"));

            UpdateCategoryToggledVisibility("Hud", PluginConfig.Instance.EnableDamagePreview.Value, "Enable Damage Preview",
                token => token.Contains("DAMAGE_PREVIEW_COLOR"));

            UpdateCategoryToggledVisibility("Hud", PluginConfig.Instance.EnableMassCapacityUI.Value, "Enable Capacity UI",
                token => token.Contains("CAPACITY_UI_") || token.Contains("ENABLE_SEPARATORS") ||
                         token.Contains("GRADIENT_INTENSITY") || token.Contains("GRADIENT_COLOR") ||
                         token.Contains("OVERENCUMBRANCE"));

            UpdateCategoryToggledVisibility("Hud", PluginConfig.Instance.EnableBaggedObjectInfo.Value, "Enable Stats Panel",
                token => token.Contains("STATS_PANEL_"));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdateRecoveryVisibility()
        {
            if (!RooInstalled) return;
            UpdateCategoryToggledVisibility("Recovery", PluginConfig.Instance.EnableRecoveryFeature.Value, "Enable Recovery Feature");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public void UpdatePersistenceVisibility()
        {
            if (!RooInstalled) return;
            UpdateCategoryToggledVisibility("Persistence", PluginConfig.Instance.EnableObjectPersistence.Value, "Enable Persistence");
        }

        private void UpdateCategoryToggledVisibility(string categoryName, bool isEnabled, string masterToggleName, System.Func<string, bool>? filterPredicate = null)
        {
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            string sanitizedCategory = categoryName.Replace(" ", "_").ToUpper();
            string sanitizedMasterKey = masterToggleName.Replace(" ", "_").ToUpper();

            foreach (var setting in allSettings)
            {
                if (string.IsNullOrEmpty(setting.settingToken)) continue;

                bool inCategory = setting.settingToken.Contains($".{sanitizedCategory}.");

                if (inCategory)
                {

                    if (setting.settingToken.Contains($".{sanitizedMasterKey}.") ||
                        setting.settingToken.EndsWith($".{sanitizedMasterKey}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (filterPredicate != null && !filterPredicate(setting.settingToken)) continue;

                    var canvasGroup = setting.GetComponent<UnityEngine.CanvasGroup>();
                    if (canvasGroup == null) canvasGroup = setting.gameObject.AddComponent<UnityEngine.CanvasGroup>();

                    var layoutElement = setting.GetComponent<UnityEngine.UI.LayoutElement>();
                    if (layoutElement == null) layoutElement = setting.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();

                    bool shouldBeVisible = true;
                    if (categoryName.Equals("Hud", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldBeVisible = ShouldHudSettingBeVisible(setting.settingToken);
                    }
                    else if (categoryName.Equals("Balance", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldBeVisible = ShouldBalanceSettingBeVisible(setting.settingToken);
                    }

                    if (!shouldBeVisible)
                    {
                        canvasGroup.alpha = 0f;
                        canvasGroup.blocksRaycasts = false;
                        layoutElement.ignoreLayout = true;
                    }
                    else
                    {
                        canvasGroup.alpha = isEnabled ? 1f : 0.3f;
                        canvasGroup.blocksRaycasts = isEnabled;
                        layoutElement.ignoreLayout = false;
                    }
                }
            }
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

    public class OnEnableCallback : UnityEngine.MonoBehaviour
    {
        public System.Action? Action;
        public void OnEnable()
        {
            Action?.Invoke();
        }
    }
}
