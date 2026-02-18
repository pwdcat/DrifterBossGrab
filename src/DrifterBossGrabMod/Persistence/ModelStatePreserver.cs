using System;
using UnityEngine;
using RoR2;
using System.Collections.Generic;

namespace DrifterBossGrabMod
{
    public class ModelStatePreserver : MonoBehaviour
    {
        // Original state data structure
        private struct ModelStateData
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Transform? parent;
            public bool autoUpdateModelTransform;
        }

        private ModelStateData _originalState;
        private ModelStateData _rootRelativeState;

        // Public fields for backward compatibility (deprecated)
        [System.Obsolete("Use _originalState instead")]
        public bool originalAutoUpdateModelTransform => _originalState.autoUpdateModelTransform;
        [System.Obsolete("Use _originalState.position instead")]
        public Vector3 originalInitialPosition => _originalState.position;
        [System.Obsolete("Use _originalState.rotation instead")]
        public Quaternion originalInitialRotation => _originalState.rotation;
        [System.Obsolete("Use _originalState.scale instead")]
        public Vector3 originalInitialScale => _originalState.scale;
        [System.Obsolete("Use _originalState.parent instead")]
        public Transform? originalModelParent => _originalState.parent;
        [System.Obsolete("Use _rootRelativeState.position instead")]
        public Vector3 rootRelativePosition => _rootRelativeState.position;
        [System.Obsolete("Use _rootRelativeState.rotation instead")]
        public Quaternion rootRelativeRotation => _rootRelativeState.rotation;
        [System.Obsolete("Use _rootRelativeState.scale instead")]
        public Vector3 rootRelativeScale => _rootRelativeState.scale;

        private ModelLocator? _modelLocator;
        private readonly Dictionary<Renderer, bool> _rendererStates = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Collider, bool> _colliderStates = new Dictionary<Collider, bool>();

