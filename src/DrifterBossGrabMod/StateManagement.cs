using System;
using System.Collections.Generic;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{
    public static class StateManagement
    {
        // Cached config values for performance
        internal static bool cachedDebugLogsEnabled;

        public static void Initialize(bool debugLogsEnabled)
        {
            cachedDebugLogsEnabled = debugLogsEnabled;
        }

        public static void UpdateDebugLogging(bool debugLogsEnabled)
        {
            cachedDebugLogsEnabled = debugLogsEnabled;
        }

        public static void DisableRenderersForInvisibility(GameObject obj, Dictionary<Renderer, bool> originalStates)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (!originalStates.ContainsKey(renderer))
                {
                    originalStates[renderer] = renderer.enabled;
                }
                renderer.enabled = false;
            }
        }

        public static void DisableColliders(GameObject obj, Dictionary<Collider, bool> originalStates)
        {
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                if (!originalStates.ContainsKey(col))
                {
                    originalStates[col] = col.enabled;
                }
                if (!col.isTrigger)
                {
                    col.enabled = false;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Disabled non-trigger collider {col.name} on {obj.name}");
                    }
                }
                else if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} Kept trigger collider {col.name} enabled on {obj.name}");
                }
            }
        }

        public static void DisableInteractable(IInteractable interactable, Dictionary<MonoBehaviour, bool> originalStates)
        {
            var interactableMB = interactable as MonoBehaviour;
            if (interactableMB != null)
            {
                if (!originalStates.ContainsKey(interactableMB))
                {
                    originalStates[interactableMB] = interactableMB.enabled;
                }
                interactableMB.enabled = false;
            }
        }

        public static void DisableMovementColliders(GameObject obj, Dictionary<GameObject, bool> originalStates)
        {
            var modelLocator = obj.GetComponent<ModelLocator>();
            if (modelLocator && modelLocator.modelTransform)
            {
                foreach (Transform child in modelLocator.modelTransform.GetComponentsInChildren<Transform>(true))
                {
                    var collider = child.GetComponent<Collider>();
                    int layer = child.gameObject.layer;
                    string layerName = LayerMask.LayerToName(layer);
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Checking {child.name}, layer: {layer} ({layerName}), hasCollider: {collider != null}");
                    }
                    if (collider != null)
                    {
                        if (!originalStates.ContainsKey(child.gameObject))
                        {
                            originalStates[child.gameObject] = child.gameObject.activeSelf;
                        }
                        child.gameObject.SetActive(false);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabled {child.name} due to collider on layer {layerName}");
                        }
                    }
                }
            }
        }

        #region State Restoration Methods

        public static void RestoreRenderers(Dictionary<Renderer, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalStates.Clear();
        }

        public static void RestoreColliders(Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Restored collider {kvp.Key.name} (trigger: {kvp.Key.isTrigger}) to enabled={kvp.Value}");
                    }
                }
            }
            originalStates.Clear();
        }

        public static void RestoreInteractables(Dictionary<MonoBehaviour, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalStates.Clear();
        }

        public static void RestoreIsTrigger(Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.isTrigger = kvp.Value;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Restored collider {kvp.Key.name} isTrigger to {kvp.Value}");
                    }
                }
            }
            originalStates.Clear();
        }

        public static void RestoreMovementColliders(Dictionary<GameObject, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetActive(kvp.Value);
                }
            }
            originalStates.Clear();
        }

        public static void RestoreHighlights(Dictionary<Highlight, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalStates.Clear();
        }

        public static void RestoreRigidbody(GameObject obj)
        {
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} Restored Rigidbody for {obj.name}");
                }
            }
        }

        #endregion
    }

    public class GrabbedObjectState : MonoBehaviour
    {
        public Dictionary<Collider, bool> originalColliderStates = new Dictionary<Collider, bool>();
        public Dictionary<Collider, bool> originalIsTrigger = new Dictionary<Collider, bool>();
        public Dictionary<MonoBehaviour, bool> originalInteractableStates = new Dictionary<MonoBehaviour, bool>();
        public Dictionary<GameObject, bool> originalMovementStates = new Dictionary<GameObject, bool>();
        public Dictionary<Renderer, bool> originalRendererStates = new Dictionary<Renderer, bool>();
        public Dictionary<Highlight, bool> originalHighlightStates = new Dictionary<Highlight, bool>();

        public void RestoreAllStates()
        {
            if (StateManagement.cachedDebugLogsEnabled)
            {
                Log.Info($"{Constants.LogPrefix} Restoring all states for {gameObject.name} on landing");
            }

            // Restore all states
            StateManagement.RestoreColliders(originalColliderStates);
            StateManagement.RestoreIsTrigger(originalIsTrigger);
            StateManagement.RestoreInteractables(originalInteractableStates);
            StateManagement.RestoreMovementColliders(originalMovementStates);
            StateManagement.RestoreRenderers(originalRendererStates);

            // Restore highlight states
            StateManagement.RestoreHighlights(originalHighlightStates);

            // Re-enable Rigidbody
            StateManagement.RestoreRigidbody(gameObject);

            // Remove this component since restoration is complete
            Destroy(this);
        }
    }
}