#nullable enable
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

    public class MassCapacityUIController : MonoBehaviour, IConfigObserver
    {
        private void OnEnable()
        {
            ConfigChangeNotifier.AddObserver(this);
        }

        private void OnDisable()
        {
            ConfigChangeNotifier.RemoveObserver(this);
        }

        public void OnConfigChanged(string key, object value)
        {
            UpdateConfig();
            UpdateCapacityUI();
        }
        private GameObject? _massCapacityUIInstance;
        private DrifterBagController? _bagController;
        private RectTransform? _massCapacityUIRectTransform;

        private HGTextMeshProUGUI? _percentageText;
        private Image? _fillBarImage;
        private Image? _overencumbranceFillImage;
        private CapacityUIGradient? _gradientEffect;
        private OverencumbranceUIGradient? _overencumbranceGradientEffect;

        private GameObject? _separatorTemplate;
        private List<GameObject> _separatorObjects = new List<GameObject>();

        private float _currentCapacity = 0f;
        private float _currentUsedCapacity = 0f;

        private const float ShowUIThreshold = 1.0f;

        private void Start()
        {

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
                enabled = false;
                return;
            }

            InitializeCapacityUI();
        }

        private void InitializeCapacityUI()
        {
            if (_massCapacityUIInstance != null) return;

            LoadCapacityUIPrefab();
        }

        private void LoadCapacityUIPrefab()
        {
            StartCoroutine(LoadCapacityUIPrefabCoroutine());
        }

        private IEnumerator LoadCapacityUIPrefabCoroutine()
        {

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
                    Log.Error("[CapacityUI] Failed to load Capacity UI prefab: prefab is null");
                }
            }
            else
            {
                Log.Error($"[CapacityUI] Failed to load Capacity UI prefab: {handle.Status}");
            }

            Addressables.Release(handle);
        }

        private void InstantiateCapacityUI(GameObject prefab)
        {

            var hudCanvas = UnityEngine.Object.FindFirstObjectByType<RoR2.UI.HUD>();
            if (hudCanvas == null)
            {
                Log.Error("[CapacityUI] Failed to find HUD canvas");
                return;
            }

            Transform? targetParent = null;
            Transform mainContainer = hudCanvas.mainContainer.transform;

            if (mainContainer != null)
            {
                Transform mainRect = mainContainer.Find("MainRect");
                if (mainRect != null)
                {
                    Transform bottomCenter = mainRect.Find("BottomCenterCluster");
                    targetParent = bottomCenter ?? mainRect;
                }
                else
                {
                    targetParent = mainContainer;
                }
            }

            _massCapacityUIInstance = UnityEngine.Object.Instantiate(prefab, targetParent);
            _massCapacityUIInstance.name = "CapacityUI";

            _massCapacityUIRectTransform = _massCapacityUIInstance.GetComponent<RectTransform>();

            FindUIElements();

            UpdateConfig();

            UpdateCapacityUI();

            if (_massCapacityUIInstance != null)
            {
                var draggable = _massCapacityUIInstance.AddComponent<UI.HudDraggable>();
                draggable.ElementType = HudElementType.CapacityUI;
                draggable.DragSizePadding = new Vector2(-215, -150);
                draggable.DragOffset = new Vector2(-140, 0);
                draggable.XConfig = PluginConfig.Instance.MassCapacityUIPositionX;
                draggable.YConfig = PluginConfig.Instance.MassCapacityUIPositionY;
                draggable.ScaleConfig = PluginConfig.Instance.MassCapacityUIScale;
            }
        }

        private void FindUIElements()
        {
            if (_massCapacityUIInstance == null)
            {
                Log.Error("[CapacityUI] Cannot find UI elements: instance is null");
                return;
            }

            _percentageText = _massCapacityUIInstance.GetComponentInChildren<HGTextMeshProUGUI>();

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
                _fillBarImage.color = PluginConfig.Instance.CapacityGradientColorMid.Value;
                _gradientEffect = _fillBarImage.gameObject.AddComponent<CapacityUIGradient>();

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

            CreateOverencumbranceFillImage();
        }

        private void CreateOverencumbranceFillImage()
        {
            if (_fillBarImage == null)
            {
                return;
            }

            Transform junkMeterTransform = _fillBarImage.transform.parent;
            if (junkMeterTransform == null)
            {
                return;
            }

            var overencumbranceFillObj = new GameObject("OverencumbranceFillImage");

            overencumbranceFillObj.transform.SetParent(junkMeterTransform, false);

            overencumbranceFillObj.transform.localPosition = _fillBarImage.transform.localPosition;
            overencumbranceFillObj.transform.localRotation = _fillBarImage.transform.localRotation;
            overencumbranceFillObj.transform.localScale = _fillBarImage.transform.localScale;

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

            _overencumbranceFillImage = overencumbranceFillObj.AddComponent<Image>();

            _overencumbranceFillImage.type = Image.Type.Filled;
            _overencumbranceFillImage.fillMethod = _fillBarImage.fillMethod;
            _overencumbranceFillImage.fillOrigin = _fillBarImage.fillOrigin;
            _overencumbranceFillImage.fillClockwise = _fillBarImage.fillClockwise;
            _overencumbranceFillImage.fillAmount = 0f;
            _overencumbranceFillImage.preserveAspect = true;
            _overencumbranceFillImage.useSpriteMesh = _fillBarImage.useSpriteMesh;
            _overencumbranceFillImage.pixelsPerUnitMultiplier = _fillBarImage.pixelsPerUnitMultiplier;

            _overencumbranceFillImage.color = PluginConfig.Instance.OverencumbranceGradientColorMid.Value;

            if (_fillBarImage.sprite != null)
            {
                _overencumbranceFillImage.sprite = _fillBarImage.sprite;
            }

            _overencumbranceFillImage.raycastTarget = false;

            _overencumbranceGradientEffect = overencumbranceFillObj.AddComponent<OverencumbranceUIGradient>();
        }

        public void UpdateCapacityUI()
        {
            if (_massCapacityUIInstance == null || !PluginConfig.Instance.EnableMassCapacityUI.Value)
            {
                return;
            }

            if (_bagController != null)
            {

                if (PluginConfig.Instance.EnableBalance.Value)
                {

                    _currentCapacity = CapacityScalingSystem.CalculateMassCapacity(_bagController);

                    _currentUsedCapacity = BagCapacityCalculator.GetBaggedObjectMass(_bagController);
                }
                else
                {

                    int slotCapacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
                    _currentCapacity = slotCapacity;

                    _currentUsedCapacity = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);
                }
            }

            bool shouldShow = HudEditorManager.IsEditorActive || _currentCapacity >= ShowUIThreshold;
            _massCapacityUIInstance.SetActive(shouldShow);

            if (!shouldShow)
            {
                return;
            }

            float massPercentage = 0f;
            if (PluginConfig.Instance.EnableBalance.Value && _currentCapacity > 0)
            {
                massPercentage = _currentUsedCapacity / _currentCapacity;
            }

            float slotPercentage = 0f;
            if (_bagController != null)
            {
                int slotCapacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
                int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);

                var incomingObject = BagPatches.GetState(_bagController).IncomingObject;
                if (incomingObject != null)
                {
                    currentCount++;
                }

                if (slotCapacity > 0)
                {
                    slotPercentage = (float)currentCount / slotCapacity;
                }
            }

            float percentage = PluginConfig.Instance.EnableBalance.Value ? Mathf.Max(massPercentage, slotPercentage) : slotPercentage;

            if (_fillBarImage != null)
            {
                _fillBarImage.fillAmount = Mathf.Clamp01(percentage);
            }

            UpdateOverencumbranceFill(percentage);

            if (_percentageText != null)
            {
                _percentageText.text = $"{Mathf.RoundToInt(percentage * 100)}%";
            }

            UpdateGradient(percentage);
            UpdateSeparators(percentage);
        }

        private void UpdateOverencumbranceFill(float currentPercentage)
        {
            if (_overencumbranceFillImage == null)
            {
                return;
            }

            if (PluginConfig.Instance.OverencumbranceMax.Value <= 0)
            {
                _overencumbranceFillImage.gameObject.SetActive(false);
                return;
            }

            float overencumbranceFraction = 0f;
            if (currentPercentage > 1.0f)
            {

                float maxOverencumbrancePercent = PluginConfig.Instance.EnableBalance.Value
                    ? PluginConfig.Instance.OverencumbranceMax.Value / 100.0f
                    : 0f;
                float overencumbranceAmount = currentPercentage - 1.0f;
                overencumbranceFraction = Mathf.Clamp01(overencumbranceAmount / maxOverencumbrancePercent);
            }

            _overencumbranceFillImage.fillAmount = overencumbranceFraction;

            _overencumbranceFillImage.gameObject.SetActive(overencumbranceFraction > 0f);
        }

        public void UpdateConfig()
        {
            bool shouldExist = HudEditorManager.IsEditorActive || PluginConfig.Instance.EnableMassCapacityUI.Value;

            if (_massCapacityUIInstance == null)
            {
                if (shouldExist)
                {
                    InitializeCapacityUI();
                }
                return;
            }

            _massCapacityUIInstance.SetActive(shouldExist);
            if (!shouldExist) return;

            if (_massCapacityUIRectTransform != null)
            {
                _massCapacityUIRectTransform.anchoredPosition = new Vector2(
                    PluginConfig.Instance.MassCapacityUIPositionX.Value,
                    PluginConfig.Instance.MassCapacityUIPositionY.Value
                );
            }

            float scale = PluginConfig.Instance.MassCapacityUIScale.Value;
            _massCapacityUIInstance.transform.localScale = Vector3.one * scale;
        }

        private void OnDestroy()
        {

            if (_massCapacityUIInstance != null)
            {
                UnityEngine.Object.Destroy(_massCapacityUIInstance);
                _massCapacityUIInstance = null;
            }

            _overencumbranceFillImage = null;
        }

        public float CurrentCapacity => _currentCapacity;

        public float CurrentUsedCapacity => _currentUsedCapacity;

        private void UpdateGradient(float percentage)
        {
            if (_fillBarImage != null)
            {
                _fillBarImage.color = PluginConfig.Instance.CapacityGradientColorMid.Value;
            }

            if (_overencumbranceFillImage != null)
            {
                _overencumbranceFillImage.color = PluginConfig.Instance.OverencumbranceGradientColorMid.Value;
            }

            if (_gradientEffect != null && _fillBarImage != null)
            {
                _gradientEffect.Intensity = PluginConfig.Instance.GradientIntensity.Value;
                _fillBarImage.SetVerticesDirty();
            }

            if (_overencumbranceGradientEffect != null && _overencumbranceFillImage != null)
            {
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

            if (PluginConfig.Instance.EnableBalance.Value)
            {
                float cumulativeMass = 0f;

                if (_bagController != null)
                {
                    int capacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
                    bool uncap = PluginConfig.Instance.IsAddedCapacityInfinite && PluginConfig.Instance.BottomlessBagEnabled.Value;

                    int k = 1;

                    var list = BagPatches.GetState(_bagController).BaggedObjects;
                    if (list != null)
                    {
                        var countedInstanceIds = new HashSet<int>();
                        foreach (var obj in list)
                        {
                            if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                            {
                                int instanceId = obj.GetInstanceID();
                                if (!countedInstanceIds.Contains(instanceId))
                                {
                                    countedInstanceIds.Add(instanceId);
                                    var objState = BaggedObjectPatches.LoadObjectState(_bagController, obj);
                                    float mass = objState != null ? objState.baggedMass : _bagController.CalculateBaggedObjectMass(obj);
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
                    if (incomingObject != null && !ProjectileRecoveryPatches.IsInProjectileState(incomingObject))
                    {
                        var incomingState = BaggedObjectPatches.LoadObjectState(_bagController, incomingObject);
                        float mass = incomingState != null ? incomingState.baggedMass : _bagController.CalculateBaggedObjectMass(incomingObject);
                        cumulativeMass += mass;

                        float frac = cumulativeMass / _currentCapacity;
                        if (!uncap && capacity > 0)
                        {
                            frac = Mathf.Max((float)k / capacity, frac);
                        }
                        separatorFractions.Add(frac);
                        k++;
                    }

                    if (!uncap && capacity > 1)
                    {
                        int displayCapacity = Mathf.Min(capacity, 15);
                        int maxPads = displayCapacity;
                        for (int i = k; i < maxPads; i++)
                        {

                            float slotFrac = (float)i / displayCapacity;

                            float currentMassFrac = cumulativeMass / _currentCapacity;
                            if (slotFrac > currentMassFrac)
                            {
                                separatorFractions.Add(slotFrac);
                            }
                        }
                    }
                }

                if (PluginConfig.Instance.OverencumbranceMax.Value > 0)
                {
                    float maxOverencumbrancePercent = PluginConfig.Instance.OverencumbranceMax.Value / 100.0f;
                    if (cumulativeMass > _currentCapacity)
                    {
                        List<float> remappedFractions = new List<float>();
                        foreach (float originalFrac in separatorFractions)
                        {

                            if (originalFrac > 1.0f)
                            {

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

                int capacity = _bagController != null ? BagCapacityCalculator.GetUtilityMaxStock(_bagController) : 3;

                if (capacity > 1)
                {
                    int displayCapacity = Mathf.Min(capacity, 15);
                    int maxSegments = displayCapacity;
                    for (int i = 1; i < maxSegments; i++)
                    {
                        separatorFractions.Add((float)i / displayCapacity);
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

                            float centerX = -37.76f;
                            float centerY = 51.92f;

                            float radius = 21.53f;

                            float startAngle = 226.44f;
                            float totalSweep = 98.88f;

                            float curAngle = startAngle - (frac * totalSweep);

                            float rad = curAngle * Mathf.Deg2Rad;
                            float posX = centerX + (Mathf.Cos(rad) * radius);
                            float posY = centerY + (Mathf.Sin(rad) * radius);

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
        public float Intensity = 1f;

        public override void ModifyMesh(UnityEngine.UI.VertexHelper vh)
        {
            if (!IsActive()) return;

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

                Color gradientColor = targetColor;
                gradientColor.a *= vertex.color.a;
                vertex.color = Color.Lerp(vertex.color, gradientColor, Intensity);
                vertices[i] = vertex;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }

    public class OverencumbranceUIGradient : UnityEngine.UI.BaseMeshEffect
    {
        public float Intensity = 1f;

        public override void ModifyMesh(UnityEngine.UI.VertexHelper vh)
        {
            if (!IsActive()) return;

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

                Color gradientColor = targetColor;
                gradientColor.a *= vertex.color.a;
                vertex.color = Color.Lerp(vertex.color, gradientColor, Intensity);
                vertices[i] = vertex;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }
}
