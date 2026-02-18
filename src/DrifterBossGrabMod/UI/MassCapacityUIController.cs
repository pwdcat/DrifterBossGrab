using UnityEngine;
using UnityEngine.UI;
using RoR2;
using RoR2.UI;
using System.Collections;
using UnityEngine.AddressableAssets;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.UI
{
    // Controls the Capacity UI for displaying bag capacity status
    public class MassCapacityUIController : MonoBehaviour
    {
        private GameObject? _massCapacityUIInstance;
        private DrifterBagController? _bagController;
        private RectTransform? _massCapacityUIRectTransform;

        // UI Elements - these will be found on the prefab
        private HGTextMeshProUGUI? _percentageText;
        private Image? _fillBarImage;
        private Image? _overencumbranceFillImage;

        // State tracking
        private float _currentCapacity = 0f;
        private float _currentUsedCapacity = 0f;

        // Threshold for showing UI
        private const float ShowUIThreshold = 1.0f;  // Show UI when capacity is at least 1

        private void Start()
        {
            // Find the local player's DrifterBagController
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bc in bagControllers)
            {
                if (bc.hasAuthority)
                {
                    _bagController = bc;
                    break;
                }
            }

            if (_bagController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[MassCapacityUIController] No local DrifterBagController found. Disabling.");
                }
                enabled = false;
                return;
            }

            // Initialize the Capacity UI
            InitializeCapacityUI();
        }

        private void InitializeCapacityUI()
        {
            if (!PluginConfig.Instance.EnableMassCapacityUI.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[MassCapacityUIController] Capacity UI is disabled in config.");
                }
                return;
            }

            // Load the Capacity UI prefab
            LoadCapacityUIPrefab();
        }

        private void LoadCapacityUIPrefab()
        {
            StartCoroutine(LoadCapacityUIPrefabCoroutine());
        }

        private IEnumerator LoadCapacityUIPrefabCoroutine()
        {
            // Load the Junk UI prefab from Addressables (still uses the same prefab)
            var handle = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Junk UI.prefab");
            yield return handle;

            if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                var prefab = handle.Result;
                if (prefab != null)
                {
                    InstantiateCapacityUI(prefab);
                }
                else
                {
                    Log.Error("[MassCapacityUIController] Failed to load Capacity UI prefab: prefab is null");
                }
            }
            else
            {
                Log.Error($"[MassCapacityUIController] Failed to load Capacity UI prefab: {handle.Status}");
            }

            Addressables.Release(handle);
        }

        private void InstantiateCapacityUI(GameObject prefab)
        {
            // Find the HUD canvas to parent the Capacity UI
            var hudCanvas = UnityEngine.Object.FindFirstObjectByType<RoR2.UI.HUD>();
            if (hudCanvas == null)
            {
                Log.Error("[MassCapacityUIController] Failed to find HUD canvas");
                return;
            }

            Transform? targetParent = null;
            Transform mainContainer = hudCanvas.mainContainer.transform;

            if (mainContainer != null)
            {
                Transform mainUIArea = mainContainer.Find("MainUIArea");
                if (mainUIArea != null)
                {
                    Transform crosshairCanvas = mainUIArea.Find("CrosshairCanvas");
                    if (crosshairCanvas != null)
                    {
                        Transform crosshairExtras = crosshairCanvas.Find("CrosshairExtras");
                        if (crosshairExtras != null)
                        {
                            targetParent = crosshairExtras;
                        }
                        else
                        {
                            Log.Warning("[MassCapacityUIController] CrosshairExtras not found, using CrosshairCanvas as parent");
                            targetParent = crosshairCanvas;
                        }
                    }
                    else
                    {
                        Log.Warning("[MassCapacityUIController] CrosshairCanvas not found, using MainUIArea as parent");
                        targetParent = mainUIArea;
                    }
                }
                else
                {
                    Log.Warning("[MassCapacityUIController] MainUIArea not found, using MainContainer as parent");
                    targetParent = mainContainer;
                }
            }

            // Instantiate the Capacity UI
            _massCapacityUIInstance = UnityEngine.Object.Instantiate(prefab, targetParent);
            _massCapacityUIInstance.name = "CapacityUI";

            // Get the RectTransform
            _massCapacityUIRectTransform = _massCapacityUIInstance.GetComponent<RectTransform>();

            // Find UI elements on the prefab
            FindUIElements();

            // Apply initial config
            UpdateConfig();

            // Initial update
            UpdateCapacityUI();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[MassCapacityUIController] Capacity UI instantiated successfully at parent: {targetParent?.name ?? "null"}");
            }
        }

        private void FindUIElements()
        {
            if (_massCapacityUIInstance == null)
            {
                Log.Error("[MassCapacityUIController] Cannot find UI elements: instance is null");
                return;
            }

            // Find the percentage text
            _percentageText = _massCapacityUIInstance.GetComponentInChildren<HGTextMeshProUGUI>();
            if (_percentageText == null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning("[MassCapacityUIController] HGTextMeshProUGUI component not found on CapacityUI instance");
            }

            // Find the fill bar image
            // Look for Image components that might be fill bars
            var images = _massCapacityUIInstance.GetComponentsInChildren<Image>();
            foreach (var img in images)
            {
                // Check if this image has a fill type (likely a fill bar)
                if (img.type == Image.Type.Filled)
                {
                    _fillBarImage = img;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[MassCapacityUIController] Found fill bar Image: {img.name}");
                    }
                    break;
                }
            }

            if (_fillBarImage == null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning("[MassCapacityUIController] No fill bar Image found on CapacityUI instance");
            }

            // Create overencumbrance fill image dynamically
            CreateOverencumbranceFillImage();
        }

        // Creates the overencumbrance fill image as a sibling of the main fill bar image.
        // This overlays the primary fill to show overencumbrance status.
        private void CreateOverencumbranceFillImage()
        {
            if (_fillBarImage == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Warning("[MassCapacityUIController] Cannot create overencumbrance fill: primary fill bar not found");
                }
                return;
            }

            // Get the parent transform (JunkMeter) - Fill image's parent
            Transform junkMeterTransform = _fillBarImage.transform.parent;
            if (junkMeterTransform == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Warning("[MassCapacityUIController] Cannot create overencumbrance fill: Fill image has no parent");
                }
                return;
            }

            // Create a new GameObject for the overencumbrance fill
            var overencumbranceFillObj = new GameObject("OverencumbranceFillImage");

            // Parent it to JunkMeter (same as Fill image) so it's a sibling
            overencumbranceFillObj.transform.SetParent(junkMeterTransform, false);

            // Copy the local transform from Fill image to match its position and size
            overencumbranceFillObj.transform.localPosition = _fillBarImage.transform.localPosition;
            overencumbranceFillObj.transform.localRotation = _fillBarImage.transform.localRotation;
            overencumbranceFillObj.transform.localScale = _fillBarImage.transform.localScale;

            // Copy the RectTransform settings from Fill image
            RectTransform? fillRect = _fillBarImage.transform as RectTransform;
            if (fillRect != null)
            {
                RectTransform overencumbranceRect = overencumbranceFillObj.AddComponent<RectTransform>();
                overencumbranceRect.anchorMin = fillRect.anchorMin;
                overencumbranceRect.anchorMax = fillRect.anchorMax;
                overencumbranceRect.pivot = fillRect.pivot;
                overencumbranceRect.sizeDelta = fillRect.sizeDelta;
                overencumbranceRect.anchoredPosition = fillRect.anchoredPosition;
            }

            // Add Image component
            _overencumbranceFillImage = overencumbranceFillObj.AddComponent<Image>();

            // Configure the image to match the fill bar
            _overencumbranceFillImage.type = Image.Type.Filled;
            _overencumbranceFillImage.fillMethod = _fillBarImage.fillMethod;
            _overencumbranceFillImage.fillOrigin = _fillBarImage.fillOrigin;
            _overencumbranceFillImage.fillClockwise = _fillBarImage.fillClockwise;
            _overencumbranceFillImage.fillAmount = 0f;
            _overencumbranceFillImage.preserveAspect = true;
            _overencumbranceFillImage.useSpriteMesh = _fillBarImage.useSpriteMesh;
            _overencumbranceFillImage.pixelsPerUnitMultiplier = _fillBarImage.pixelsPerUnitMultiplier;

            // Set the color to blue (overencumbrance indicator)
            _overencumbranceFillImage.color = new Color(0.2f, 0.5f, 1.0f, 0.7f); // Semi-transparent blue

            // Set the sprite to match the fill bar (if it has one)
            if (_fillBarImage.sprite != null)
            {
                _overencumbranceFillImage.sprite = _fillBarImage.sprite;
            }

            // Set raycast target to false so it doesn't block UI interactions
            _overencumbranceFillImage.raycastTarget = false;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[MassCapacityUIController] Created overencumbrance fill image as sibling of Fill");
            }
        }

        // Updates the Capacity UI display based on current capacity.
        // Called via hooks when bag state changes.
        public void UpdateCapacityUI()
        {
            if (_massCapacityUIInstance == null || !PluginConfig.Instance.EnableMassCapacityUI.Value)
            {
                return;
            }

            // Update capacity values
            if (_bagController != null)
            {
                // Only use mass-based calculations if EnableBalance is enabled
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    // Get MASS capacity (not slot capacity)
                    _currentCapacity = CapacityScalingSystem.CalculateMassCapacity(_bagController);

                    // Get TOTAL MASS (not object count)
                    _currentUsedCapacity = BagCapacityCalculator.GetBaggedObjectMass(_bagController);
                }
                else
                {
                    // When balance is disabled, use slot capacity instead
                    int slotCapacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
                    _currentCapacity = slotCapacity;

                    // Get object count (not mass)
                    _currentUsedCapacity = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);
                }
            }

            // Show/hide UI based on capacity threshold
            bool shouldShow = _currentCapacity >= ShowUIThreshold;
            _massCapacityUIInstance.SetActive(shouldShow);

            if (!shouldShow)
            {
                return;
            }

            // Calculate percentage based on MASS
            float massPercentage = 0f;
            if (PluginConfig.Instance.EnableBalance.Value && _currentCapacity > 0)
            {
                massPercentage = _currentUsedCapacity / _currentCapacity;
            }

            // Calculate percentage based on SLOT COUNT
            // This ensures UI shows fill even when grabbing 0-mass objects
            float slotPercentage = 0f;
            if (_bagController != null)
            {
                int slotCapacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
                int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);

                // Check if there's an incoming object being tracked
                var incomingObject = BagPatches.GetState(_bagController).IncomingObject;
                if (incomingObject != null)
                {
                    currentCount++; // Include incoming object in count for predictive UI
                }

                if (slotCapacity > 0)
                {
                    slotPercentage = (float)currentCount / slotCapacity;
                }
            }

            // Use MAX of mass percentage and slot percentage
            // When EnableBalance is disabled, only use slot percentage
            float percentage = PluginConfig.Instance.EnableBalance.Value ? Mathf.Max(massPercentage, slotPercentage) : slotPercentage;

            // Update fill bar
            if (_fillBarImage != null)
            {
                _fillBarImage.fillAmount = Mathf.Clamp01(percentage);
            }

            // Update overencumbrance fill
            UpdateOverencumbranceFill(percentage);

            // Update percentage text
            if (_percentageText != null)
            {
                _percentageText.text = $"{Mathf.RoundToInt(percentage * 100)}%";
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[MassCapacityUIController] Updated UI: mass={_currentUsedCapacity}/{_currentCapacity} ({massPercentage:P1}), slot={slotPercentage:P1}, final={percentage:P1}");
            }
        }

        // Updates the overencumbrance fill image based on current overencumbrance status.
        // currentPercentage: The current capacity usage percentage (0.0 to 1.0+)
        private void UpdateOverencumbranceFill(float currentPercentage)
        {
            if (_overencumbranceFillImage == null)
            {
                return;
            }

            // Check if overencumbrance is enabled in config
            if (!PluginConfig.Instance.EnableOverencumbrance.Value)
            {
                _overencumbranceFillImage.gameObject.SetActive(false);
                return;
            }

            // Calculate overencumbrance fraction (0.0 to 1.0)
            // Overencumbrance starts when currentPercentage > 1.0 (over capacity)
            float overencumbranceFraction = 0f;
            if (currentPercentage > 1.0f)
            {
                // Calculate how much over capacity we are as a fraction of max overencumbrance
                // Only apply overencumbrance settings when EnableBalance is true
                float maxOverencumbrancePercent = PluginConfig.Instance.EnableBalance.Value
                    ? PluginConfig.Instance.OverencumbranceMaxPercent.Value / 100.0f
                    : 0f;
                float overencumbranceAmount = currentPercentage - 1.0f;
                overencumbranceFraction = Mathf.Clamp01(overencumbranceAmount / maxOverencumbrancePercent);
            }

            // Update fill amount
            _overencumbranceFillImage.fillAmount = overencumbranceFraction;

            // Update color based on overencumbrance severity (gradient effect)
            // Light blue (low overencumbrance) -> Dark blue (high overencumbrance)
            Color lightBlue = new Color(0.2f, 0.6f, 1.0f, 0.7f);
            Color darkBlue = new Color(0.1f, 0.3f, 0.8f, 0.9f);
            _overencumbranceFillImage.color = Color.Lerp(lightBlue, darkBlue, overencumbranceFraction);

            // Show/hide based on whether we're overencumbered
            _overencumbranceFillImage.gameObject.SetActive(overencumbranceFraction > 0f);

            if (PluginConfig.Instance.EnableDebugLogs.Value && overencumbranceFraction > 0f)
            {
                Log.Info($"[MassCapacityUIController] Overencumbrance fill: {overencumbranceFraction:P1}");
            }
        }

        // Updates the Capacity UI configuration (position, scale, visibility).
        public void UpdateConfig()
        {
            if (_massCapacityUIInstance == null) return;

            // Toggle visibility
            bool isEnabled = PluginConfig.Instance.EnableMassCapacityUI.Value;
            _massCapacityUIInstance.SetActive(isEnabled);

            if (!isEnabled) return;

            // Update position
            if (_massCapacityUIRectTransform != null)
            {
                _massCapacityUIRectTransform.anchoredPosition = new Vector2(
                    PluginConfig.Instance.MassCapacityUIPositionX.Value,
                    PluginConfig.Instance.MassCapacityUIPositionY.Value
                );
            }

            // Update scale
            float scale = PluginConfig.Instance.MassCapacityUIScale.Value;
            _massCapacityUIInstance.transform.localScale = Vector3.one * scale;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[MassCapacityUIController] Updated config: Position=({PluginConfig.Instance.MassCapacityUIPositionX.Value}, {PluginConfig.Instance.MassCapacityUIPositionY.Value}), Scale={scale}");
            }
        }

        private void OnDestroy()
        {
            // Clean up the Capacity UI instance
            if (_massCapacityUIInstance != null)
            {
                UnityEngine.Object.Destroy(_massCapacityUIInstance);
                _massCapacityUIInstance = null;
            }

            // Clear references
            _overencumbranceFillImage = null;
        }

        // Gets the current mass capacity.
        public float CurrentCapacity => _currentCapacity;

        // Gets the current used mass capacity (total mass of bagged objects).
        public float CurrentUsedCapacity => _currentUsedCapacity;
    }
}
