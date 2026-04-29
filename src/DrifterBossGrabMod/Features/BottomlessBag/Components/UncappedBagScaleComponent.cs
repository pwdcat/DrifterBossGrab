#nullable enable
using System;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.Features
{
    // Applies dynamic vertex-level scaling to the Drifter's bag mesh
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

        public bool IsInitialized => _isInitialized;

        public float TargetScale
        {
            get => _targetScale;
            set
            {
                _targetScale = Mathf.Max(value, 1.0f);
            }
        }
        public void Initialize(DrifterBagController bagController)
        {
            if (_isInitialized)
            {
                Log.Debug("[UncappedBagScaleComponent] Already initialized, skipping duplicate initialization");
                return;
            }

            if (bagController == null)
            {
                Log.Error("[UncappedBagScaleComponent] Cannot initialize with null bag controller");
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

            var characterBody = _bagController != null ? _bagController.GetComponent<CharacterBody>() : null;
            if (characterBody != null && characterBody.modelLocator != null)
            {
                var modelTransform = characterBody.modelLocator.modelTransform;
                if (modelTransform != null)
                {
                    foundTransform = modelTransform.Find("meshBag");

                    if (foundTransform != null)
                    {
                        Log.Info($"[UncappedBagScaleComponent] Found meshBag via modelLocator: {modelTransform.name}>meshBag");
                    }
                }
            }

            // Get SkinnedMeshRenderer component
            if (foundTransform != null)
            {
                _skinnedMeshRenderer = foundTransform.GetComponent<SkinnedMeshRenderer>();
                _bones = _skinnedMeshRenderer.bones;
            }
        }
        // Only specific "bulge" bones are scaled
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

        // Bone filtering is case-insensitive to account for naming variations, probably not needed
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

        // Allows the original animation system to handle loads, only taking over for "overstuffed" scenarios.
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
            float maxCapacity = bagController != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass; // Use maxCapacity config value instead of hardcoded 700f

            // If mass is at or below maxCapacity, don't use this component
            // Let the original animation system handle scaling
            if (mass <= maxCapacity)
            {
                TargetScale = 1.0f;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UncappedBagScaleComponent] Mass {mass} <= maxCapacity {maxCapacity}, using original animation system");
                }
                return;
            }

            // The scale formula is linear but uncapped, allowing the bag to grow indefinitely as more mass is added.
            float value = Mathf.Max(mass, 1f);

            float t = (value - 1f) / (maxCapacity - 1f);

            // Map to scale range with floor of 1.0f and allow exceeding
            // Original formula was 0.5f + 0.5f * t, which gave range 0.5f to 1.0f
            // New formula: 1.0f + t, which gives floor of 1.0f and allows exceeding 1.0f
            float newScale = 1.0f + t;

            if (!PluginConfig.Instance.IsBagScaleCapInfinite)
            {
                newScale = Mathf.Min(newScale, PluginConfig.Instance.ParsedBagScaleCap);
            }

            TargetScale = newScale;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[UncappedBagScaleComponent] Mass {mass} > maxCapacity {maxCapacity}, calculated scale {newScale:F2} (t={t:F2})");
            }
        }

        // LateUpdate is used to override any bone modifications applied by the standard animation system in the same frame.
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

            if (_targetScale <= 1.0f) return;

            // Early return optimization: if current scale is already at target scale, skip calculations
            if (Mathf.Approximately(_currentScale, _targetScale)) return;

            // Lerping provides a smooth expansion effect rather than jarring "pops" when mass changes instantly.
            _currentScale = Mathf.Lerp(_currentScale, _targetScale, Time.deltaTime * 10f);

            int bonesUpdated = 0;
            for (int i = 0; i < _filteredBones.Length; i++)
            {
                if (_filteredBones[i] != null)
                {
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
            // Reset bone scales when component is destroyed
            ResetBoneScales();
            _isInitialized = false;
        }

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

            _currentScale = 1.0f;
            _targetScale = 1.0f;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[UncappedBagScaleComponent] Reset {_filteredBones.Length} bag bones to original scales");
            }
        }

    }

}
