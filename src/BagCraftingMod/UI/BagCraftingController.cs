using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.API;
using BagCraftingMod.Merging;
using TMPro;
using BagCraftingMod;
using BagCraftingMod.Config;
using BagCraftingMod.Support;

namespace BagCraftingMod.UI
{
    public class BagCraftingController : MonoBehaviour
    {
        public static GameObject? PanelPrefab; // Assigned by Plugin

        private List<GameObject> _selectedIngredients = new List<GameObject>();
        public List<GameObject> Ingredients => _selectedIngredients;

        public RecipeMatch? BestFitRecipe { get; private set; }

        public void SelectIngredient(GameObject obj)
        {
            if (_selectedIngredients.Contains(obj))
            {
                _selectedIngredients.Remove(obj);
            }
            else
            {
                _selectedIngredients.Add(obj);
            }
            UpdateBestFit();
        }

        private void UpdateBestFit()
        {
            BestFitRecipe = MergeRecipeManager.FindMatchingRecipe(_selectedIngredients);
        }

        public bool IsValidIngredient(GameObject obj)
        {
            if (_selectedIngredients.Contains(obj)) return true;

            // Check if it's a valid ingredient from configured recipes
            string baseName = ItemQualitySupport.GetBaseName(obj.name);
            bool isRecipeIngredient = MergeRecipeManager.ConfiguredRecipes.Any(r => r.Ingredients.Contains(baseName));
            
            return isRecipeIngredient; 
        }

        public void ConfirmCraft()
        {
            if (BestFitRecipe == null) return;

            var localUser = RoR2.LocalUserManager.GetFirstLocalUser();
            var drifter = localUser?.cachedBody?.GetComponent<DrifterBagController>();

            if (drifter)
            {
                Log.Info($"Crafting {BestFitRecipe.Recipe.ResultPrefabName} (Quality: {BestFitRecipe.Quality}) on authority: {drifter.hasAuthority}");
                
                var position = drifter.transform.position;
                var rotation = drifter.transform.rotation;

                // Remove ingredients
                foreach (var ingredient in _selectedIngredients)
                {
                    DrifterBagAPI.RemoveBaggedObject(drifter, ingredient, true);
                }

                // Spawn Result
                SpawnResult(BestFitRecipe, drifter.gameObject, drifter);
            }

            _selectedIngredients.Clear();
            UpdateBestFit();
        }

        private void SpawnResult(RecipeMatch match, GameObject summoner, DrifterBagController drifter)
        {
            if (!NetworkServer.active)
            {
                Log.Warning("Cannot spawn - not on server");
                return;
            }

            string resultName = match.Recipe.ResultPrefabName;
            QualityTier quality = match.Quality;

            // Try to find the quality variant if applicable
            string finalPrefabName = ItemQualitySupport.GetQualityResultName(resultName, quality);

            // Try to find and use the appropriate Prefab
            var prefab = FindPrefabForResult(finalPrefabName);
            
            // Fallback to base prefab if quality variant not found
            if (prefab == null && finalPrefabName != resultName)
            {
                Log.Debug($"Quality variant {finalPrefabName} not found, falling back to {resultName}");
                prefab = FindPrefabForResult(resultName);
            }

            if (prefab != null)
            {
                Log.Info($"Spawning prefab directly: {finalPrefabName}");
                
                // Get position far away/below to avoid visual popping before grab
                Vector3 spawnPos = summoner.transform.position + (Vector3.down * 500f);
                Quaternion spawnRot = Quaternion.identity;

                GameObject spawnedInstance = Instantiate(prefab, spawnPos, spawnRot);
                
                // Set up custom logic and auto-grab
                if (spawnedInstance != null)
                {
                    // Apply custom result logic
                    if (match.Recipe.CustomResultLogic != null)
                    {
                        match.Recipe.CustomResultLogic(spawnedInstance, _selectedIngredients);
                    }

                    // Apply quality to result if it's an item/equipment
                    ApplyQualityToSpawnedInstance(spawnedInstance, quality);

                    // Network Spawn
                    NetworkServer.Spawn(spawnedInstance);
                    
                    // Schedule auto-grab
                    Log.Info($"Scheduling auto-grab for direct-spawned: {finalPrefabName}");
                    DrifterBagAPI.ScheduleAutoGrab(drifter, spawnedInstance, 0.0f);
                    
                    Log.Info($"Successfully spawned prefab: {finalPrefabName}");
                }
                else
                {
                    Log.Warning($"Failed to instantiate prefab: {finalPrefabName}");
                }
            }
            else
            {
                Log.Warning($"Prefab not found for result: {finalPrefabName}");
            }
        }

        private void ApplyQualityToSpawnedInstance(GameObject instance, QualityTier quality)
        {
            if (quality <= QualityTier.None || !ItemQualitySupport.IsInstalled) return;

            var pickupDisplay = instance.GetComponentInChildren<PickupDisplay>();
            if (pickupDisplay != null)
            {
                var newIndex = ItemQualitySupport.GetQualityPickupIndex(pickupDisplay.pickupState.pickupIndex, quality);
                if (newIndex != PickupIndex.none)
                {
                    pickupDisplay.SetPickup(new UniquePickup(newIndex), false);
                }
            }
        }

        public GameObject? FindPrefabForResult(string resultName)
        {
            // 1. Try AssetCache (Config Mapping)
            var cachedPrefab = AssetCache.GetPrefab(resultName);
            if (cachedPrefab != null)
            {
                return cachedPrefab;
            }

            // 2. Fallback to legacy path lookup
            var isc = LegacyResourcesAPI.Load<SpawnCard>($"SpawnCards/InteractableSpawnCard/isc{resultName}");
            if (isc != null && isc.prefab)
            {
                Log.Debug($"Found prefab via legacy SpawnCard fallback: {resultName}");
                return isc.prefab;
            }

            return null;
        }
    }
}
