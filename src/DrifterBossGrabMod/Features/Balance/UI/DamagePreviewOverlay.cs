#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.UI
{

    public class DamagePreviewOverlay : MonoBehaviour, IConfigObserver
    {
        private void OnEnable()
        {
            ConfigChangeNotifier.AddObserver(this);
        }

        private void OnDisable()
        {
            ConfigChangeNotifier.RemoveObserver(this);
            UnregisterEventHandlers();
        }

        public void OnConfigChanged(string key, object value)
        {
            if (key == "DamagePreviewColor")
            {
                UpdateColor();
            }
        }
        public GameObject? targetObject;

        public DrifterBagController? bagController;

        private HealthBar? _healthBar;
        private Image? _previewImage;
        private RectTransform? _previewRect;
        private bool _initialized;
        private bool _eventHandlersRegistered;

        private float _logTimer;
        private const float LogInterval = 1.0f;

        private float _cachedDamageFraction = 0f;
        private int _cachedTargetInstanceId = 0;
        private bool _cacheValid = false;

        private void Awake()
        {
            _healthBar = GetComponent<HealthBar>();
        }

        private void LateUpdate()
        {
            if (!PluginConfig.Instance.EnableDamagePreview.Value)
            {
                if (_previewRect && _previewRect!.gameObject.activeSelf)
                    _previewRect.gameObject.SetActive(false);
                return;
            }

            if (!_healthBar || !bagController || !targetObject || targetObject!.name.Contains("EmptySlotMarker"))
            {
                if (_previewRect && _previewRect!.gameObject.activeSelf)
                    _previewRect.gameObject.SetActive(false);
                return;
            }

            if (!_initialized)
            {
                CreatePreviewBar();
                _initialized = true;
            }

            if (!_previewImage) return;

            if (!_previewRect!.gameObject!.activeSelf)
                _previewRect!.gameObject.SetActive(true);

            UpdatePreviewBar();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                _logTimer += Time.deltaTime;
                if (_logTimer >= LogInterval)
                {
                    _logTimer = 0f;
                    SlamDamageCalculator.LogDetails(bagController!, targetObject!);
                }
            }
        }

        private void CreatePreviewBar()
        {
            if (!_healthBar) return;
            var barContainer = _healthBar!.barContainer;
            if (!barContainer)
            {
                Log.Warning("[DamagePreviewOverlay] HealthBar.barContainer is null");
                return;
            }

            GameObject? barPrefab = _healthBar.style?.barPrefab;
            if (barPrefab == null)
            {
                throw new InvalidOperationException("HealthBar.style.barPrefab is null - cannot create damage preview");
            }
            GameObject barInstance = Instantiate(barPrefab, barContainer);
            barInstance.name = "DamagePreview";
            Log.Debug("[DamagePreviewOverlay] Created damage preview inside barContainer");

            _previewRect = barInstance.GetComponent<RectTransform>();
            if (!_previewRect)
                _previewRect = barInstance.AddComponent<RectTransform>();

            _previewImage = barInstance.GetComponent<Image>();
            if (!_previewImage)
                _previewImage = barInstance.AddComponent<Image>();

            _previewImage.color = PluginConfig.Instance.DamagePreviewColor.Value;
            _previewImage.raycastTarget = false;
        }

        private void UpdatePreviewBar()
        {

            int currentTargetInstanceId = targetObject ? targetObject!.GetInstanceID() : 0;
            bool targetChanged = currentTargetInstanceId != _cachedTargetInstanceId;

            if (!_cacheValid || targetChanged)
            {

                _cachedDamageFraction = SlamDamageCalculator.GetPredictedDamageFraction(bagController!, targetObject!);
                _cachedTargetInstanceId = currentTargetInstanceId;
                _cacheValid = true;
            }

            float damageFraction = _cachedDamageFraction;
            float healthFraction = GetCurrentHealthFraction();

            if (damageFraction <= 0f)
            {
                if (_previewImage != null) _previewImage.enabled = false;
                return;
            }

            if (_previewImage != null)
            {
                _previewImage.enabled = true;
                _previewImage.color = PluginConfig.Instance.DamagePreviewColor.Value;
            }

            float previewStart = Mathf.Max(0f, healthFraction - damageFraction);
            float previewEnd = healthFraction;

            if (_previewRect != null)
            {
                _previewRect.anchorMin = new Vector2(previewStart, 0f);
                _previewRect.anchorMax = new Vector2(previewEnd, 1f);
                _previewRect.anchoredPosition = Vector2.zero;

                _previewRect.offsetMin = new Vector2(-0.5f, -0.5f);
                _previewRect.offsetMax = new Vector2(0.5f, 0.5f);
            }

            var barContainer = _healthBar?.barContainer;
            if (_previewRect != null && barContainer != null && _previewRect.transform.GetSiblingIndex() < barContainer.childCount - 1)
            {
                _previewRect.SetAsLastSibling();
            }
        }

        private float GetCurrentHealthFraction()
        {

            if (_healthBar != null && _healthBar.source != null)
            {
                float total = _healthBar.source!.fullCombinedHealth;
                if (total <= 0f) return 0f;
                return Mathf.Clamp01(_healthBar.source!.combinedHealth / total);
            }

            if (_healthBar != null && _healthBar.altSource)
            {
                if (_healthBar.altSource.maxDurability <= 0) return 0f;
                return Mathf.Clamp01((float)_healthBar.altSource.durability / _healthBar.altSource.maxDurability);
            }

            return 0f;
        }

        public void UpdateColor()
        {
            if (_previewImage)
            {
                _previewImage!.color = PluginConfig.Instance.DamagePreviewColor.Value;
            }
        }

        public void SetTarget(GameObject? target, DrifterBagController? controller)
        {
            UnregisterEventHandlers();
            targetObject = target;
            bagController = controller;
            InvalidateCache();
            RegisterEventHandlers();
        }

        public void InvalidateCache()
        {
            _cacheValid = false;
        }

        private void RegisterEventHandlers()
        {
            if (_eventHandlersRegistered || bagController == null) return;

            DrifterBossGrabMod.API.DrifterBagAPI.OnMassRecalculated += OnMassRecalculated;
            _eventHandlersRegistered = true;
        }

        private void UnregisterEventHandlers()
        {
            if (!_eventHandlersRegistered) return;

            DrifterBossGrabMod.API.DrifterBagAPI.OnMassRecalculated -= OnMassRecalculated;
            _eventHandlersRegistered = false;
        }

        private void OnMassRecalculated(RoR2.DrifterBagController controller, float totalMass, float previousTotalMass)
        {
            if (controller == bagController)
            {
                InvalidateCache();
            }
        }

        public static void InvalidateAllCaches()
        {

            var overlays = UnityEngine.Object.FindObjectsByType<DamagePreviewOverlay>(UnityEngine.FindObjectsSortMode.None);
            foreach (var overlay in overlays)
            {
                overlay.InvalidateCache();
            }
        }

        private void OnDestroy()
        {
            UnregisterEventHandlers();
            if (_previewRect)
            {
                Destroy(_previewRect!.gameObject);
            }
        }
    }
}
