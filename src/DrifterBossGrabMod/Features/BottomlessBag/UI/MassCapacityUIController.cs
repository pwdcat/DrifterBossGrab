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
            _hasPresentation = false;
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
        private float _targetFill;
        private float _targetOverFill;
        private float _displayFill;
        private float _displayOverFill;
        private float _fillVelocity;
        private float _overFillVelocity;
        private float _accentAge = 1f;
        private float _limitAccentAge = 1f;
        private float _overEntryAccentAge = 1f;
        private bool _wasOverencumbered;
        private bool _wasAtMaxOverencumbrance;
        private bool _wasAtLimit;
        private float _accentStart;
        private float _overAccentStart;
        private int _lastPassengerCount;
        private float _lastCommittedFill;
        private float _lastCommittedOverFill;
        private bool _hasPresentation;
        private bool _balancePresentation;
        private bool _editorPreview;

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

        internal static Transform GetCapacityParent(Transform mainContainer)
        {
            var mainRect = mainContainer.Find("MainRect");
            return mainRect != null ? mainRect.Find("BottomCenterCluster") ?? mainRect : mainContainer;
        }

        internal void InitializeEditorPreview(GameObject meter)
        {
            enabled = false;
            _editorPreview = true;
            _massCapacityUIInstance = meter;
            _massCapacityUIRectTransform = meter.GetComponent<RectTransform>();
            FindUIElements();
        }

        internal void UpdateEditorPreview(float load, int slots)
        {
            _currentCapacity = Mathf.Max(1, slots);
            float previousFill = _targetFill;
            float previousOver = _targetOverFill;
            _targetFill = Mathf.Clamp01(load);
            _targetOverFill = Mathf.Clamp01(load - 1f);
            _balancePresentation = PluginConfig.Instance.EnableBalance.Value;
            if (_targetFill > previousFill || _targetOverFill > previousOver)
            {
                _accentStart = previousFill;
                _overAccentStart = previousOver;
                _accentAge = 0f;
                if (previousFill < 1f && _targetFill >= 1f) _limitAccentAge = 0f;
                if ((previousOver <= 0f && _targetOverFill > 0f) || (previousOver < 1f && _targetOverFill >= 1f))
                    _overEntryAccentAge = 0f;
            }
            if (_percentageText != null)
            {
                _percentageText.text = load > 1f ? $"+{Mathf.RoundToInt((load - 1f) * 100f)}%" : $"{Mathf.RoundToInt(load * 100f)}%";
                _percentageText.color = load > 1f ? new Color(1f, 0.72f, 0.36f) : Color.white;
            }
            UpdateConfig();
            UpdateGradient(load);
            if (_separatorTemplate != null) UpdateSeparators(load);
            ApplyPresentation(Time.unscaledDeltaTime);
        }

        private void InstantiateCapacityUI(GameObject prefab)
        {

            var hudCanvas = UnityEngine.Object.FindFirstObjectByType<RoR2.UI.HUD>();
            if (hudCanvas == null)
            {
                Log.Error("[CapacityUI] Failed to find HUD canvas");
                return;
            }

            Transform targetParent = GetCapacityParent(hudCanvas.mainContainer.transform);

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
                _gradientEffect.ReverseFill = _fillBarImage.fillOrigin == 1;

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
            _overencumbranceFillImage.preserveAspect = _fillBarImage.preserveAspect;
            _overencumbranceFillImage.useSpriteMesh = _fillBarImage.useSpriteMesh;
            _overencumbranceFillImage.pixelsPerUnitMultiplier = _fillBarImage.pixelsPerUnitMultiplier;

            _overencumbranceFillImage.color = PluginConfig.Instance.OverencumbranceGradientColorMid.Value;

            if (_fillBarImage.sprite != null)
            {
                _overencumbranceFillImage.sprite = _fillBarImage.sprite;
            }

            _overencumbranceFillImage.raycastTarget = false;

            _overencumbranceGradientEffect = overencumbranceFillObj.AddComponent<OverencumbranceUIGradient>();
            _overencumbranceGradientEffect.ReverseFill = _overencumbranceFillImage.fillOrigin == 1;
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

            bool unlimitedSlots = BagCapacityCalculator.GetUtilityMaxStock(_bagController) == int.MaxValue;
            bool unlimitedMass = !PluginConfig.Instance.EnableBalance.Value ||
                _currentCapacity >= float.MaxValue || float.IsPositiveInfinity(_currentCapacity);
            bool shouldShow = HudEditorManager.IsEditorActive ||
                (!(unlimitedSlots && unlimitedMass) && _currentCapacity >= ShowUIThreshold);
            _massCapacityUIInstance.SetActive(shouldShow);

            if (!shouldShow)
            {
                _hasPresentation = false;
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

            _targetFill = Mathf.Clamp01(percentage);
            float overPercent = _bagController != null
                ? OverencumbranceSystem.CalculateOverencumbrancePercent(_bagController.baggedMass, _currentCapacity)
                : 0f;
            float maxOver = PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.OverencumbranceMax.Value / 100f : 0f;
            _targetOverFill = maxOver > 0f ? Mathf.Clamp01(overPercent / maxOver) : 0f;
            int passengerCount = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);
            bool balance = PluginConfig.Instance.EnableBalance.Value;
            int slotLimit = BagCapacityCalculator.GetUtilityMaxStock(_bagController);
            float maxMass = balance ? _currentCapacity * (1f + Mathf.Max(0f, maxOver)) : float.PositiveInfinity;
            bool atMaxOverencumbrance = balance && maxOver > 0f && maxMass > 0f &&
                !float.IsInfinity(maxMass) && _currentUsedCapacity >= maxMass;
            bool atLimit = (slotLimit > 0 && passengerCount >= slotLimit) ||
                (balance && maxMass > 0f && !float.IsInfinity(maxMass) && _currentUsedCapacity >= maxMass);
            bool initializing = !_hasPresentation || _balancePresentation != balance;
            if (initializing)
            {
                _displayFill = _targetFill;
                _displayOverFill = _targetOverFill;
                _fillVelocity = _overFillVelocity = 0f;
                _accentAge = 1f;
                _limitAccentAge = 1f;
                _overEntryAccentAge = 1f;
                _hasPresentation = true;
            }
            else if (passengerCount > _lastPassengerCount &&
                (_targetFill > _lastCommittedFill || _targetOverFill > _lastCommittedOverFill))
            {
                _accentStart = _lastCommittedFill;
                _overAccentStart = _lastCommittedOverFill;
                _accentAge = 0f;
            }
            if (!initializing && passengerCount > _lastPassengerCount && atLimit && !_wasAtLimit)
                _limitAccentAge = 0f;
            if (!initializing && ((overPercent > 0f && !_wasOverencumbered) ||
                (passengerCount > _lastPassengerCount && atMaxOverencumbrance && !_wasAtMaxOverencumbrance)))
                _overEntryAccentAge = 0f;
            _wasAtMaxOverencumbrance = atMaxOverencumbrance;
            _wasOverencumbered = overPercent > 0f;
            _wasAtLimit = atLimit;
            float committedSlots = (float)passengerCount / Mathf.Max(1, BagCapacityCalculator.GetUtilityMaxStock(_bagController));
            _lastCommittedFill = Mathf.Clamp01(balance ? Mathf.Max(massPercentage, committedSlots) : committedSlots);
            _lastCommittedOverFill = _targetOverFill;
            _lastPassengerCount = passengerCount;
            _balancePresentation = balance;
            if (_percentageText != null)
            {
                _percentageText.text = overPercent > 0f ? $"+{Mathf.RoundToInt(overPercent * 100f)}%" : $"{Mathf.RoundToInt(percentage * 100f)}%";
                _percentageText.color = overPercent > 0f ? new Color(1f, 0.72f, 0.36f) : Color.white;
            }
            UpdateGradient(percentage);
            UpdateSeparators(percentage);
            ApplyPresentation(0f);
        }

        private void LateUpdate()
        {
            if (_massCapacityUIInstance == null || !_massCapacityUIInstance.activeInHierarchy || !_hasPresentation) return;
            ApplyPresentation(Time.unscaledDeltaTime);
        }

        private static Vector3 GetMeterPoint(float fraction)
        {
            float angle = (226.44f - Mathf.Clamp01(fraction) * 98.88f) * Mathf.Deg2Rad;
            return new Vector3(-37.76f + Mathf.Cos(angle) * 21.53f, 51.92f + Mathf.Sin(angle) * 21.53f, 0f);
        }

        private float GetVisualFill(float fraction)
        {
            if (_fillBarImage == null || fraction <= 0f || fraction >= 1f) return Mathf.Clamp01(fraction);
            var sprite = _fillBarImage.overrideSprite != null ? _fillBarImage.overrideSprite : _fillBarImage.sprite;
            var rect = _fillBarImage.GetPixelAdjustedRect();
            if (sprite != null && _fillBarImage.preserveAspect && sprite.rect.width > 0f && sprite.rect.height > 0f && rect.height > 0f)
            {
                float spriteAspect = sprite.rect.width / sprite.rect.height;
                float rectAspect = rect.width / rect.height;
                if (spriteAspect > rectAspect)
                {
                    float height = rect.height;
                    rect.height = rect.width / spriteAspect;
                    rect.y += (height - rect.height) * _fillBarImage.rectTransform.pivot.y;
                }
                else
                {
                    float width = rect.width;
                    rect.width = rect.height * spriteAspect;
                    rect.x += (width - rect.width) * _fillBarImage.rectTransform.pivot.x;
                }
            }
            float minX = rect.xMin;
            float maxX = rect.xMax;
            if (sprite != null && sprite.rect.width > 0f)
            {
                var padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
                minX += rect.width * padding.x / sprite.rect.width;
                maxX -= rect.width * padding.z / sprite.rect.width;
            }
            Vector3 point = _fillBarImage.rectTransform.InverseTransformPoint(
                _fillBarImage.transform.parent.TransformPoint(GetMeterPoint(fraction)));
            float amount = Mathf.InverseLerp(minX, maxX, point.x);
            amount = _fillBarImage.fillOrigin == 1 ? 1f - amount : amount;
            float endBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.95f, 1f, fraction));
            return Mathf.Lerp(amount, 1f, endBlend);
        }

        private static float GetSettleStrength(float age)
        {
            age *= 0.32f / PluginConfig.Instance.CapacityFlashDuration.Value;
            if (age < 0.025f) return Mathf.SmoothStep(0f, 1f, age / 0.025f);
            if (age < 0.09f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 0.09f) / 0.23f));
        }

        private void ApplyPresentation(float deltaTime)
        {
            if (!PluginConfig.Instance.SmoothCapacityFill.Value)
            {
                _displayFill = _targetFill;
                _displayOverFill = _targetOverFill;
                _fillVelocity = 0f;
                _overFillVelocity = 0f;
            }
            if (deltaTime > 0f)
            {
                if (PluginConfig.Instance.SmoothCapacityFill.Value)
                {
                    _displayFill = Mathf.SmoothDamp(_displayFill, _targetFill, ref _fillVelocity, PluginConfig.Instance.CapacityFillSmoothTime.Value, Mathf.Infinity, deltaTime);
                    _displayOverFill = Mathf.SmoothDamp(_displayOverFill, _targetOverFill, ref _overFillVelocity, PluginConfig.Instance.CapacityFillSmoothTime.Value, Mathf.Infinity, deltaTime);
                }
                _accentAge += deltaTime;
                _limitAccentAge += deltaTime;
                _overEntryAccentAge += deltaTime;
            }
            float fillTolerance = _targetFill >= 1f ? 0.005f : 0.001f;
            float overTolerance = _targetOverFill >= 1f ? 0.005f : 0.001f;
            if (Mathf.Abs(_displayFill - _targetFill) < fillTolerance)
            {
                _displayFill = _targetFill;
                _fillVelocity = 0f;
            }
            if (Mathf.Abs(_displayOverFill - _targetOverFill) < overTolerance)
            {
                _displayOverFill = _targetOverFill;
                _overFillVelocity = 0f;
            }
            float flashStrength = PluginConfig.Instance.EnableCapacityFlashes.Value && (!HudEditorManager.IsEditorActive || _editorPreview) ? PluginConfig.Instance.CapacityFlashStrength.Value : 0f;
            float accent = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_accentAge / 0.22f));
            float fullSettle = GetSettleStrength(_limitAccentAge);
            float overSettle = GetSettleStrength(_overEntryAccentAge);
            if (_fillBarImage != null)
            {
                _fillBarImage.fillAmount = GetVisualFill(_displayFill);
                if (_gradientEffect != null)
                {
                    float strength = accent * 0.45f * flashStrength;
                    float settle = _targetFill >= 1f ? fullSettle * 0.85f * flashStrength : 0f;
                    float accentStart = GetVisualFill(_accentStart);
                    float accentEnd = GetVisualFill(_targetFill);
                    if (_gradientEffect.AccentStart != accentStart || _gradientEffect.AccentEnd != accentEnd ||
                        _gradientEffect.AccentStrength != strength || _gradientEffect.SettleStrength != settle)
                    {
                        _gradientEffect.AccentStart = accentStart;
                        _gradientEffect.AccentEnd = accentEnd;
                        _gradientEffect.AccentStrength = strength;
                        _gradientEffect.SettleStrength = settle;
                        _fillBarImage.SetVerticesDirty();
                    }
                }
            }
            if (_overencumbranceFillImage != null)
            {
                _overencumbranceFillImage.fillAmount = GetVisualFill(_displayOverFill);
                _overencumbranceFillImage.gameObject.SetActive(_targetOverFill > 0f || _displayOverFill > 0f);
                if (_overencumbranceGradientEffect != null)
                {
                    float strength = accent * 0.45f * flashStrength;
                    float settle = _targetOverFill > 0f ? Mathf.Max(fullSettle, overSettle) * 0.85f * flashStrength : 0f;
                    float accentStart = GetVisualFill(_overAccentStart);
                    float accentEnd = GetVisualFill(_targetOverFill);
                    if (_overencumbranceGradientEffect.AccentStart != accentStart || _overencumbranceGradientEffect.AccentEnd != accentEnd ||
                        _overencumbranceGradientEffect.AccentStrength != strength || _overencumbranceGradientEffect.SettleStrength != settle)
                    {
                        _overencumbranceGradientEffect.AccentStart = accentStart;
                        _overencumbranceGradientEffect.AccentEnd = accentEnd;
                        _overencumbranceGradientEffect.AccentStrength = strength;
                        _overencumbranceGradientEffect.SettleStrength = settle;
                        _overencumbranceFillImage.SetVerticesDirty();
                    }
                }
            }
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
            if (!shouldExist)
            {
                _hasPresentation = false;
                return;
            }

            if (_massCapacityUIRectTransform != null)
            {
                var draggable = _massCapacityUIInstance != null ? _massCapacityUIInstance.GetComponent<UI.HudDraggable>() : null;
                if (draggable == null || !draggable.IsDragging)
                {
                    _massCapacityUIRectTransform.anchoredPosition = new Vector2(
                        PluginConfig.Instance.MassCapacityUIPositionX.Value,
                        PluginConfig.Instance.MassCapacityUIPositionY.Value
                    );
                }
            }

            float scale = PluginConfig.Instance.MassCapacityUIScale.Value;
            if (_massCapacityUIInstance != null)
            {
                _massCapacityUIInstance.transform.localScale = Vector3.one * scale;
            }
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

            if (PluginConfig.Instance.EnableBalance.Value && _bagController != null)
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

                            rect.localPosition = GetMeterPoint(frac);
                            rect.localEulerAngles = new Vector3(0f, 0f, 226.44f - frac * 98.88f + 90.5f);
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
        public float AccentStart;
        public float AccentEnd;
        public float AccentStrength;
        public float SettleStrength;
        public bool ReverseFill;

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
                    targetColor = Color.Lerp(colorStart, colorMid, normalizedX * 2f);
                else
                    targetColor = Color.Lerp(colorMid, colorEnd, (normalizedX - 0.5f) * 2f);

                Color gradientColor = targetColor;
                gradientColor.a *= vertex.color.a;
                Color finalColor = Color.Lerp(vertex.color, gradientColor, Intensity);
                float fillPosition = ReverseFill ? 1f - normalizedX : normalizedX;
                float highlight = fillPosition >= AccentStart && fillPosition <= AccentEnd ? AccentStrength : 0f;
                highlight = Mathf.Max(highlight, SettleStrength);
                Color bright = Color.Lerp(finalColor, Color.white, highlight);
                bright.a = finalColor.a;
                vertex.color = bright;
                vertices[i] = vertex;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }

    public class OverencumbranceUIGradient : UnityEngine.UI.BaseMeshEffect
    {
        public float Intensity = 1f;
        public float AccentStart;
        public float AccentEnd;
        public float AccentStrength;
        public float SettleStrength;
        public bool ReverseFill;

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
                Color finalColor = Color.Lerp(vertex.color, gradientColor, Intensity);
                float fillPosition = ReverseFill ? 1f - normalizedX : normalizedX;
                float highlight = fillPosition >= AccentStart && fillPosition <= AccentEnd ? AccentStrength : 0f;
                highlight = Mathf.Max(highlight, SettleStrength);
                Color bright = Color.Lerp(finalColor, Color.white, highlight);
                bright.a = finalColor.a;
                vertex.color = bright;
                vertices[i] = vertex;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }
}
