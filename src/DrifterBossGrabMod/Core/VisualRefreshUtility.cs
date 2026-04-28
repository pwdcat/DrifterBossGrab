using RoR2;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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

            foreach (var r in allRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                var sharedMats = r.sharedMaterials;
                if (sharedMats == null || sharedMats.Length == 0) continue;

                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] == null || !IsValidMaterial(sharedMats[i]))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            string matName = sharedMats[i] == null ? "NULL" : sharedMats[i]!.name;
                            Log.Info($"[VisualRefreshUtility] Renderer '{r.name}' on {target.name} has invalid material at slot {i} (Mat: {matName}). Disabling renderer.");
                        }

                        r.enabled = false;
                        break;
                    }
                }
            }
        }

        private static bool IsValidMaterial(Material? mat)
        {
            if (mat == null) return false;
            if (mat.shader == null) return false;

            string shaderName = mat.shader.name;
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
