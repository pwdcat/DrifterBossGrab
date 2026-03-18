using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using System.Collections.ObjectModel;
using System.Linq;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.API;
using TMPro;
using UnityEngine.EventSystems;
using BagCraftingMod.Config;
using BagCraftingMod.Support;

namespace BagCraftingMod.UI
{
    public class BagCraftingPanel : MonoBehaviour
    {
        public BagCraftingController? Controller { get; set; }
        
        private RectTransform? _bagContainer;
        private RectTransform? _selectedContainer;
        private MPButton? _confirmButton;
        private PickupIcon? _resultIcon;
        private GameObject? _resultContainer;
        private GameObject? _slotPrefab;
        private MPButton? _existingButton1;
        private MPButton? _existingButton2;

        private List<GameObject> _baggedObjects = new List<GameObject>();
        private List<GameObject> _lastIngredients = new List<GameObject>();
        private int _lastBagCount = -1;
        
        private struct GroupedItem
        {
            public string Name;
            public List<GameObject> Items;
            public int SelectedCount;
            public GameObject Representative => Items.FirstOrDefault();
        }

        private List<GroupedItem> _bagGroups = new List<GroupedItem>();
        private List<GroupedItem> _selectedGroups = new List<GroupedItem>();

        private UIElementAllocator<MPButton>? _bagAllocator;
        private UIElementAllocator<MPButton>? _selectedAllocator;

