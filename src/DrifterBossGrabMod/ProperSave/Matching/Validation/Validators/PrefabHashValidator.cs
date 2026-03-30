#nullable enable
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.ProperSave.Core;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Matching;
using DrifterBossGrabMod.ProperSave;

namespace DrifterBossGrabMod.ProperSave.Matching.Validators
{
    // Validator using NetworkIdentity.assetId (NetworkHash128) for high-confidence prefab matching
    public class PrefabHashValidator : IObjectValidator
    {
        public int Priority => 95;

        public ValidationResult Validate(GameObject obj, BaggedObjectSaveData saveData)
        {
            var prefabHash = ParsePrefabHash(saveData.PrefabHash);

            // Return no match if no prefab hash available
            if (prefabHash.Equals(default))
            {
                return NoMatch;
            }

            // Get NetworkIdentity to access assetId
            NetworkIdentity? networkIdentity = obj.GetComponent<NetworkIdentity>();
            if (networkIdentity == null)
            {
                return NoMatch;
            }

            // Compare NetworkIdentity.assetId with saved PrefabHash
            if (networkIdentity.assetId.Equals(prefabHash))
            {
                return new ValidationResult
                {
                    Confidence = ProperSaveConstants.Validation.ExactPrefabHashConfidence,
                    Method = MatchMethod.ExactPrefabHash,
                    Reason = "Exact PrefabHash match"
                };
            }

            return NoMatch;
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

        // Helper for no-match result
        private static ValidationResult NoMatch => new()
        {
            Confidence = 0.0f,
            Method = MatchMethod.None,
            Reason = string.Empty
        };
    }
}
