using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BagCraftingMod.Config
{
    public class PluginConfig
    {
        public static PluginConfig Instance { get; private set; } = null!;

        public ConfigEntry<string> RecipesRaw { get; private set; }

        // Tooltip Configuration
        public ConfigEntry<Color> TooltipColor { get; private set; }
        public ConfigEntry<string> ObjectDisplayNamesRaw { get; private set; }

        // Icon Hue Shifting Configuration
        public ConfigEntry<bool> EnableHueShifting { get; private set; }
        public ConfigEntry<string> IconHueShiftsRaw { get; private set; }

        // Icon Asset Mapping Configuration
        public ConfigEntry<string> IconAssetPathsRaw { get; private set; }

        public ConfigEntry<bool> EnableDebugLogs { get; private set; }

        public PluginConfig(ConfigFile config)
        {
            // Merging Configuration

            RecipesRaw = config.Bind("Merging", "Recipes", 
                "Chest1,Chest1=Chest2;" +
                "Chest2,Chest2,Chest2=GoldChest;" +
                "ShrineBlood,ShrineBlood=ShrineChance;" +
                "ShrineChance,ShrineChance=ShrineCombat;" +
                "ShrineHealing,ShrineHealing=ShrineBlood;" +
                "ShrineCombat,ShrineCombat=ShrineBoss;" +
                "ShrineBlood,ShrineChance,ShrineHealing,ShrineCombat=ShrineCleanse;" +
                "ShopTerminal,ShopTerminal=DroneVendorTerminal;" +
                "DroneVendorTerminal,ShopTerminal=BlueprintTerminal;" +
                "LunarChest,LunarChest=Chest2;" +
                "Barrel1,Barrel1,Barrel1=Chest1;" +
                "Chest1,Chest1,Chest1=ShrineChance;",
                "Semi-colon separated recipes. Format: Ingredient1,Ingredient2=ResultPrefab");

            // Tooltip Configuration
            TooltipColor = config.Bind("Tooltip", "TooltipColor", Color.white, "Color of the tooltip text.");
            ObjectDisplayNamesRaw = config.Bind("Tooltip", "ObjectDisplayNames", "Chest1=Small Chest;Chest2=Large Chest;GoldChest=Gold Chest", "Semi-colon separated object name mappings. Format: ObjectName=DisplayName");

            // Icon Hue Shifting Configuration
            EnableHueShifting = config.Bind("Icons", "EnableHueShifting", true, "If true, hue shifting will be applied to icons based on configuration.");
            IconHueShiftsRaw = config.Bind("Icons", "IconHueShifts", "Chest1=#FFD700;Chest2=#00BFFF;GoldChest=#FFD700", "Semi-colon separated hue shift values. Format: ObjectName=#RRGGBB (hex color for hue tint)");

            // Icon Asset Mapping Configuration
            IconAssetPathsRaw = config.Bind("Icons", "IconAssetPaths",            "Chest1=RoR2/Base/Chest1/Chest1.prefab|RoR2/Base/ChestIcon_1.png;" +
            "Chest2=RoR2/Base/Chest2/Chest2.prefab|RoR2/Base/ChestIcon_1.png;" +
            "GoldChest=RoR2/Base/GoldChest/GoldChest.prefab|RoR2/Base/ChestIcon_1.png;" +
            "Scrapper=RoR2/Base/Scrapper/Scrapper.prefab;" +
            "TripleShop=RoR2/Base/TripleShop/TripleShop.prefab",
            "Initial mapping of item names to Addressable asset paths. Use '|' to provide multiple paths (e.g. Prefab|ExplicitIcon).");

            EnableDebugLogs = config.Bind("Debug", "EnableDebugLogs", false, "If true, verbose diagnostic logs will be enabled.");
        }

        public static void Init(ConfigFile config)
        {
            Instance = new PluginConfig(config);
        }

        public IEnumerable<(List<string> ingredients, string result)> GetParsedRecipes()
        {
            string raw = RecipesRaw.Value;
            if (string.IsNullOrWhiteSpace(raw)) return Enumerable.Empty<(List<string>, string)>();

            var recipes = new List<(List<string>, string)>();
            foreach (var recipePart in raw.Split(';'))
            {
                var sideSplit = recipePart.Split('=');
                if (sideSplit.Length != 2) continue;

                var ingredients = sideSplit[0].Split(',').Select(s => s.Trim()).ToList();
                var result = sideSplit[1].Trim();
                recipes.Add((ingredients, result));
            }
            return recipes;
        }

        public Dictionary<string, string> GetObjectDisplayNames()
        {
            var displayNames = new Dictionary<string, string>();
            string raw = ObjectDisplayNamesRaw.Value;
            if (string.IsNullOrWhiteSpace(raw)) return displayNames;

            foreach (var mapping in raw.Split(';'))
            {
                var parts = mapping.Split('=');
                if (parts.Length == 2)
                {
                    string objectName = parts[0].Trim();
                    string displayName = parts[1].Trim();
                    if (!string.IsNullOrEmpty(objectName) && !string.IsNullOrEmpty(displayName))
                    {
                        displayNames[objectName] = displayName;
                    }
                }
            }
            return displayNames;
        }

        public Dictionary<string, Color> GetIconHueShifts()
        {
            var hueShifts = new Dictionary<string, Color>();
            string raw = IconHueShiftsRaw.Value;
            if (string.IsNullOrWhiteSpace(raw)) return hueShifts;

            foreach (var mapping in raw.Split(';'))
            {
                var parts = mapping.Split('=');
                if (parts.Length == 2)
                {
                    string objectName = parts[0].Trim();
                    string colorHex = parts[1].Trim();
                    if (!string.IsNullOrEmpty(objectName) && !string.IsNullOrEmpty(colorHex))
                    {
                        if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
                        {
                            hueShifts[objectName] = color;
                        }
                    }
                }
            }
            return hueShifts;
        }

        public string GetDisplayName(string objectName)
        {
            var displayNames = GetObjectDisplayNames();
            return displayNames.TryGetValue(objectName, out string displayName) ? displayName : objectName;
        }

        public Color? GetHueShiftColor(string objectName)
        {
            if (!EnableHueShifting.Value) return null;
            var hueShifts = GetIconHueShifts();
            return hueShifts.TryGetValue(objectName, out Color color) ? color : (Color?)null;
        }

        public Dictionary<string, string> GetIconAssetPaths()
        {
            var paths = new Dictionary<string, string>();
            string raw = IconAssetPathsRaw.Value;
            if (string.IsNullOrWhiteSpace(raw)) return paths;

            foreach (var mapping in raw.Split(';'))
            {
                var parts = mapping.Split('=');
                if (parts.Length == 2)
                {
                    string objectName = parts[0].Trim();
                    string path = parts[1].Trim();
                    if (!string.IsNullOrEmpty(objectName) && !string.IsNullOrEmpty(path))
                    {
                        paths[objectName] = path;
                    }
                }
            }
            return paths;
        }
    }
}