        private void Awake()
        {
            var pp = GetComponent<RoR2.UI.PickupPickerPanel>();
            if (pp) pp.enabled = false;
            var cp = GetComponent<RoR2.UI.CraftingPanel>();
            if (cp) cp.enabled = false;
            
            foreach (var mono in GetComponents<MonoBehaviour>())
            {
                if (mono == this) continue;
                string typeName = mono.GetType().Name;
                if (typeName.Contains("MealPrep") || typeName.Contains("Chef"))
                {
                    mono.enabled = false;
                }
            }
            gameObject.name = "BagCraftingMenu(Clone)";
            Log.Info("BagCraftingPanel.Awake()");

            // 1. Find the key containers using discovery
            var bgContainer = FindChildRecursive(transform, "BGContainer");
            if (bgContainer)
            {
                Log.Info("Found BGContainer, searching for children...");
                
                // Crafting Container
                var craftingContainer = FindChildRecursive(bgContainer, "CraftingContainer");
                if (craftingContainer)
                {
                    Log.Info("Found CraftingContainer.");
                    _selectedContainer = FindChildRecursive(craftingContainer, "Container") as RectTransform;
                    
                    if (_selectedContainer)
                    {
                        var oldGlg = _selectedContainer.GetComponent<GridLayoutGroup>();
                        if (oldGlg) UnityEngine.Object.DestroyImmediate(oldGlg);

                        var oldHlg = _selectedContainer.GetComponent<HorizontalLayoutGroup>();
                        if (oldHlg) UnityEngine.Object.DestroyImmediate(oldHlg);

                        var glg = _selectedContainer.gameObject.AddComponent<GridLayoutGroup>();
                        if (glg)
                        {
                            glg.cellSize = new Vector2(96, 96);
                            glg.spacing = new Vector2(10, 10);
                            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
                            glg.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                            glg.constraintCount = 1;
                            glg.childAlignment = TextAnchor.MiddleCenter;
                            Log.Info("Configured GridLayoutGroup for Selection.");
                        }
                    }

                    var ingredientsParent = FindChildRecursive(craftingContainer, "Ingredients");
                    if (ingredientsParent)
                    {
                        ingredientsParent.gameObject.SetActive(true);
                        
                        // Add RectMask2D to constrain width only
                        var ingredientsRect = ingredientsParent as RectTransform;
                        if (ingredientsRect)
                        {
                            var container = _selectedContainer;
                            if (container == null) return;

                            Log.Info($"Container - Before ScrollRect/RectMask2D: anchorMin={container.anchorMin}, anchorMax={container.anchorMax}, sizeDelta={container.sizeDelta}, rect={container.rect}");
                             
                            var scrollRect = container.gameObject.GetComponent<ScrollRect>();
                            if (!scrollRect)
                            {
                                scrollRect = container.gameObject.AddComponent<ScrollRect>();
                            }
                            
                            if (scrollRect)
                            {
                                scrollRect.horizontal = true;
                                scrollRect.vertical = false;
                                scrollRect.scrollSensitivity = 15f;
                                scrollRect.inertia = true;
                                scrollRect.decelerationRate = 0.135f;
                                scrollRect.elasticity = 0.1f;
                                scrollRect.content = container;
                                
                                Log.Info($"ScrollRect configured on Container: horizontal={scrollRect.horizontal}, vertical={scrollRect.vertical}, scrollSensitivity={scrollRect.scrollSensitivity}");
                            }
                            
                            var rectMask = container.gameObject.GetComponent<RectMask2D>();
                            if (!rectMask)
                            {
                                rectMask = container.gameObject.AddComponent<RectMask2D>();
                            }
                            
                            if (rectMask)
                            {
                                rectMask.padding = new Vector4(0, 0, 0, 0);
                                rectMask.softness = new Vector2Int(0, 0);
                                
                                Log.Info($"RectMask2D configured on Container: padding={rectMask.padding}, softness={rectMask.softness}");
                            }
                             
                            Log.Info($"Container - After ScrollRect/RectMask2D: anchorMin={container.anchorMin}, anchorMax={container.anchorMax}, sizeDelta={container.sizeDelta}, rect={container.rect}");
                        }
                    }

                    _confirmButton = FindChildRecursive(craftingContainer, "Confirm Button")?.GetComponent<MPButton>();
                    _resultIcon = FindChildRecursive(craftingContainer, "DisplayIcon")?.GetComponent<PickupIcon>();
                    _resultContainer = FindChildRecursive(craftingContainer, "Result")?.gameObject;
                    
                    if (_resultIcon != null)
                    {
                        var timerTransform = _resultIcon.transform.Find("Timer");
                        if (timerTransform != null)
                        {
                            timerTransform.gameObject.SetActive(false);
                        }
                    }
                    
                    _slotPrefab = FindChildRecursive(_selectedContainer!, "PickupButtonTemplate (1)")?.gameObject;
                    if (!_slotPrefab) _slotPrefab = FindChildRecursive(_selectedContainer!, "PickupButtonTemplate")?.gameObject;
                    
                    if (_slotPrefab)
                    {
                         _slotPrefab.SetActive(false);
                         // Hide the "Ingredient Container" placeholder image that often sits behind slots
                         var placeholderIcon = FindChildRecursive(_selectedContainer!, "Ingredient Container");
                         if (placeholderIcon) placeholderIcon.gameObject.SetActive(false);

                         // Find existing pickup button templates from prefab
                         _existingButton1 = FindChildRecursive(_selectedContainer!, "PickupButtonTemplate")?.GetComponent<MPButton>();
                         _existingButton2 = FindChildRecursive(_selectedContainer!, "PickupButtonTemplate (1)")?.GetComponent<MPButton>();
                    }
                }

                // Inventory Container
                var inventoryContainer = FindChildRecursive(bgContainer, "InventoryContainer");
                if (inventoryContainer)
                {
                    Log.Info("Found InventoryContainer.");
                    inventoryContainer.gameObject.SetActive(true); // Ensure it's active
                    
                    var iconContainer = FindChildRecursive(inventoryContainer, "IconContainer") as RectTransform;
                    if (iconContainer)
                    {
                        Log.Info("Found IconContainer.");
                        _bagContainer = iconContainer;
                        
                        // Fallback prefab if not found in crafting
                        if (!_slotPrefab)
                        {
                            _slotPrefab = FindChildRecursive(_bagContainer, "PickupButtonTemplate")?.gameObject;
                        }

                        // Clear vanilla items (Meals from previous cooks)
                        foreach (Transform child in _bagContainer)
                        {
                            if (child.gameObject != _slotPrefab) child.gameObject.SetActive(false);
                        }

                        // Re-configure grid for inventory
                        var glg = _bagContainer.GetComponent<GridLayoutGroup>();
                        if (glg)
                        {
                            glg.cellSize = new Vector2(96, 96);
                            glg.spacing = new Vector2(10, 10);
                            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
                            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
                            glg.childAlignment = TextAnchor.UpperLeft;
                            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                            glg.constraintCount = 6;
                        }
                    }
                }
                
                // Cancel Button
                var cancelBtn = FindChildRecursive(bgContainer, "CancelButton")?.GetComponent<MPButton>();
                if (cancelBtn)
                {
                    cancelBtn.gameObject.SetActive(true);
                    cancelBtn.onClick.AddListener(() => UnityEngine.Object.Destroy(gameObject));
                }
            }
            else
            {
                Log.Warning("BGContainer not found! UI will be empty.");
                Log.Info("Full hierarchy dump for debugging:");
                DumpHierarchy(transform, "");
            }

            // Labels
            var labelContainer = FindChildRecursive(transform, "LabelContainer");
            if (labelContainer)
            {
                Log.Info("Found LabelContainer.");
                var mainLabel = FindChildRecursive(labelContainer, "Label")?.GetComponent<TMP_Text>();
                if (mainLabel) mainLabel.text = "Bag Crafting";

                // Hide Chef icons
                var chefIcon1 = FindChildRecursive(labelContainer, "Image (1)");
                if (chefIcon1) chefIcon1.gameObject.SetActive(false);
                var chefIcon2 = FindChildRecursive(labelContainer, "Image");
                if (chefIcon2) chefIcon2.gameObject.SetActive(false);
            }

            if (_bagContainer && _slotPrefab)
            {
                Log.Info($"Initializing _bagAllocator on {_bagContainer.name} with prefab {_slotPrefab.name}");
                _bagAllocator = new UIElementAllocator<MPButton>(_bagContainer, _slotPrefab, true, false);
                _bagAllocator.onCreateElement = OnCreateBagButton;
            }
            else
            {
                Log.Warning($"_bagContainer ({_bagContainer != null}) or _slotPrefab ({_slotPrefab != null}) is null.");
            }

            if (_selectedContainer && _slotPrefab)
            {
                Log.Info($"Initializing _selectedAllocator on {_selectedContainer.name} with prefab {_slotPrefab.name}");
                _selectedAllocator = new UIElementAllocator<MPButton>(_selectedContainer, _slotPrefab, true, false);
                _selectedAllocator.onCreateElement = OnCreateSelectedButton;
            }

            if (_confirmButton)
            {
                _confirmButton.onClick.RemoveAllListeners();
                _confirmButton.onClick.AddListener(() => {
                    Controller?.ConfirmCraft();
                    UnityEngine.Object.Destroy(gameObject);
                });
            }
            
        }

