using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{
    /// <summary>
    /// Component that preserves the original ModelLocator state for proper restoration after being grabbed/thrown.
    /// </summary>
    public class ModelStatePreserver : MonoBehaviour
    {
        public bool originalAutoUpdateModelTransform;
        public Vector3 originalInitialPosition;
        public Quaternion originalInitialRotation;
        public Vector3 originalInitialScale;
        public Transform originalModelParent;
    
        private ModelLocator _modelLocator;

        private void Awake()
        {
            _modelLocator = GetComponent<ModelLocator>();
            if (_modelLocator != null && _modelLocator.modelTransform != null)
            {
                // Store original values
                originalAutoUpdateModelTransform = _modelLocator.autoUpdateModelTransform;
                originalInitialPosition = _modelLocator.modelTransform.localPosition;
                originalInitialRotation = _modelLocator.modelTransform.localRotation;
                originalInitialScale = _modelLocator.modelTransform.localScale;
                originalModelParent = _modelLocator.modelTransform.parent;
            }
        }

        /// <summary>
        /// Restores the ModelLocator to its original state.
        /// </summary>
        public void RestoreOriginalState()
        {
            if (_modelLocator != null && _modelLocator.modelTransform != null)
            {
                // First restore the parent relationship
                _modelLocator.modelTransform.SetParent(originalModelParent, false);

                // Root model transforms should keep their thrown position, while child models can be repositioned
                if (_modelLocator.modelTransform != gameObject.transform)
                {
                    // Then restore the initial transform values on the modelTransform
                    _modelLocator.modelTransform.localPosition = originalInitialPosition;
                    _modelLocator.modelTransform.localRotation = originalInitialRotation;
                    _modelLocator.modelTransform.localScale = originalInitialScale;
                }

                // Finally restore autoUpdateModelTransform to false if it was originally false
                if (!originalAutoUpdateModelTransform)
                {
                    _modelLocator.autoUpdateModelTransform = false;
                }
            }
        }
    }
}