#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.ProperSave.Core;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Matching;

namespace DrifterBossGrabMod.ProperSave.Matching
{
    public static class ObjectFinder
    {
        // Find the best matching object to restore from save data
        // Returns MatchResult with confidence metrics
        public static MatchResult FindObjectToRestore(BaggedObjectSaveData saveData)
        {
            var candidates = FindAllCandidates(saveData);
            if (candidates.Count == 0) return NoMatch();

            var bestMatch = candidates
                .Select(obj => ObjectValidator.ValidateMatch(obj, saveData))
                .Where(r => r.ValidationErrors.Count == 0)
                .OrderByDescending(r => r.ConfidenceScore)
                .ThenBy(r => r.PositionDistance)
                .FirstOrDefault();

            if (bestMatch != null && bestMatch.ConfidenceScore >= ObjectValidator.MIN_CONFIDENCE_THRESHOLD)
            {
                Log.Info($"[ObjectFind] Found match: {bestMatch.MatchReason} (confidence: {bestMatch.ConfidenceScore:F2})");
                return bestMatch;
            }

            Log.Warning($"[ObjectFind] No valid match found (checked {candidates.Count} candidates)");
            return NoMatch();
        }

        // Find all potential matching objects using exact match methods only
        private static List<GameObject> FindAllCandidates(BaggedObjectSaveData saveData)
        {
            var candidates = new List<GameObject>();

            var assetId = ParseGuid(saveData.AssetId);
            var prefabHash = ParsePrefabHash(saveData.PrefabHash);

            // Find by AssetId
            if (assetId.HasValue && assetId.Value != Guid.Empty)
            {
                var byAssetId = FindByAssetIdExact(assetId.Value);
                if (byAssetId != null) candidates.Add(byAssetId);
            }

            // Find by Prefab Hash (exact hash comparison)
            if (!prefabHash.Equals(default))
            {
                var byHash = FindByPrefabHashExact(prefabHash);
                if (byHash != null && !candidates.Contains(byHash)) candidates.Add(byHash);
            }

            // Find by Component Type + Position
            if (!string.IsNullOrEmpty(saveData.ComponentType))
            {
                var byComponent = FindByComponentTypeAndPosition(saveData);
                candidates.AddRange(byComponent.Where(c => !candidates.Contains(c)));
            }

            // Remove duplicates by instance ID
            return candidates.GroupBy(o => o.GetInstanceID()).Select(g => g.First()).ToList();
        }

        // Find object by exact AssetId comparison
        // Direct GUID comparison on NetworkIdentity.assetId
        private static GameObject? FindByAssetIdExact(Guid assetId)
        {
            var allNetworkIdentities = UnityEngine.Object.FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None);

            foreach (var networkIdentity in allNetworkIdentities)
            {
                if (networkIdentity?.gameObject == null) continue;

                var objAssetId = new Guid(networkIdentity.assetId.ToString());
                if (objAssetId == assetId)
                {
                    return networkIdentity.gameObject;
                }
            }

            return null;
        }

        // Find object by exact PrefabHash comparison
        private static GameObject? FindByPrefabHashExact(NetworkHash128 prefabHash)
        {
            var allNetworkIdentities = UnityEngine.Object.FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None);

            foreach (var networkIdentity in allNetworkIdentities)
            {
                if (networkIdentity?.gameObject == null) continue;

                if (networkIdentity.assetId.Equals(prefabHash))
                {
                    return networkIdentity.gameObject;
                }
            }

            return null;
        }

        // Find objects with specific component type within position tolerance
        // Uses exact component type matching
        private static List<GameObject> FindByComponentTypeAndPosition(BaggedObjectSaveData saveData)
        {
            var componentType = Type.GetType(saveData.ComponentType);
            if (componentType == null) return new List<GameObject>();

            // Get all objects with the required component
            var components = UnityEngine.Object.FindObjectsByType(componentType, FindObjectsSortMode.None) as Component[];
            if (components == null) return new List<GameObject>();

            var savedPosition = ParseVector3(saveData.Position);

            // Filter by position tolerance
            var results = new List<GameObject>();
            foreach (var comp in components)
            {
                if (comp?.gameObject == null) continue;

                var distance = Vector3.Distance(comp.gameObject.transform.position, savedPosition);
                if (distance <= ObjectValidator.POSITION_TOLERANCE)
                {
                    results.Add(comp.gameObject);
                }
            }

            return results;
        }

        private static Vector3 ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector3.zero;
            var parts = s.Split('|');
            if (parts.Length != 3) return Vector3.zero;
            float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
            return new Vector3(x, y, z);
        }

        private static NetworkHash128 ParsePrefabHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            try
            {
                return NetworkHash128.Parse(s);
            }
            catch
            {
                return default;
            }
        }

        private static Guid? ParseGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Guid.TryParse(s, out var guid))
                return guid;
            return null;
        }

        // Create a MatchResult indicating no match found
        private static MatchResult NoMatch()
        {
            return new MatchResult
            {
                MatchedObject = null,
                ConfidenceScore = 0.0f,
                MatchMethod = MatchMethod.None,
                ValidationErrors = new List<string> { "No match found" }
            };
        }
    }
}