        private void OnCreateBagButton(int index, MPButton button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => {
                var controller = Controller;
                if (controller == null) return;

                var ingredients = controller.Ingredients;
                if (ingredients == null) return;

                if (index < _bagGroups.Count)
                {
                    var group = _bagGroups[index];
                    if (group.Items == null) return;

                    // Prefer adding an unselected one
                    var unselected = group.Items.FirstOrDefault(o => !ingredients.Contains(o));
                    if (unselected)
                    {
                        controller.SelectIngredient(unselected);
                    }
                    else
                    {
                        // Toggle last one if all selected
                        var last = group.Items.LastOrDefault();
                        if (last) controller.SelectIngredient(last);
                    }
                    UpdateAllVisuals();
                }
            });
        }

        private void OnCreateSelectedButton(int index, MPButton button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => {
                if (Controller != null && index < _selectedGroups.Count)
                {
                    var group = _selectedGroups[index];
                    var last = group.Items.LastOrDefault();
                    if (last)
                    {
                        Controller.SelectIngredient(last);
                        UpdateAllVisuals();
                    }
                }
            });
        }

        public void UpdateAllVisuals(bool force = false)
        {
            if (Controller == null) return;

            var localUser = RoR2.LocalUserManager.GetFirstLocalUser();
            var body = localUser?.cachedBody;
            if (!body) return;

            var drifter = body.GetComponent<DrifterBagController>();
            if (drifter == null) return;

            // Simple Dirty Check
            var currentIngredients = Controller.Ingredients;
            bool ingredientsChanged = !currentIngredients.SequenceEqual(_lastIngredients);
            
            List<GameObject> currentBag;
            try {
                currentBag = DrifterBagAPI.GetBaggedObjects(drifter).Where(o => o != null).ToList();
            } catch {
                currentBag = new List<GameObject>();
            }

            bool bagChanged = currentBag.Count != _lastBagCount || !currentBag.SequenceEqual(_baggedObjects);

            if (!force && !ingredientsChanged && !bagChanged) return;

            _lastIngredients = new List<GameObject>(currentIngredients);
            _baggedObjects = currentBag;
            _lastBagCount = _baggedObjects.Count;

            Log.Info($"UpdateAllVisuals: Updating UI (Bag: {_baggedObjects.Count}, Selected: {currentIngredients.Count})");

            // 1. Group items for UI
            _bagGroups = _baggedObjects
                .GroupBy(o => DrifterBagAPI.GetObjectName(o))
                .Select(g => new GroupedItem { 
                    Name = g.Key, 
                    Items = g.ToList(),
                    SelectedCount = g.Count(o => Controller.Ingredients.Contains(o))
                })
                .ToList();

            _selectedGroups = currentIngredients
                .GroupBy(o => DrifterBagAPI.GetObjectName(o))
                .Select(g => new GroupedItem {
                    Name = g.Key,
                    Items = g.ToList(),
                    SelectedCount = g.Count()
                })
                .ToList();

            // 1. Update Bag List
            if (_bagAllocator != null)
            {
                _bagAllocator.AllocateElements(_bagGroups.Count);
                for (int i = 0; i < _bagGroups.Count; i++)
                {
                    var group = _bagGroups[i];
                    // Display count is (Total - Selected) - how many are still available in bag
                    SetupButton(_bagAllocator.elements[i], group.Representative, group.SelectedCount > 0, group.Items.Count - group.SelectedCount);
                }
            }

            // 2. Update Selected List
            if (_selectedAllocator != null)
            {
                int displayCount = Mathf.Max(2, _selectedGroups.Count);
                _selectedAllocator.AllocateElements(displayCount);

                for (int i = 0; i < displayCount; i++)
                {
                    if (i < _selectedGroups.Count)
                    {
                        var group = _selectedGroups[i];
                        SetupButton(_selectedAllocator.elements[i], group.Representative, true, group.SelectedCount);
                    }
                    else
                    {
                        SetupButton(_selectedAllocator.elements[i], null, false);
                    }
                }
            }

            // 3. Update Result & Button
            bool hasRecipe = Controller.BestFitRecipe != null;
            if (_confirmButton) _confirmButton.interactable = hasRecipe;

            if (_resultIcon)
            {
                _resultIcon.gameObject.SetActive(hasRecipe);

                if (hasRecipe && Controller.BestFitRecipe != null)
                {
                    string resultName = Controller.BestFitRecipe.Recipe.ResultPrefabName;
                    QualityTier quality = Controller.BestFitRecipe.Quality;

                    var iconImage = _resultIcon.GetComponent<Image>();
                    if (!iconImage) iconImage = _resultIcon.transform.Find("Icon")?.GetComponent<Image>();

                    // Handle non-pickup results (chests, etc.)
                    var sprite = GetIconForResult(resultName);
                    if (sprite)
                    {
                        // Update the main image component (PickupIcon uses RawImage)
                        if (_resultIcon.image)
                        {
                            _resultIcon.image.texture = sprite.texture;
                            _resultIcon.image.enabled = true;
                        }
                        
                        if (iconImage)
                        {
                            iconImage.sprite = sprite;
                            iconImage.enabled = true;
                        }
                        
                        _resultIcon.SetPickupIndex(PickupIndex.none, 1, 0f);
                    }

                    
                    // Apply hue shift to result icon components
                    if (_resultIcon.image)
                    {
                        ApplyHueShift(_resultIcon.image, resultName);
                    }
                    if (iconImage)
                    {
                        ApplyHueShift(iconImage, resultName);
                        iconImage.color = ItemQualitySupport.GetQualityColor(quality);
                    }
                }
            }

            // Force Layout Refresh
            if (_bagContainer) LayoutRebuilder.ForceRebuildLayoutImmediate(_bagContainer);
            if (_selectedContainer) LayoutRebuilder.ForceRebuildLayoutImmediate(_selectedContainer);
            
            if (_bagContainer && _bagAllocator != null)
            {
                var ourElements = _bagAllocator.elements.Cast<MPButton>().Select(e => e.transform).ToList();
                foreach(Transform child in _bagContainer)
                {
                    if (!ourElements.Contains(child) && child.gameObject != _slotPrefab?.transform)
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }

            if (_selectedContainer && _selectedAllocator != null)
            {
                var ourElements = _selectedAllocator.elements.Cast<MPButton>().Select(e => e.transform).ToList();
                foreach(Transform child in _selectedContainer)
                {
                    if (!ourElements.Contains(child) && child.gameObject != _slotPrefab)
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }
        }

        private void SetupButton(MPButton button, GameObject? obj, bool isSelected, int quantity = 1)
        {
            button.gameObject.SetActive(true);
            
            // Explicit size to prevent squishing in horizontal layout
            var rt = button.GetComponent<RectTransform>();
            if (rt) rt.sizeDelta = new Vector2(96, 96);

            var childLocator = button.GetComponent<ChildLocator>();
            var iconImage = childLocator?.FindChild("Icon")?.GetComponent<Image>();
            
            // Fallback for icon image if ChildLocator fails
            if (!iconImage) iconImage = button.transform.Find("Icon")?.GetComponent<Image>();
            
            // Quantity Text (TextMeshProUGUI as per user request)
            var quantityText = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (quantityText)
            {
                bool showQuantity = quantity > 1;
                quantityText.text = showQuantity ? quantity.ToString() : "";
                quantityText.enabled = showQuantity;
                quantityText.gameObject.SetActive(showQuantity);
            }

            // Setup tooltip provider
            SetupTooltipProvider(button, obj);
            
            if (obj == null)
            {
                if (iconImage) iconImage.enabled = false;
                button.interactable = false;
                // Tint to look like an empty slot background
                button.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.6f);
                return;
            }

            if (iconImage)
            {
                var sprite = GetIconForObject(obj);
                if (sprite)
                {
                    iconImage.sprite = sprite;
                    iconImage.enabled = true;
                    
                    // Apply hue shifting if configured
                    ApplyHueShift(iconImage, obj);
                }
                else
                {
                    Log.Debug("No sprite found for object.");
                    iconImage.enabled = false;
                }
            }
            else
            {
                 var allImages = button.GetComponentsInChildren<Image>(true);
                 Log.Warning($"iconImage not found on button! Total images in children: {allImages.Length}");
                 foreach(var img in allImages) Log.Debug($" - Image: {img.name}, gameobject: {img.gameObject.name}");
            }

            var quality = ItemQualitySupport.GetQuality(obj);
            var qualityColor = ItemQualitySupport.GetQualityColor(quality);

            button.GetComponent<Image>().color = isSelected ? Color.green : qualityColor;
            button.interactable = Controller?.IsValidIngredient(obj) ?? false;
            // If it's already selected, it should be interactable (to deselect)
            if (isSelected) button.interactable = true;
            
            if (!button.interactable && !isSelected) button.GetComponent<Image>().color = Color.gray;
        }
        
        private void SetupTooltipProvider(MPButton button, GameObject? obj)
        {
            // Get or add TooltipProvider component
            var tooltipProvider = button.GetComponent<TooltipProvider>();
            if (!tooltipProvider)
            {
                tooltipProvider = button.gameObject.AddComponent<TooltipProvider>();
            }
            
            // Set tooltip content based on the object
            if (obj != null)
            {
                string objectName = obj.name.Replace("(Clone)", "").Trim();
                
                // Get display name from config (renaming)
                string displayName = PluginConfig.Instance.GetDisplayName(objectName);
                
                tooltipProvider.overrideTitleText = displayName;
                tooltipProvider.titleColor = PluginConfig.Instance.TooltipColor.Value;
                tooltipProvider.overrideBodyText = ""; // Empty body - only show name
            }
            else
            {
                tooltipProvider.overrideTitleText = "";
                tooltipProvider.overrideBodyText = "";
                tooltipProvider.titleColor = Color.clear;
            }
        }
        
        private void ApplyHueShift(Graphic iconGraphic, GameObject obj)
        {
            if (obj == null || iconGraphic == null) return;
            string objectName = obj.name.Replace("(Clone)", "").Trim();
            ApplyHueShift(iconGraphic, objectName);
        }

        private void ApplyHueShift(Graphic iconGraphic, string objectName)
        {
            if (iconGraphic == null || string.IsNullOrEmpty(objectName)) return;
            
            Color? hueShiftColor = PluginConfig.Instance.GetHueShiftColor(objectName);
            
            if (hueShiftColor.HasValue)
            {
                iconGraphic.color = hueShiftColor.Value;
            }
            else
            {
                iconGraphic.color = Color.white;
            }
        }


        private Sprite? GetIconForObject(GameObject obj)
        {
            var texture = DrifterBagAPI.GetObjectIcon(obj);
            if (texture) return TextureToSprite(texture);

            // Fallback to Mystery Icon
            var mystery = Addressables.LoadAssetAsync<Texture>("RoR2/Base/Common/MiscIcons/texMysteryIcon.png").WaitForCompletion();
            return TextureToSprite(mystery);
        }

        private Sprite? GetIconForResult(string resultName)
        {
            Log.Info($"GetIconForResult: {resultName}");
            // 1. Try AssetCache (Config Mapping or previously cached)
            var cachedIcon = AssetCache.GetIcon(resultName);
            if (cachedIcon != null) 
            {
                Log.Info($"Found icon in AssetCache for: {resultName}");
                return cachedIcon;
            }

            // 2. Use the Controller's verified prefab discovery
            if (Controller != null)
            {
                Log.Info($"Attempting prefab discovery for icon: {resultName}");
                var prefab = Controller.FindPrefabForResult(resultName);
                if (prefab != null)
                {
                    Log.Info($"Found prefab: {prefab.name}");
                    // Check if prefab has SpecialObjectAttributes with an icon
                    var soa = prefab.GetComponent<SpecialObjectAttributes>();
                    if (soa && soa.portraitIcon) 
                    {
                        Log.Info($"Found portraitIcon on prefab SOA: {soa.portraitIcon.name}");
                        var sprite = TextureToSprite(soa.portraitIcon);
                        if (sprite)
                        {
                            AssetCache.CacheIcon(resultName, sprite);
                            return sprite;
                        }
                    }
                    
                    // Fallback
                    string lowerName = prefab.name.ToLowerInvariant();
                    string iconPath = DrifterBossGrabMod.Patches.GrabbableObjectPatches.GetIconPathForObject(lowerName);
                    Log.Info($"Fallback icon mapping for {lowerName}: {iconPath}");
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        try
                        {
                            var texture = Addressables.LoadAssetAsync<Texture>(iconPath).WaitForCompletion();
                            if (texture)
                            {
                                Log.Info($"Successfully loaded fallback icon from Addressables: {iconPath}");
                                var sprite = TextureToSprite(texture);
                                if (sprite)
                                {
                                    AssetCache.CacheIcon(resultName, sprite);
                                    return sprite;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to load Addressable icon {iconPath}: {ex.Message}");
                        }
                    }
                }
            }

            Log.Warning($"No icon found for {resultName}, using mystery icon.");
            // Final fallback
            return GetIconForObject(null!); 
        }



        private Sprite? TextureToSprite(Texture? tex)
        {
            if (!tex) return null;
            if (tex is Texture2D tex2d)
                return Sprite.Create(tex2d, new Rect(0, 0, tex2d.width, tex2d.height), new Vector2(0.5f, 0.5f));
            return null;
        }

        private void DumpHierarchy(Transform? t, string indent)
        {
            if (!t) return;
            Log.Info($"{indent}{t.name} (Active: {t.gameObject.activeSelf})");
            for (int i = 0; i < t.childCount; i++)
            {
                DumpHierarchy(t.GetChild(i), indent + "  ");
            }
        }

        private Transform? FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}

