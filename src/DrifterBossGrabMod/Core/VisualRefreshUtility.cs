using RoR2;
using UnityEngine;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Core
{
    // Utility for refreshing the visual state of objects to resolve rendering artifacts
    public static class VisualRefreshUtility
    {
        public static void Refresh(GameObject target)
        {
            if (target == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[VisualRefreshUtility] Starting refresh for {target.name}");

            try
            {
                // 1. Determine the model transform
                var modelLocator = target.GetComponent<ModelLocator>();
                Transform modelTransform = (modelLocator != null && modelLocator.modelTransform != null) 
                    ? modelLocator.modelTransform 
                    : target.transform;

                // 2. Force a renderer refresh by briefly toggling activation
                if (modelTransform.gameObject.activeSelf)
                {
                    modelTransform.gameObject.SetActive(false);
                    modelTransform.gameObject.SetActive(true);
                }

                // 3. Refresh CharacterModel if present
                var characterModel = modelTransform.GetComponent<CharacterModel>();
                if (characterModel != null)
                {
                    characterModel.UpdateMaterials();
                }

                // 4. Generic material fix
                FixBrokenMaterials(target, modelTransform);

                // 5. Cleanup stuck visual components
                CleanupStuckVisualEffects(target);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[VisualRefreshUtility] Error during refresh of {target.name}: {ex.Message}");
            }
        }

        private static void FixBrokenMaterials(GameObject target, Transform modelTransform)
        {
            var allRenderers = modelTransform.GetComponentsInChildren<Renderer>(true);
            Material? fallbackMaterial = null;

            // Search for a valid, non-error material to use as a template for broken ones
            foreach (var r in allRenderers)
            {
                if (r.sharedMaterials == null) continue;
                foreach (var mat in r.sharedMaterials)
                {
                    if (IsValidMaterial(mat))
                    {
                        fallbackMaterial = mat;
                        break;
                    }
                }
                if (fallbackMaterial != null) break;
            }

            if (fallbackMaterial == null) return;

            int fixedCount = 0;
            foreach (var r in allRenderers)
            {
                bool needsUpdate = false;
                var sharedMats = r.sharedMaterials;
                
                if (sharedMats == null || sharedMats.Length == 0)
                {
                    r.sharedMaterials = new Material[] { fallbackMaterial };
                    fixedCount++;
                    continue;
                }

                // Check for nulls or error shaders
                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (!IsValidMaterial(sharedMats[i]))
                    {
                        sharedMats[i] = fallbackMaterial;
                        needsUpdate = true;
                    }
                }

                if (needsUpdate)
                {
                    r.sharedMaterials = sharedMats;
                    // Force the renderer to update its materials array
                    r.materials = sharedMats;
                    fixedCount++;
                }
            }

            if (fixedCount > 0 && PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[VisualRefreshUtility] Fixed {fixedCount} broken renderer(s) on {target.name}");
        }

        private static bool IsValidMaterial(Material? mat)
        {
            if (mat == null) return false;
            if (mat.shader == null) return false;
            
            string shaderName = mat.shader.name;
            // Detect common Unity error shaders
            return !shaderName.Contains("InternalError") && 
                   !shaderName.Contains("Error") && 
                   shaderName != "Hidden/InternalErrorShader";
        }

        private static void CleanupStuckVisualEffects(GameObject target)
        {
            // PrintControllers and DitherModel can leave objects invisible
            var printControllers = target.GetComponentsInChildren<PrintController>(true);
            foreach (var pc in printControllers)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[VisualRefreshUtility] Removing stuck PrintController from {target.name}");
                Object.Destroy(pc);
            }

            var ditherModels = target.GetComponentsInChildren<DitherModel>(true);
            foreach (var dm in ditherModels)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[VisualRefreshUtility] Removing stuck DitherModel from {target.name}");
                Object.Destroy(dm);
            }
        }
    }
}
