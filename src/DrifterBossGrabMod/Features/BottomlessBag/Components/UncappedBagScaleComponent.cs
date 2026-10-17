#nullable enable
using System;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod.Core;
using UnityEngine.SceneManagement;

namespace DrifterBossGrabMod.Features
{

    [DefaultExecutionOrder(10000)]
    public class UncappedBagScaleComponent : MonoBehaviour
    {
        private DrifterBagController? _bagController;
        private SkinnedMeshRenderer? _skinnedMeshRenderer;
        private Transform[]? _bones;
        private Transform? _scalePivot;
        private Transform? _modelTransform;
        private GameObject? _renderRig;
        private Transform[]? _renderBones;
        private bool[]? _bagBodyBones;
        private const float BackwardOffsetPerScale = 0.1f;
        private Bounds _originalBounds;
        private float _targetScale = 1f;
        private float _currentScale = 1f;
        private bool _isInitialized = false;
        private bool _usingRenderBones;

        public bool IsInitialized => _isInitialized;

        public float TargetScale
        {
            get => _targetScale;
            set
            {
                _targetScale = float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(value, 1f);
            }
        }
        public void Initialize(DrifterBagController bagController)
        {
            if (_isInitialized)
            {
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
                _scalePivot = FindScalePivot();
                _bagBodyBones = new bool[_bones.Length];
                for (int i = 0; i < _bones.Length; i++)
                {
                    _bagBodyBones[i] = _bones[i] != null && IsBagBodyBone(_bones[i].name);
                }
                _originalBounds = _skinnedMeshRenderer.localBounds;
                _isInitialized = true;
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
                _modelTransform = modelTransform;
                if (modelTransform != null)
                {
                    foundTransform = modelTransform.Find("meshBag");
                }
            }

            if (foundTransform != null)
            {
                _skinnedMeshRenderer = foundTransform.GetComponent<SkinnedMeshRenderer>();
                _bones = _skinnedMeshRenderer != null ? _skinnedMeshRenderer.bones : null;
            }
        }

        private Transform? FindScalePivot()
        {
            if (_bones == null) return null;
            foreach (var bone in _bones)
                if (bone != null && string.Equals(bone.name, "bag04_l", StringComparison.OrdinalIgnoreCase)) return bone;
            foreach (var bone in _bones)
                if (bone != null && string.Equals(bone.name, "bagMaster_l", StringComparison.OrdinalIgnoreCase)) return bone;
            var body = _bagController != null ? _bagController.GetComponent<CharacterBody>() : null;
            var model = body != null && body.modelLocator != null ? body.modelLocator.modelTransform : null;
            if (model != null)
                foreach (var bone in model.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(bone.name, "bagMaster_l", StringComparison.OrdinalIgnoreCase)) return bone;
            return model != null ? model : _skinnedMeshRenderer?.transform;
        }

        private static bool IsBagBodyBone(string name)
        {
            return string.Equals(name, "bag04_l", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("bagBulk", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("bagBulge", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("bagPocket", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("bagFlap", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureRenderBones()
        {
            if (_renderRig != null || _bones == null) return;
            _renderRig = new GameObject("DrifterBagRenderRig");
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                SceneManager.MoveGameObjectToScene(_renderRig, gameObject.scene);
            _renderBones = new Transform[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                var bone = new GameObject(_bones[i].name + "_BagRender").transform;
                bone.SetParent(_renderRig.transform, false);
                _renderBones[i] = bone;
            }
        }

        public void UpdateScaleFromMass(float mass)
        {
            if (!_isInitialized)
            {
                Log.Warning("[UncappedBagScaleComponent] UpdateScaleFromMass called but component is not initialized");
                return;
            }

            if (_bones == null)
            {
                Log.Warning("[UncappedBagScaleComponent] UpdateScaleFromMass called but bones are null");
                return;
            }

            var bagController = GetComponent<DrifterBagController>();
            float maxCapacity = bagController != null ? Balance.CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;

            if (!PluginConfig.Instance.EnableBalance.Value)
            {
                TargetScale = 1f;
                return;
            }

            float cap = PluginConfig.Instance.IsBagScaleCapInfinite ? float.PositiveInfinity : PluginConfig.Instance.ParsedBagScaleCap;
            TargetScale = StateCalculator.CalculateBagExtraScale(mass, maxCapacity, cap);
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _skinnedMeshRenderer == null || _bones == null || _scalePivot == null) return;
            float target = PluginConfig.Instance.EnableBalance.Value ? _targetScale : 1f;
            if (!PluginConfig.Instance.IsBagScaleCapInfinite)
            {
                float cap = PluginConfig.Instance.ParsedBagScaleCap;
                target = float.IsNaN(cap) || float.IsInfinity(cap) ? 1f : Mathf.Min(target, Mathf.Max(1f, cap));
            }
            _currentScale = Mathf.Lerp(_currentScale, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
            if (Mathf.Abs(_currentScale - target) < 0.0001f) _currentScale = target;
            if (_currentScale == 1f)
            {
                RestoreRenderBones();
                return;
            }
            EnsureRenderBones();
            if (_renderBones == null || _bagBodyBones == null) return;
            Vector3 pivot = _scalePivot.position;
            float offset = BackwardOffsetPerScale * (_currentScale - 1f);
            Vector3 backwardOffset = _modelTransform != null
                ? _modelTransform.TransformVector(Vector3.back * offset)
                : Vector3.zero;
            for (int i = 0; i < _bones.Length; i++)
            {
                var source = _bones[i];
                var render = _renderBones[i];
                if (source == null || render == null) continue;
                float scale = _bagBodyBones[i] ? _currentScale : 1f;
                render.SetPositionAndRotation(pivot + (source.position - pivot) * scale + backwardOffset, source.rotation);
                render.localScale = source.lossyScale * scale;
            }
            if (!_usingRenderBones)
            {
                _skinnedMeshRenderer.bones = _renderBones;
                _usingRenderBones = true;
            }
            Vector3 localPivot = _skinnedMeshRenderer.transform.InverseTransformPoint(pivot);
            var bounds = new Bounds(
                localPivot + (_originalBounds.center - localPivot) * _currentScale,
                _originalBounds.size * _currentScale);
            bounds.Encapsulate(_originalBounds);
            bounds.center += _skinnedMeshRenderer.transform.InverseTransformVector(backwardOffset);
            _skinnedMeshRenderer.localBounds = bounds;
        }

        private void RestoreRenderBones()
        {
            if (!_usingRenderBones || _skinnedMeshRenderer == null || _bones == null) return;
            _skinnedMeshRenderer.bones = _bones;
            _skinnedMeshRenderer.localBounds = _originalBounds;
            _usingRenderBones = false;
        }

        private void OnDisable() => ResetBoneScales();

        private void OnDestroy()
        {
            ResetBoneScales();
            if (_renderRig != null) Destroy(_renderRig);
            _isInitialized = false;
        }

        public void ResetBoneScales()
        {
            RestoreRenderBones();

            _currentScale = 1.0f;
            _targetScale = 1.0f;
        }

    }

}
