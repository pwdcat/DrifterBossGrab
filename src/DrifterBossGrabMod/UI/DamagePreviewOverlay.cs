using UnityEngine;
using UnityEngine.UI;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.UI
{
    // Adds a damage preview sub-bar to a HealthBar
    public class DamagePreviewOverlay : MonoBehaviour
    {
        // The bagged object this overlay tracks
        public GameObject? targetObject;

        // The bag controller for damage calculation
        public DrifterBagController? bagController;

        private HealthBar? _healthBar;
        private Image? _previewImage;
        private RectTransform? _previewRect;
        private bool _initialized;

        // Debug logging throttle
        private float _logTimer;
        private const float LogInterval = 1.0f;

        // Cached damage fraction to avoid recalculating every frame
        private float _cachedDamageFraction = 0f;
        private int _cachedTargetInstanceId = 0;
        private bool _cacheValid = false;

        // Static cache key for invalidation across all overlays
        private static int _globalCacheVersion = 0;

        private void Awake()
        {
            _healthBar = GetComponent<HealthBar>();
        }

        private void LateUpdate()
        {
            if (!PluginConfig.Instance.EnableDamagePreview.Value)
            {
                if (_previewRect && _previewRect.gameObject.activeSelf)
                    _previewRect.gameObject.SetActive(false);
                return;
            }

            if (!_healthBar || !bagController || !targetObject || targetObject.name.Contains("EmptySlotMarker"))
            {
                if (_previewRect && _previewRect.gameObject.activeSelf)
                    _previewRect.gameObject.SetActive(false);
                return;
            }

            // Lazy init
            if (!_initialized)
            {
                CreatePreviewBar();
                _initialized = true;
            }

            if (!_previewImage) return;

            // Ensure our bar is active
            if (!_previewRect!.gameObject.activeSelf)
                _previewRect.gameObject.SetActive(true);

            UpdatePreviewBar();

            // Debug logging
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                _logTimer += Time.deltaTime;
                if (_logTimer >= LogInterval)
                {
                    _logTimer = 0f;
                    SlamDamageCalculator.LogDetails(bagController, targetObject);
                }
            }
        }

        private void CreatePreviewBar()
        {
            if (!_healthBar) return;
            var barContainer = _healthBar.barContainer;
            if (!barContainer)
            {
                Log.Warning("[DamagePreviewOverlay] HealthBar.barContainer is null");
                return;
            }

            // Create bar inside barContainer for alignment.
            GameObject? barPrefab = _healthBar.style?.barPrefab;
            GameObject barInstance;

            if (barPrefab != null)
            {
                // Instantiate a clone of the vanilla HealthBarSubBar prefab
                barInstance = Instantiate(barPrefab, barContainer);
                barInstance.name = "DamagePreview";
                Log.Info("[DamagePreviewOverlay] Created damage preview inside barContainer");
            }
            else
            {
                // Fallback
                barInstance = new GameObject("DamagePreview");
                barInstance.transform.SetParent(barContainer, false);
                barInstance.AddComponent<CanvasRenderer>();
                Log.Warning("[DamagePreviewOverlay] barPrefab was null, using fallback");
            }

            _previewRect = barInstance.GetComponent<RectTransform>();
            if (!_previewRect)
                _previewRect = barInstance.AddComponent<RectTransform>();

            _previewImage = barInstance.GetComponent<Image>();
            if (!_previewImage)
                _previewImage = barInstance.AddComponent<Image>();

            // Configure the preview appearance
            _previewImage.color = PluginConfig.Instance.DamagePreviewColor.Value;
            _previewImage.raycastTarget = false;
        }

        private void UpdatePreviewBar()
        {
            // Check if cache is valid
            int currentTargetInstanceId = targetObject ? targetObject.GetInstanceID() : 0;
            bool targetChanged = currentTargetInstanceId != _cachedTargetInstanceId;

            // Recalculate if cache is invalid or target changed
            if (!_cacheValid || targetChanged)
            {
                // Recalculate if cache is invalid or target changed.
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

            // The preview shows from (healthFraction - damageFraction) to healthFraction
            float previewStart = Mathf.Max(0f, healthFraction - damageFraction);
            float previewEnd = healthFraction;

            // Update Rect within barContainer
            if (_previewRect != null)
            {
                _previewRect.anchorMin = new Vector2(previewStart, 0f);
                _previewRect.anchorMax = new Vector2(previewEnd, 1f);
                _previewRect.anchoredPosition = Vector2.zero;

                // Apply margins to match vanilla Green Health Bar (Child 4 - Tiled) offsets found in dump
                // Vanilla Child 4: OffsetMin=(-0.5, -0.5), OffsetMax=(0.5, 0.5)
                // This expands the bar by 0.5 units on all sides relative to anchors
                _previewRect.offsetMin = new Vector2(-0.5f, -0.5f);
                _previewRect.offsetMax = new Vector2(0.5f, 0.5f);
            }

            // Force Z-order to be on top of other bars
            // Optimization: Only set if not already last
            var barContainer = _healthBar?.barContainer; // Get barContainer here for the check
            if (_previewRect != null && barContainer != null && _previewRect.transform.GetSiblingIndex() < barContainer.childCount - 1)
            {
                _previewRect.SetAsLastSibling();
            }
        }

        private float GetCurrentHealthFraction()
        {
            // CharacterBody health source
            if (_healthBar != null && _healthBar.source != null)
            {
                float total = _healthBar.source.fullCombinedHealth;
                if (total <= 0f) return 0f;
                return Mathf.Clamp01(_healthBar.source.combinedHealth / total);
            }

            // SpecialObjectAttributes alt source
            if (_healthBar != null && _healthBar.altSource)
            {
                if (_healthBar.altSource.maxDurability <= 0) return 0f;
                return Mathf.Clamp01((float)_healthBar.altSource.durability / _healthBar.altSource.maxDurability);
            }

            return 0f;
        }

        // Updates the preview color from config.
        public void UpdateColor()
        {
            if (_previewImage)
            {
                _previewImage.color = PluginConfig.Instance.DamagePreviewColor.Value;
            }
        }

        // Sets the target object.
        public void SetTarget(GameObject target, DrifterBagController controller)
        {
            targetObject = target;
            bagController = controller;
            InvalidateCache();
        }

        // Invalidates cache.
        public void InvalidateCache()
        {
            _cacheValid = false;
        }

        // Invalidate all damage preview caches.
        public static void InvalidateAllCaches()
        {
            _globalCacheVersion++;

            // Find all DamagePreviewOverlay instances and invalidate their caches
            var overlays = UnityEngine.Object.FindObjectsByType<DamagePreviewOverlay>(UnityEngine.FindObjectsSortMode.None);
            foreach (var overlay in overlays)
            {
                overlay.InvalidateCache();
            }
        }

        private void OnDestroy()
        {
            if (_previewRect)
            {
                Destroy(_previewRect.gameObject);
            }
        }
    }
}
