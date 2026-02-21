using UnityEngine;
using UnityEngine.UI;
using RoR2;
using RoR2.UI;
using System.Collections;
using System.Collections.Generic;
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
        private CapacityUIGradient? _gradientEffect;
        private OverencumbranceUIGradient? _overencumbranceGradientEffect;

        // Separator logic
        private GameObject? _separatorTemplate;
        private List<GameObject> _separatorObjects = new List<GameObject>();

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
            // Load the Junk UI prefab from Addressables
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
            
            // Find the fill bar image
            var images = _massCapacityUIInstance.GetComponentsInChildren<Image>();
            foreach (var img in images)
            {
                if (img.type == Image.Type.Filled && img.fillMethod == Image.FillMethod.Horizontal)
                {
                    _fillBarImage = img;
                    break;
                }
            }

            if (_fillBarImage != null)
            {
                _gradientEffect = _fillBarImage.gameObject.AddComponent<CapacityUIGradient>();
                
                // Grab one of the original Thresholds and save it as an exact blueprint clone, delete the rest
                Transform junkMeterTransform = _fillBarImage.transform.parent;
                if (junkMeterTransform != null)
                {
                    for (int i = junkMeterTransform.childCount - 1; i >= 0; i--)
                    {
                        Transform child = junkMeterTransform.GetChild(i);
                        if (child.name.StartsWith("Threshold"))
                        {
                            if (_separatorTemplate == null)
                            {
                                child.gameObject.name = "SeparatorTemplate";
                                child.gameObject.SetActive(false);
                                _separatorTemplate = child.gameObject;
                            }
                            else
                            {
                                UnityEngine.Object.Destroy(child.gameObject);
                            }
                        }
                    }
                }
            }

            // Create overencumbrance fill image dynamically
            CreateOverencumbranceFillImage();
        }

        // Creates the overencumbrance fill image as a sibling of the main fill bar image
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

            // Set the color to white so the vertex gradient handles the colors pure without muddying
            _overencumbranceFillImage.color = Color.white;

            // Set the sprite to match the fill bar
            if (_fillBarImage.sprite != null)
            {
                _overencumbranceFillImage.sprite = _fillBarImage.sprite;
            }

            // Set raycast target to false so it doesn't block UI interactions
            _overencumbranceFillImage.raycastTarget = false;

            // Add the new blue gradient effect
            _overencumbranceGradientEffect = overencumbranceFillObj.AddComponent<OverencumbranceUIGradient>();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[MassCapacityUIController] Created overencumbrance fill image as sibling of Fill");
            }
        }

        // Updates the Capacity UI display based on current capacity
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

            // Calculate percentage based on slot count
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

            UpdateGradient(percentage);
            UpdateSeparators(percentage);

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

            // Show/hide based on whether we're overencumbered
            _overencumbranceFillImage.gameObject.SetActive(overencumbranceFraction > 0f);

            if (PluginConfig.Instance.EnableDebugLogs.Value && overencumbranceFraction > 0f)
            {
                Log.Info($"[MassCapacityUIController] Overencumbrance fill: {overencumbranceFraction:P1}");
            }
        }

        // Updates the Capacity UI configuration
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

        private void UpdateGradient(float percentage)
        {
            if (_gradientEffect != null && _fillBarImage != null)
            {
                _gradientEffect.Enabled = PluginConfig.Instance.EnableGradient.Value;
                _gradientEffect.Intensity = PluginConfig.Instance.GradientIntensity.Value;
                _fillBarImage.SetVerticesDirty();
            }

            if (_overencumbranceGradientEffect != null && _overencumbranceFillImage != null)
            {
                _overencumbranceGradientEffect.Enabled = PluginConfig.Instance.EnableGradient.Value;
                _overencumbranceGradientEffect.Intensity = PluginConfig.Instance.GradientIntensity.Value;
                _overencumbranceFillImage.SetVerticesDirty();
            }
        }

        private void CreateSeparator()
        {
            if (_fillBarImage == null || _separatorTemplate == null) return;

            GameObject sepObj = UnityEngine.Object.Instantiate(_separatorTemplate, _separatorTemplate.transform.parent);
            sepObj.name = $"Separator_{_separatorObjects.Count}";
            _separatorObjects.Add(sepObj);
        }

        private void UpdateSeparators(float percentage)
        {
            if (_massCapacityUIInstance == null) return;

            bool enableSeparators = PluginConfig.Instance.EnableSeparators.Value;
            if (!enableSeparators || _currentCapacity <= 0)
            {
                foreach (var bg in _separatorObjects) bg.SetActive(false);
                return;
            }

            List<float> separatorFractions = new List<float>();

            // Hybrid Mode Logic
            if (PluginConfig.Instance.EnableBalance.Value)
            {
                float cumulativeMass = 0f;
                // Add separators for objects currently in the bag based on mass
                if (_bagController != null)
                {
                    int capacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
                    bool uncap = PluginConfig.Instance.UncapCapacity.Value;

                    int k = 1;

                    var list = BagPatches.GetState(_bagController).BaggedObjects;
                    if (list != null)
                    {
                        var countedInstanceIds = new HashSet<int>();
                        foreach (var obj in list)
                        {
                            if (obj != null && !OtherPatches.IsInProjectileState(obj))
                            {
                                int instanceId = obj.GetInstanceID();
                                if (!countedInstanceIds.Contains(instanceId))
                                {
                                    countedInstanceIds.Add(instanceId);
                                    float mass = _bagController.CalculateBaggedObjectMass(obj);
                                    cumulativeMass += mass;
                                    
                                    float frac = cumulativeMass / _currentCapacity;
                                    if (!uncap && capacity > 0)
                                    {
                                        frac = Mathf.Max((float)k / capacity, frac);
                                    }
                                    separatorFractions.Add(frac);
                                    k++;
                                }
                            }
                        }
                    }

                    var incomingObject = BagPatches.GetState(_bagController).IncomingObject;
                    if (incomingObject != null && !OtherPatches.IsInProjectileState(incomingObject))
                    {
                        float mass = _bagController.CalculateBaggedObjectMass(incomingObject);
                        cumulativeMass += mass;
                        
                        float frac = cumulativeMass / _currentCapacity;
                        if (!uncap && capacity > 0)
                        {
                            frac = Mathf.Max((float)k / capacity, frac);
                        }
                        separatorFractions.Add(frac);
                        k++;
                    }

                    // If Capacity is capped, we want to pad the remaining space with static slot separators
                    if (!uncap && capacity > 1)
                    {
                        for (int i = k; i < capacity; i++)
                        {
                            // Static slot location
                            float slotFrac = (float)i / capacity;
                            
                            // Only draw this static slot pip if we haven't already filled past it with mass
                            float currentMassFrac = cumulativeMass / _currentCapacity;
                            if (slotFrac > currentMassFrac)
                            {
                                separatorFractions.Add(slotFrac);
                            }
                        }
                    }
                }

                // --- Overencumbrance Separator Remapping ---
                if (PluginConfig.Instance.EnableOverencumbrance.Value)
                {
                    float maxOverencumbrancePercent = PluginConfig.Instance.OverencumbranceMaxPercent.Value / 100.0f;
                    if (maxOverencumbrancePercent > 0 && cumulativeMass > _currentCapacity)
                    {
                        List<float> remappedFractions = new List<float>();
                        foreach (float originalFrac in separatorFractions)
                        {
                            // If this separator is for an object that pushed us into overencumbrance
                            if (originalFrac > 1.0f)
                            {
                                // Subtract 100% base capacity, and map the remainder to the Overencumbrance bar's bounds
                                float overAmount = originalFrac - 1.0f;
                                float newFrac = Mathf.Clamp01(overAmount / maxOverencumbrancePercent);
                                remappedFractions.Add(newFrac);
                            }
                        }
                        separatorFractions = remappedFractions;
                    }
                }
            }
            else
            {
                // Retrieve the raw utility slot capacity to map the fractions evenly
                int capacity = _bagController != null ? BagCapacityCalculator.GetUtilityMaxStock(_bagController) : 3;
                
                if (capacity > 1) 
                {
                    for (int i = 1; i < capacity; i++)
                    {
                        separatorFractions.Add((float)i / capacity); 
                    }
                }
            }

            while (_separatorObjects.Count < separatorFractions.Count)
            {
                CreateSeparator();
            }

            for (int i = 0; i < _separatorObjects.Count; i++)
            {
                if (i < separatorFractions.Count)
                {
                    float frac = separatorFractions[i];
                    if (frac > 0.01f && frac < 0.99f)
                    {
                        _separatorObjects[i].SetActive(true);
                        var rect = _separatorObjects[i].GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            // Threshold 1 (66.6%): (pos: -58.06, 59.10, rot: 251.7)
                            // Threshold 2 (16%):   (pos: -56.26, 40.90, rot: 122.2)
                            // Threshold 3 (33.3%): (pos: -58.70, 46.90, rot: 284.0)
                            
                            // Exact Circle Center
                            float centerX = -37.76f;
                            float centerY = 51.92f;
                            
                            // Exact Radius
                            float radius = 21.53f;

                            // 0% translates to ~226.44 degrees
                            // 100% translates to ~127.56 degrees
                            // Total sweep = 98.88 degrees
                            float startAngle = 226.44f;
                            float totalSweep = 98.88f;

                            // Interpolate the angle along the circle
                            float curAngle = startAngle - (frac * totalSweep);
                            
                            // Calculate exact position on the circle
                            float rad = curAngle * Mathf.Deg2Rad;
                            float posX = centerX + (Mathf.Cos(rad) * radius);
                            float posY = centerY + (Mathf.Sin(rad) * radius);
                            
                            // The rotation tangent to the circle points away the center
                            float rotZ = curAngle + 90.5f;

                            rect.localPosition = new Vector3(posX, posY, 0f);
                            rect.localEulerAngles = new Vector3(0, 0, rotZ);
                        }
                    }
                    else
                    {
                        _separatorObjects[i].SetActive(false);
                    }
                }
                else
                {
                    _separatorObjects[i].SetActive(false);
                }
            }
        }
    }

    public class CapacityUIGradient : UnityEngine.UI.BaseMeshEffect
    {
        public bool Enabled = true;
        public float Intensity = 1f;

        public override void ModifyMesh(UnityEngine.UI.VertexHelper vh)
        {
            if (!IsActive() || !Enabled) return;

            List<UIVertex> vertices = new List<UIVertex>();
            vh.GetUIVertexStream(vertices);

            if (vertices.Count == 0) return;

            Rect rect = graphic.rectTransform.rect;
            float minX = rect.xMin;
            float width = rect.width;

            Color colorStart = PluginConfig.Instance.CapacityGradientColorStart.Value;
            Color colorMid = PluginConfig.Instance.CapacityGradientColorMid.Value;
            Color colorEnd = PluginConfig.Instance.CapacityGradientColorEnd.Value;

            for (int i = 0; i < vertices.Count; i++)
            {
                UIVertex vertex = vertices[i];
                float normalizedX = width > 0 ? (vertex.position.x - minX) / width : 0f;
                normalizedX = Mathf.Clamp01(normalizedX);

                Color targetColor;
                if (normalizedX <= 0.5f)
                    targetColor = Color.Lerp(colorEnd, colorMid, normalizedX * 2f);
                else
                    targetColor = Color.Lerp(colorMid, colorStart, (normalizedX - 0.5f) * 2f);

                vertex.color = Color.Lerp(vertex.color, vertex.color * targetColor, Intensity);
                vertices[i] = vertex;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }

    public class OverencumbranceUIGradient : UnityEngine.UI.BaseMeshEffect
    {
        public bool Enabled = true;
        public float Intensity = 1f;

        public override void ModifyMesh(UnityEngine.UI.VertexHelper vh)
        {
            if (!IsActive() || !Enabled) return;

            List<UIVertex> vertices = new List<UIVertex>();
            vh.GetUIVertexStream(vertices);

            if (vertices.Count == 0) return;

            Rect rect = graphic.rectTransform.rect;
            float minX = rect.xMin;
            float width = rect.width;

            Color colorStart = PluginConfig.Instance.OverencumbranceGradientColorStart.Value;
            Color colorMid = PluginConfig.Instance.OverencumbranceGradientColorMid.Value;
            Color colorEnd = PluginConfig.Instance.OverencumbranceGradientColorEnd.Value;

            for (int i = 0; i < vertices.Count; i++)
            {
                UIVertex vertex = vertices[i];
                float normalizedX = width > 0 ? (vertex.position.x - minX) / width : 0f;
                normalizedX = Mathf.Clamp01(normalizedX);

                Color targetColor;
                if (normalizedX <= 0.5f)
                    targetColor = Color.Lerp(colorEnd, colorMid, normalizedX * 2f);
                else
                    targetColor = Color.Lerp(colorMid, colorStart, (normalizedX - 0.5f) * 2f);

                vertex.color = Color.Lerp(vertex.color, vertex.color * targetColor, Intensity);
                vertices[i] = vertex;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }
}
