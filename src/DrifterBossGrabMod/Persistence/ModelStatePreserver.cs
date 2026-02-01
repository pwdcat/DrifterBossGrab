using UnityEngine;
using RoR2;
using System.Collections.Generic;

namespace DrifterBossGrabMod
{
    public class ModelStatePreserver : MonoBehaviour
    {
        public bool originalAutoUpdateModelTransform;
        public Vector3 originalInitialPosition;
        public Quaternion originalInitialRotation;
        public Vector3 originalInitialScale;
        public Transform? originalModelParent;

        // Root-relative values (preserving world-scale and world-offset when flattened to root)
        public Vector3 rootRelativePosition;
        public Quaternion rootRelativeRotation;
        public Vector3 rootRelativeScale;
        
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
            if (_modelLocator != null && _modelLocator.modelTransform != null)
            {
                // Store original local values
                originalAutoUpdateModelTransform = _modelLocator.autoUpdateModelTransform;
                originalInitialPosition = _modelLocator.modelTransform.localPosition;
                originalInitialRotation = _modelLocator.modelTransform.localRotation;
                originalInitialScale = _modelLocator.modelTransform.localScale;
                originalModelParent = _modelLocator.modelTransform.parent;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ModelStatePreserver.Awake] Captured state for {gameObject.name}");
                    Log.Info($"[ModelStatePreserver.Awake] Original Position: {originalInitialPosition}");
                    Log.Info($"[ModelStatePreserver.Awake] Original Rotation: {originalInitialRotation}");
                    Log.Info($"[ModelStatePreserver.Awake] Original Scale: {originalInitialScale}");
                }

                // Store root-relative values (relative to this GameObject's transform)
                rootRelativePosition = transform.InverseTransformPoint(_modelLocator.modelTransform.position);
                rootRelativeRotation = Quaternion.Inverse(transform.rotation) * _modelLocator.modelTransform.rotation;
                
                // Calculate scale relative to root
                Vector3 modelWorldScale = _modelLocator.modelTransform.lossyScale;
                Vector3 rootWorldScale = transform.lossyScale;
                rootRelativeScale = new Vector3(
                    rootWorldScale.x != 0 ? modelWorldScale.x / rootWorldScale.x : modelWorldScale.x,
                    rootWorldScale.y != 0 ? modelWorldScale.y / rootWorldScale.y : modelWorldScale.y,
                    rootWorldScale.z != 0 ? modelWorldScale.z / rootWorldScale.z : modelWorldScale.z
                );
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning($"[ModelStatePreserver.Awake] No ModelLocator or modelTransform found on {gameObject.name}");
            }

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

        public void RestoreOriginalState(bool restoreParent = true)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ModelStatePreserver.RestoreOriginalState] Called for {gameObject.name}, restoreParent: {restoreParent}");
            }
            if (_modelLocator != null && _modelLocator.modelTransform != null)
            {
                // First restore parent relationship if requested
                if (restoreParent)
                {
                    _modelLocator.modelTransform.SetParent(originalModelParent, false);
                    
                    // Restore local transform values
                    _modelLocator.modelTransform.localPosition = originalInitialPosition;
                    _modelLocator.modelTransform.localRotation = originalInitialRotation;
                    _modelLocator.modelTransform.localScale = originalInitialScale;
                }
                else
                {
                    // Restore root-relative values (useful when model has been flattened/re-parented to root)
                    if (_modelLocator.modelTransform != gameObject.transform)
                    {
                        _modelLocator.modelTransform.localPosition = rootRelativePosition;
                        _modelLocator.modelTransform.localRotation = rootRelativeRotation;
                        _modelLocator.modelTransform.localScale = rootRelativeScale;
                    }
                    else
                    {
                        // If model is root itself, we only restore its original scale.
                        // Position and rotation are managed by ejection/projectile/physics state.
                        _modelLocator.modelTransform.localScale = originalInitialScale;
                    }
                }

                // Finally restore autoUpdateModelTransform to its original value
                _modelLocator.autoUpdateModelTransform = originalAutoUpdateModelTransform;

                // Restore renderer states
                foreach (var kvp in _rendererStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = kvp.Value;
                    }
                }

                // Restore collider states
                foreach (var kvp in _colliderStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = kvp.Value;
                    }
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