        private void Awake()
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ModelStatePreserver.Awake] === MODELSTATEPRESERVER CREATED ===");
                Log.Info($"[ModelStatePreserver.Awake] GameObject: {gameObject.name} (InstanceID: {gameObject.GetInstanceID()})");
                Log.Info($"[ModelStatePreserver.Awake] EnableObjectPersistence: {PluginConfig.Instance.EnableObjectPersistence.Value}");
                Log.Info($"[ModelStatePreserver.Awake] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                Log.Info($"[ModelStatePreserver.Awake] NetworkServer.active: {UnityEngine.Networking.NetworkServer.active}");
                Log.Info($"[ModelStatePreserver.Awake] =======================================");
            }
            _modelLocator = GetComponent<ModelLocator>();
            CaptureModelState();
            CaptureRendererAndColliderStates();
        }

        // Captures the model state from the ModelLocator component
        private void CaptureModelState()
        {
            if (_modelLocator != null && _modelLocator.modelTransform != null)
            {
                // Store original local values
                _originalState.autoUpdateModelTransform = _modelLocator.autoUpdateModelTransform;
                _originalState.position = _modelLocator.modelTransform.localPosition;
                _originalState.rotation = _modelLocator.modelTransform.localRotation;
                _originalState.scale = _modelLocator.modelTransform.localScale;
                _originalState.parent = _modelLocator.modelTransform.parent;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ModelStatePreserver.CaptureModelState] Captured state for {gameObject.name}");
                    Log.Info($"[ModelStatePreserver.CaptureModelState] Original Position: {_originalState.position}");
                    Log.Info($"[ModelStatePreserver.CaptureModelState] Original Rotation: {_originalState.rotation}");
                    Log.Info($"[ModelStatePreserver.CaptureModelState] Original Scale: {_originalState.scale}");
                }

                // Store root-relative values (relative to this GameObject's transform)
                _rootRelativeState.position = transform.InverseTransformPoint(_modelLocator.modelTransform.position);
                _rootRelativeState.rotation = Quaternion.Inverse(transform.rotation) * _modelLocator.modelTransform.rotation;

                // Calculate scale relative to root
                Vector3 modelWorldScale = _modelLocator.modelTransform.lossyScale;
                Vector3 rootWorldScale = transform.lossyScale;
                _rootRelativeState.scale = new Vector3(
                    rootWorldScale.x != 0 ? modelWorldScale.x / rootWorldScale.x : modelWorldScale.x,
                    rootWorldScale.y != 0 ? modelWorldScale.y / rootWorldScale.y : modelWorldScale.y,
                    rootWorldScale.z != 0 ? modelWorldScale.z / rootWorldScale.z : modelWorldScale.z
                );
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning($"[ModelStatePreserver.CaptureModelState] No ModelLocator or modelTransform found on {gameObject.name}");
            }
        }

        // Captures renderer and collider states from the entire object hierarchy
        private void CaptureRendererAndColliderStates()
        {
            // Store renderer states from entire object hierarchy
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer != null && !_rendererStates.ContainsKey(renderer))
                {
                    _rendererStates[renderer] = renderer.enabled;
                }
            }

            // Store collider states from entire object hierarchy
            var colliders = GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider != null && !_colliderStates.ContainsKey(collider))
                {
                    _colliderStates[collider] = collider.enabled;
                }
            }
        }

        // Validates captured state is valid for restoration (checks null references only)
        public bool ValidateState()
        {
            return _modelLocator != null &&
                   _modelLocator.modelTransform != null;
        }

        // Restores original model state with validation and error handling
        // preserveTransform: if true, preserves current transform (root-relative); if false, restores to original parent
        public void RestoreOriginalState(bool preserveTransform = false)
        {
            if (!ValidateState())
            {
                Log.Warning($"[ModelStatePreserver.RestoreOriginalState] Invalid state for {gameObject.name}, skipping restore");
                return;
            }

            // Restore with validation
            try
            {
                ApplyState(_originalState, preserveTransform);
            }
            catch (Exception ex)
            {
                Log.Error($"[ModelStatePreserver.RestoreOriginalState] Failed to restore state for {gameObject.name}: {ex.Message}");
                // Fallback to default state
                ApplyDefaultState();
            }
        }

        // Applies the specified model state to the ModelLocator
        private void ApplyState(ModelStateData state, bool preserveTransform)
        {
            if (_modelLocator == null || _modelLocator.modelTransform == null)
            {
                throw new InvalidOperationException("ModelLocator or modelTransform is null");
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ModelStatePreserver.ApplyState] Called for {gameObject.name}, preserveTransform: {preserveTransform}");
            }

            if (!preserveTransform)
            {
                // Restore parent relationship
                _modelLocator.modelTransform.SetParent(state.parent, false);

                // Restore local transform values
                _modelLocator.modelTransform.localPosition = state.position;
                _modelLocator.modelTransform.localRotation = state.rotation;
                _modelLocator.modelTransform.localScale = state.scale;
            }
            else
            {
                // Restore root-relative values (useful when model has been flattened/re-parented to root)
                if (_modelLocator.modelTransform != gameObject.transform)
                {
                    _modelLocator.modelTransform.localPosition = _rootRelativeState.position;
                    _modelLocator.modelTransform.localRotation = _rootRelativeState.rotation;
                    _modelLocator.modelTransform.localScale = _rootRelativeState.scale;
                }
                else
                {
                    // If model is root itself, we only restore its original scale.
                    // Position and rotation are managed by ejection/projectile/physics state.
                    _modelLocator.modelTransform.localScale = state.scale;
                }
            }

            // Restore autoUpdateModelTransform to its original value
            _modelLocator.autoUpdateModelTransform = state.autoUpdateModelTransform;

            // Restore renderer states
            RestoreRendererStates();

            // Restore collider states
            RestoreColliderStates();
        }

        // Applies default safe state as a fallback when restoration fails
        private void ApplyDefaultState()
        {
            if (_modelLocator == null || _modelLocator.modelTransform == null)
            {
                Log.Warning($"[ModelStatePreserver.ApplyDefaultState] Cannot apply default state - ModelLocator or modelTransform is null for {gameObject.name}");
                return;
            }

            Log.Warning($"[ModelStatePreserver.ApplyDefaultState] Applying default state for {gameObject.name}");

            try
            {
                // Enable auto-update to let the system handle the model
                _modelLocator.autoUpdateModelTransform = true;

                // Restore renderer states (safe operation)
                RestoreRendererStates();

                // Restore collider states (safe operation)
                RestoreColliderStates();

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ModelStatePreserver.ApplyDefaultState] Default state applied for {gameObject.name}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ModelStatePreserver.ApplyDefaultState] Failed to apply default state for {gameObject.name}: {ex.Message}");
            }
        }

        // Restores saved renderer states
        private void RestoreRendererStates()
        {
            foreach (var kvp in _rendererStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
        }

        // Restores saved collider states
        private void RestoreColliderStates()
        {
            foreach (var kvp in _colliderStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
        }

        private void OnDestroy()
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ModelStatePreserver.OnDestroy] === MODELSTATEPRESERVER DESTROYED ===");
                Log.Info($"[ModelStatePreserver.OnDestroy] GameObject: {gameObject.name} (InstanceID: {gameObject.GetInstanceID()})");
                Log.Info($"[ModelStatePreserver.OnDestroy] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                Log.Info($"[ModelStatePreserver.OnDestroy] =======================================");
            }
        }
    }
}
