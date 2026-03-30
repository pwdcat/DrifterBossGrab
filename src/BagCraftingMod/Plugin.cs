using BepInEx;
using BepInEx.Bootstrap;
using RoR2;
using RoR2.Navigation;
using On.RoR2;
using UnityEngine;
using DrifterBossGrabMod.API;
using BagCraftingMod.UI;
using BagCraftingMod.Config;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using System.IO;
using BagCraftingMod.Input;
using UnityEngine.AddressableAssets;

namespace BagCraftingMod
{
    [BepInPlugin("com.pwdcat.BagCraftingMod", "BagCraftingMod", "1.0.0")]
    [BepInDependency("com.pwdcat.DrifterBossGrab")]
    [BepInDependency("com.ThinkInvisible.ItemQualities", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; } = null!;

        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            
            BagCraftingMod.Config.PluginConfig.Init(Config);
            BagCraftingMod.Merging.MergeRecipeManager.Init();
            InputSetup.Init();

            // Load UI Prefab from Addressables
            string prefabPath = "RoR2/DLC3/MealPrep/MealPrepPickerPanel.prefab";
            try
            {
                BagCraftingController.PanelPrefab = Addressables.LoadAssetAsync<GameObject>(prefabPath).WaitForCompletion();
                Log.Info($"[BagCrafting] Successfully loaded {prefabPath}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BagCrafting] Failed to load {prefabPath}: {ex.Message}");
            }

        }

        private void Update()
        {
            var localUser = RoR2.LocalUserManager.GetFirstLocalUser();
            if (localUser?.inputPlayer != null)
            {
                if (localUser.inputPlayer.GetButtonDown(RewiredActions.ToggleCrafting.ActionId))
                {
                    ToggleCraftingMenu();
                }
            }
        }

        private GameObject? _activeMenu;
        private void ToggleCraftingMenu()
        {
            if (_activeMenu)
            {
                UnityEngine.Object.Destroy(_activeMenu);
                _activeMenu = null;
                return;
            }

            var localUser = RoR2.LocalUserManager.GetFirstLocalUser();
            if (localUser?.cachedBody == null || localUser.cameraRigController == null) return;

            if (BagCraftingController.PanelPrefab == null)
            {
                Log.Warning("[BagCrafting] BagCraftingPanelPrefab is not assigned. Cannot open menu.");
                return;
            }

            _activeMenu = UnityEngine.Object.Instantiate(BagCraftingController.PanelPrefab, localUser.cameraRigController.hud.mainContainer.transform);
            _activeMenu.name = "BagCraftingMenu(Clone)"; // Explicitly name it

            // Add or find controller
            var controller = _activeMenu.GetComponent<BagCraftingController>();
            if (controller == null)
            {
                controller = _activeMenu.AddComponent<BagCraftingController>();
            }

            // The MealPrep UI might have its own panel component, we need to add ours or hook into it
            var panelController = _activeMenu.GetComponent<BagCraftingPanel>();
            if (panelController == null)
            {
                panelController = _activeMenu.AddComponent<BagCraftingPanel>();
            }
            
            panelController.Controller = controller;
            panelController.UpdateAllVisuals();
        }

        private void Start()
        {
            SetupRiskOfOptions();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void SetupRiskOfOptions()
        {
            if (!Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions")) return;

            ModSettingsManager.SetModDescription("Merge bagged chests into higher tiers with configurable recipes.", "com.pwdcat.BagCraftingMod", "BagCraftingMod");

            // Load icon
            string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Info.Location), "icon.png");
            if (System.IO.File.Exists(iconPath))
            {
                try
                {
                    byte[] array = System.IO.File.ReadAllBytes(iconPath);
                    Texture2D val = new Texture2D(256, 256);
                    ImageConversion.LoadImage(val, array);
                    ModSettingsManager.SetModIcon(Sprite.Create(val, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f)));
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[BagCrafting] Failed to load mod icon: {ex.Message}");
                }
            }


            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.RecipesRaw, new InputFieldConfig { name = "Recipes", category = "Merging" }));

            // Tooltip Configuration
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.TooltipColor, new ColorOptionConfig { name = "Tooltip Color", category = "Tooltip" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.ObjectDisplayNamesRaw, new InputFieldConfig { name = "Object Display Names", category = "Tooltip", submitOn = InputFieldConfig.SubmitEnum.OnExit }));

            // Icon Hue Shifting Configuration
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableHueShifting, new CheckBoxConfig { name = "Enable Hue Shifting", category = "Icons" }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.IconHueShiftsRaw, new InputFieldConfig { name = "Icon Hue Shifts", category = "Icons", submitOn = InputFieldConfig.SubmitEnum.OnExit }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.IconAssetPathsRaw, new InputFieldConfig { name = "Icon Asset Paths", category = "Icons", submitOn = InputFieldConfig.SubmitEnum.OnExit }));
        }

    }

    public static class Log
    {
        private static BepInEx.Logging.ManualLogSource _logSource = null!;
        public static void Init(BepInEx.Logging.ManualLogSource logSource) => _logSource = logSource;
        public static void Debug(object data) => _logSource.LogDebug(data);
        public static void Info(object data) => _logSource.LogInfo(data);
        public static void Warning(object data) => _logSource.LogWarning(data);
        public static void Error(object data) => _logSource.LogError(data);
    }
}
