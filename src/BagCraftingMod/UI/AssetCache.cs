using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using RoR2;
using BagCraftingMod.Config;

namespace BagCraftingMod.UI
{
    public static class AssetCache
    {
        private static readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();

        public static Sprite? GetIcon(string key)
        {
            if (_iconCache.TryGetValue(key, out var cachedSprite)) return cachedSprite;

            var assetPaths = PluginConfig.Instance.GetIconAssetPaths();
            if (assetPaths.TryGetValue(key, out var rawPath))
            {
                Log.Debug($"[AssetCache] Raw config path for {key}: {rawPath}");
                var paths = rawPath.Split('|');
                foreach (var path in paths)
                {
                    Log.Info($"[AssetCache] Trying icon path for {key}: {path}");
                    var iconSprite = LoadSpriteInternal(key, path);
                    if (iconSprite != null)
                    {
                        bool isMystery = iconSprite.name != null && iconSprite.name.IndexOf("Mystery", StringComparison.OrdinalIgnoreCase) >= 0;
                        
                        if (isMystery)
                        {
                            Log.Info($"[AssetCache] Skipping mystery icon ({iconSprite.name}) from {path}, looking for better one in pipe...");
                            continue; 
                        }
                        
                        Log.Info($"[AssetCache] Successfully resolved icon for {key} from {path} (Sprite: {iconSprite.name})");
                        _iconCache[key] = iconSprite;
                        return iconSprite;
                    }
                }
            }
            else
            {
                Log.Debug($"[AssetCache] No config mapping found for icon key: {key}");
            }

            return null;
        }

        private static Sprite? LoadSpriteInternal(string key, string path)
        {
            Log.Info($"[AssetCache] Loading asset for {key} at: {path}");
            try
            {
                var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(path);
                var asset = handle.WaitForCompletion();
                if (asset == null) return null;

                if (asset is Texture tex) return TextureToSprite(tex);
                if (asset is Sprite s) return s;
                
                if (asset is GameObject go) return GetIconFromPrefab(key, go);
                
                SpawnCard? sc = asset as SpawnCard;
                if (sc != null && sc.prefab) return GetIconFromPrefab(key, sc.prefab);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[AssetCache] Failed to load {path}: {ex.Message}");
            }
            return null;
        }

        private static Sprite? GetIconFromPrefab(string key, GameObject prefab)
        {
            var soa = prefab.GetComponent<SpecialObjectAttributes>();
            if (soa == null)
            {
                Log.Debug($"[AssetCache] Prefab {prefab.name} does NOT have SpecialObjectAttributes component.");
            }
            else
            {
                Log.Debug($"[AssetCache] Prefab {prefab.name} has SOA. portraitIcon: {(soa.portraitIcon ? soa.portraitIcon.name : "NULL")}");
                if (soa.portraitIcon != null)
                {
                    // Accept it unless it's explicitly mystery
                    if (soa.portraitIcon.name != null && soa.portraitIcon.name.Contains("Mystery"))
                    {
                        Log.Debug($"[AssetCache] Prefab {prefab.name} has mystery icon on SOA, forcing fallback.");
                        return null; 
                    }
                    return TextureToSprite(soa.portraitIcon);
                }
            }

            // Try fallback map
            string lowerKey = key.ToLowerInvariant();
            string fallbackPath = DrifterBossGrabMod.Patches.GrabbableObjectPatches.GetIconPathForObject(lowerKey);
            
            if (!string.IsNullOrEmpty(fallbackPath))
            {
                if (fallbackPath.Contains("Mystery"))
                {
                    Log.Debug($"[AssetCache] Fallback path for {key} is mystery, skipping to allow pipe fallback.");
                    return null;
                }

                Log.Info($"[AssetCache] Falling back to hardcoded map for {key}: {fallbackPath}");
                var fallbackTex = Addressables.LoadAssetAsync<Texture>(fallbackPath).WaitForCompletion();
                if (fallbackTex) return TextureToSprite(fallbackTex);
            }

            return null;
        }

        public static GameObject? GetPrefab(string key)
        {
            if (_prefabCache.TryGetValue(key, out var cachedPrefab)) return cachedPrefab;

            var assetPaths = PluginConfig.Instance.GetIconAssetPaths();
            if (assetPaths.TryGetValue(key, out var rawPath))
            {
                var paths = rawPath.Split('|');
                foreach (var path in paths)
                {
                    Log.Info($"[AssetCache] Trying prefab path for {key}: {path}");
                    try
                    {
                        var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(path);
                        var asset = handle.WaitForCompletion();
                        if (asset == null) continue;

                        GameObject? prefab = null;
                        if (asset is GameObject go) 
                        {
                            prefab = go;
                        }
                        else 
                        {
                            SpawnCard? sc = asset as SpawnCard;
                            if (sc != null) prefab = sc.prefab;
                        }

                        if (prefab != null)
                        {
                            Log.Info($"[AssetCache] Successfully cached prefab for {key}");
                            _prefabCache[key] = prefab;
                            return prefab;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[AssetCache] Failed to load {path}: {ex.Message}");
                    }
                }
            }

            return null;
        }

        public static void CacheIcon(string key, Sprite sprite)
        {
            if (sprite) _iconCache[key] = sprite;
        }

        private static Sprite? TextureToSprite(Texture? tex)
        {
            if (!tex) return null;
            if (tex is Texture2D tex2d)
            {
                var sprite = Sprite.Create(tex2d, new Rect(0, 0, tex2d.width, tex2d.height), new Vector2(0.5f, 0.5f));
                sprite.name = tex.name;
                return sprite;
            }
            return null;
        }
    }
}
