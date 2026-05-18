#nullable enable
using System;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.Features
{

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

            if (_skinnedMeshRenderer != null && _bones != null)
            {
                FilterAndCacheBagBones();
                _isInitialized = true;
                Log.Debug("[UncappedBagScaleComponent] Successfully initialized with " + (_filteredBones?.Length ?? 0) + " bag bones");
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
                        Log.Debug($"[UncappedBagScaleComponent] Found meshBag via modelLocator: {modelTransform.name}>meshBag");
                    }
                }
            }

            if (foundTransform != null)
            {
                _skinnedMeshRenderer = foundTransform.GetComponent<SkinnedMeshRenderer>();
                _bones = _skinnedMeshRenderer.bones;
            }
        }

        private void FilterAndCacheBagBones()
        {
            if (_bones == null || _skinnedMeshRenderer == null) return;

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

            int bagBoneCount = 0;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null && ShouldScaleBone(_bones[i].name, scaleKeywords))
                {
                    bagBoneCount++;
                }
            }

            Log.Debug($"[UncappedBagScaleComponent] Found {bagBoneCount} bones matching scale keywords out of {_bones.Length} total bones");

            _filteredBones = new Transform[bagBoneCount];
            _originalBoneScales = new Vector3[bagBoneCount];

            int filteredIndex = 0;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null && ShouldScaleBone(_bones[i].name, scaleKeywords))
                {
                    _filteredBones[filteredIndex] = _bones[i];
                    _originalBoneScales[filteredIndex] = _bones[i].localScale;
                    Log.Debug($"[UncappedBagScaleComponent] Filtered bone [{filteredIndex}]: {_bones[i].name}");
                    filteredIndex++;
                }
            }
        }

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
            float maxCapacity = bagController != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;

            if (mass <= maxCapacity)
            {
                TargetScale = 1.0f;
                Log.Debug($"[UncappedBagScaleComponent] Mass {mass} <= maxCapacity {maxCapacity}, using original animation system");
                return;
            }

            float value = Mathf.Max(mass, 1f);

            float t = (value - 1f) / (maxCapacity - 1f);

            float newScale = 1.0f + t;

            if (!PluginConfig.Instance.IsBagScaleCapInfinite)
            {
                newScale = Mathf.Min(newScale, PluginConfig.Instance.ParsedBagScaleCap);
            }

            TargetScale = newScale;

            Log.Debug($"[UncappedBagScaleComponent] Mass {mass} > maxCapacity {maxCapacity}, calculated scale {newScale:F2} (t={t:F2})");
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

            if (_targetScale <= 1.0f)
            {
                if (_currentScale <= 1.0f) return;

                _currentScale = Mathf.Lerp(_currentScale, 1.0f, Time.deltaTime * 10f);

                if (Mathf.Approximately(_currentScale, 1.0f))
                {
                    _currentScale = 1.0f;
                    ResetBoneScales();
                    return;
                }

                for (int i = 0; i < _filteredBones.Length; i++)
                {
                    if (_filteredBones[i] != null)
                    {
                        _filteredBones[i].localScale = _originalBoneScales[i] * _currentScale;
                    }
                }
                return;
            }

            if (Mathf.Approximately(_currentScale, _targetScale)) return;

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

            if (bonesUpdated > 0)
            {
                Log.Debug($"[UncappedBagScaleComponent] Applied scale {_currentScale:F2} (target: {_targetScale:F2}) to {bonesUpdated} bag bones");
            }
        }

        private void OnDestroy()
        {

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

            Log.Debug($"[UncappedBagScaleComponent] Reset {_filteredBones.Length} bag bones to original scales");
        }

    }

}
