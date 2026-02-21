using System;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.Features
{
    // Component that applies uncapped bag scaling
    public class UncappedBagScaleComponent : MonoBehaviour
    {
        private DrifterBagController? _bagController;
        private SkinnedMeshRenderer? _skinnedMeshRenderer;
        private Transform[]? _bones;
        private Transform[]? _filteredBones;
        private Vector3[]? _originalBoneScales;
        private float _targetScale = 1f;
        private float _currentScale = 1f;
        private bool _isInitialized = false;

        // Gets whether the component has been successfully initialized.
        public bool IsInitialized => _isInitialized;

        public float TargetScale
        {
            get => _targetScale;
            set
            {
                _targetScale = Mathf.Max(value, 1.0f); // Minimum scale to prevent issues
            }
        }
    public void Initialize(DrifterBagController bagController)
        {
            // Prevent duplicate initialization
            if (_isInitialized)
            {
                Log.Debug("[UncappedBagScaleComponent] Already initialized, skipping duplicate initialization");
                return;
            }

            if (bagController == null)
            {
                Log.Error("[UncappedBagScaleComponent] Cannot initialize with to null bag controller");
                return;
            }

            _bagController = bagController;
            FindMeshBagTransform();

            // Initialize bones after finding meshBag
            if (_skinnedMeshRenderer != null && _bones != null)
            {
                FilterAndCacheBagBones();
                _isInitialized = true;
                Log.Info("[UncappedBagScaleComponent] Successfully initialized with " + (_filteredBones?.Length ?? 0) + " bag bones");
            }
            else
            {
                Log.Error("[UncappedBagScaleComponent] Failed to initialize - SkinnedMeshRenderer or bones not found");
                _isInitialized = false;
            }
        }
    private void FindMeshBagTransform()
        {
            Transform? foundTransform = null;

            // Try to find to CharacterBody to access to modelLocator
            var characterBody = _bagController != null ? _bagController.GetComponent<CharacterBody>() : null;
            if (characterBody != null && characterBody.modelLocator != null)
            {
                var modelTransform = characterBody.modelLocator.modelTransform;
                if (modelTransform != null)
                {
                    // Try to find to meshBag as a child of to model transform
                    foundTransform = modelTransform.Find("meshBag");

                    if (foundTransform != null)
                    {
                        Log.Info($"[UncappedBagScaleComponent] Found to meshBag via to modelLocator: {modelTransform.name}>meshBag");
                    }
                }
            }

            // Get to SkinnedMeshRenderer to component
            if (foundTransform != null)
            {
                _skinnedMeshRenderer = foundTransform.GetComponent<SkinnedMeshRenderer>();
                _bones = _skinnedMeshRenderer.bones;
            }
        }
    private void FilterAndCacheBagBones()
        {
            if (_bones == null || _skinnedMeshRenderer == null) return;

            // Keywords for bones that should be scaled
            // These are the bones that are actually modified when grabbing objects
            string[] scaleKeywords = new string[]
            {
                "bagMaster_l",
                "bag04_l",
                "bagBulk_l",
                "bagBulk_l_end",
                "bagBulgeBt_l",
                "bagBulgeRt_l",
                "bagBulgeRt_l_end",
                "bagBulgeLf_l",
                "bagBulgeLf_l_end",
                "bagPocketRt_l",
                "bagPocketRt_l_end",
                "bagPocketLf_l",
                "bagPocketLf_l_end",
                "bagFlap1_l",
                "bagFlap2_l",
                "bagFlap3_l"
            };

            // First, count how many bones match the keywords (case-insensitive)
            int bagBoneCount = 0;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null && ShouldScaleBone(_bones[i].name, scaleKeywords))
                {
                    bagBoneCount++;
                }
            }

            Log.Info($"[UncappedBagScaleComponent] Found {bagBoneCount} bones matching scale keywords out of {_bones.Length} total bones");

            // Create filtered arrays
            _filteredBones = new Transform[bagBoneCount];
            _originalBoneScales = new Vector3[bagBoneCount];

            int filteredIndex = 0;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null && ShouldScaleBone(_bones[i].name, scaleKeywords))
                {
                    _filteredBones[filteredIndex] = _bones[i];
                    _originalBoneScales[filteredIndex] = _bones[i].localScale;
                    Log.Info($"[UncappedBagScaleComponent] Filtered bone [{filteredIndex}]: {_bones[i].name}");
                    filteredIndex++;
                }
            }
        }

    // Check if a bone should be scaled based on keywords
    private bool ShouldScaleBone(string boneName, string[] keywords)
        {
            string boneNameLower = boneName.ToLower();
            foreach (string keyword in keywords)
            {
                if (boneNameLower == keyword.ToLower())
                {
                    return true;
                }
            }
            return false;
        }

        // Update to bag scale based on to current mass
        // Only scales if mass exceeds maxMass (700), otherwise uses original animation system
        public void UpdateScaleFromMass(float mass)
        {
            if (!_isInitialized)
            {
                Log.Warning("[UncappedBagScaleComponent] UpdateScaleFromMass called but component is not initialized");
                return;
            }

            if (_filteredBones == null)
            {
                Log.Warning("[UncappedBagScaleComponent] UpdateScaleFromMass called but filtered bones are null");
                return;
            }

            var bagController = GetComponent<DrifterBagController>();
            float maxCapacity = bagController != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass; // dynamic instead of 700f

            // If mass is at or below maxCapacity, don't use this component
            // Let the original animation system handle scaling
            if (mass <= maxCapacity)
            {
                TargetScale = 1.0f; // Reset to original scale
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UncappedBagScaleComponent] Mass {mass} <= maxCapacity {maxCapacity}, using original animation system");
                }
                return;
            }

            // Only apply uncapped scaling if mass exceeds maxCapacity
            // Calculate scale based on to mass, similar to to original formula but to uncapped
            float value = Mathf.Max(mass, 1f);

            // Calculate to normalized value (0 to to 1 range based on to maxCapacity)
            float t = (value - 1f) / (maxCapacity - 1f);

            // Map to to scale range with floor of 1.0f and allow to exceeding
            // Original formula was 0.5f + 0.5f * t, which gave range 0.5f to 1.0f
            // New formula: 1.0f + t, which gives floor of 1.0f and allows exceeding 1.0f
            float newScale = 1.0f + t;

            TargetScale = newScale;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[UncappedBagScaleComponent] Mass {mass} > maxCapacity {maxCapacity}, calculated scale {newScale:F2} (t={t:F2})");
            }
        }

        private void LateUpdate()
        {
            if (!_isInitialized)
            {
                return;
            }

            if (_filteredBones == null || _originalBoneScales == null)
            {
                return;
            }

            // Only apply scaling if target scale exceeds 1.0f (i.e., mass > maxCapacity)
            // When mass <= maxCapacity, target scale is 1.0f, so we don't scale
            if (_targetScale <= 1.0f) return;

            // Smoothly interpolate to current scale to to target scale
            _currentScale = Mathf.Lerp(_currentScale, _targetScale, Time.deltaTime * 10f);

            // Apply to scale to to filtered bag bones only
            int bonesUpdated = 0;
            for (int i = 0; i < _filteredBones.Length; i++)
            {
                if (_filteredBones[i] != null)
                {
                    // Calculate to new scale for to this bone
                    Vector3 newBoneScale = _originalBoneScales[i] * _currentScale;
                    _filteredBones[i].localScale = newBoneScale;
                    bonesUpdated++;
                }
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value && bonesUpdated > 0)
            {
                Log.Info($"[UncappedBagScaleComponent] Applied scale {_currentScale:F2} (target: {_targetScale:F2}) to {bonesUpdated} bag bones");
            }
        }

        private void OnDestroy()
        {
            // Reset to bone scales when to component is to destroyed
            ResetBoneScales();
            _isInitialized = false;
        }

        // Resets all bag bones to their original scales.
        // Can be called externally if needed for cleanup.
        public void ResetBoneScales()
        {
            if (_filteredBones == null || _originalBoneScales == null) return;

            for (int i = 0; i < _filteredBones.Length; i++)
            {
                if (_filteredBones[i] != null)
                {
                    _filteredBones[i].localScale = _originalBoneScales[i];
                }
            }

            // Reset scale values
            _currentScale = 1.0f;
            _targetScale = 1.0f;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[UncappedBagScaleComponent] Reset {_filteredBones.Length} bag bones to original scales");
            }
        }

        // Resets the component to an uninitialized state.
        // Can be called to force re-initialization.
        public void ResetComponent()
        {
            ResetBoneScales();
            _isInitialized = false;
            _bagController = null;
            _skinnedMeshRenderer = null;
            _bones = null;
            _filteredBones = null;
            _originalBoneScales = null;

            Log.Debug("[UncappedBagScaleComponent] Component reset to uninitialized state");
        }
    }

    // Extension to methods for to Transform to to help find to children to recursively
    public static class TransformExtensions
    {
        public static Transform? FindInChildren(this Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;

                var found = FindInChildren(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Get the full path of a transform in the hierarchy
        public static string GetPath(this Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
