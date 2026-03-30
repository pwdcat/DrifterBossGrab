#nullable enable
using System;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.ProperSave.Core;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Matching;
using DrifterBossGrabMod.ProperSave;

namespace DrifterBossGrabMod.ProperSave.Matching.Validators
{
    public class AssetIdValidator : IObjectValidator
    {
        public int Priority => 100;

        public ValidationResult Validate(GameObject obj, BaggedObjectSaveData saveData)
        {
            var assetId = ParseGuid(saveData.AssetId);

            // Return no match if AssetId is not available
            if (assetId == null || assetId == Guid.Empty)
                return NoMatch();

            // Get NetworkIdentity to access AssetId
            var networkIdentity = obj.GetComponent<NetworkIdentity>();
            if (networkIdentity == null)
                return NoMatch();

            // Convert networkIdentity AssetId to Guid and compare
            var objAssetId = new Guid(networkIdentity.assetId.ToString());
            if (objAssetId == assetId)
            {
                return new ValidationResult
                {
                    Confidence = ProperSaveConstants.Validation.ExactAssetIdConfidence,
                    Method = MatchMethod.ExactAssetId,
                    Reason = $"Exact AssetId match: {assetId}"
                };
            }

            return NoMatch();
        }

        private static Guid? ParseGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Guid.TryParse(s, out var guid))
                return guid;
            return null;
        }

        // Helper method for no match result
        private static ValidationResult NoMatch()
        {
            return new ValidationResult
            {
                Confidence = 0.0f,
                Method = MatchMethod.None,
                Reason = string.Empty
            };
        }
    }
}
