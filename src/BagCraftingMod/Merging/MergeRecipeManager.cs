using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod.API;
using BagCraftingMod.Support;
using BagCraftingMod.Config;

namespace BagCraftingMod.Merging
{
    public class RecipeMatch
    {
        public MergeRecipe Recipe { get; set; }
        public QualityTier Quality { get; set; }

        public RecipeMatch(MergeRecipe recipe, QualityTier quality)
        {
            Recipe = recipe;
            Quality = quality;
        }
    }

    public class MergeRecipe
    {
        public List<string> Ingredients { get; set; } = new List<string>();
        public string ResultPrefabName { get; set; } = "";
        public Action<GameObject, List<GameObject>>? CustomResultLogic { get; set; }

        public bool Matches(List<GameObject> selectedObjects)
        {
            if (selectedObjects.Count != Ingredients.Count) return false;
            
            var availableIngredients = new List<string>(Ingredients);
            foreach (var obj in selectedObjects)
            {
                string baseName = ItemQualitySupport.GetBaseName(obj.name);
                int index = availableIngredients.IndexOf(baseName);
                if (index != -1)
                {
                    availableIngredients.RemoveAt(index);
                }
                else
                {
                    return false;
                }
            }
            return availableIngredients.Count == 0;
        }
    }

    public static class MergeRecipeManager
    {
        public static List<MergeRecipe> ConfiguredRecipes = new List<MergeRecipe>();

        public static void Init()
        {
            // Load recipes from config
            foreach (var (ingredients, result) in PluginConfig.Instance.GetParsedRecipes())
            {
                ConfiguredRecipes.Add(new MergeRecipe
                {
                    Ingredients = ingredients,
                    ResultPrefabName = result
                });
            }
        }

        public static RecipeMatch? FindMatchingRecipe(List<GameObject> selectedObjects)
        {
            // 1. Check configured static recipes
            var recipe = ConfiguredRecipes.FirstOrDefault(r => r.Matches(selectedObjects));
            if (recipe != null)
            {
                // Calculate average quality
                QualityTier avgQuality = CalculateAverageQuality(selectedObjects);
                return new RecipeMatch(recipe, avgQuality);
            }
            
            return null;
        }

        private static QualityTier CalculateAverageQuality(List<GameObject> selectedObjects)
        {
            if (!ItemQualitySupport.IsInstalled) return QualityTier.None;

            int totalQuality = 0;
            foreach (var obj in selectedObjects)
            {
                totalQuality += (int)ItemQualitySupport.GetQuality(obj);
            }

            float avg = (float)totalQuality / selectedObjects.Count;
            // Round to nearest quality tier, starting from -1 (None)
            return (QualityTier)Mathf.RoundToInt(avg);
        }
        
        public static bool IsPossibleIngredient(GameObject obj, List<GameObject> currentSelection)
        {
            string baseName = ItemQualitySupport.GetBaseName(obj.name);
            
            // Check if this base name is an ingredient in any configured recipe
            return ConfiguredRecipes.Any(r => r.Ingredients.Contains(baseName));
        }
    }
}
