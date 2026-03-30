#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.ProperSave.Core;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Matching.Validators;

namespace DrifterBossGrabMod.ProperSave.Matching
{
    // Shared constants and utilities for object validation
    public static class ObjectValidator
    {
        // Maximum allowed distance between saved and current position (in world units)
        public const float POSITION_TOLERANCE = 10.0f;

        // Minimum confidence threshold for accepting a match
        public const float MIN_CONFIDENCE_THRESHOLD = 0.75f;

        // Validate an object against save data, returning match result with confidence
        public static MatchResult ValidateMatch(GameObject obj, BaggedObjectSaveData saveData)
        {
            var result = new MatchResult { MatchedObject = obj };
            var validators = GetValidators();

            foreach (var validator in validators)
            {
                var validation = validator.Validate(obj, saveData);
                if (validation.Confidence > result.ConfidenceScore)
                {
                    result.ConfidenceScore = validation.Confidence;
                    result.MatchMethod = validation.Method;
                    result.MatchReason = validation.Reason;
                }
            }

            var savedPosition = ParseVector3(saveData.Position);
            result.PositionDistance = Vector3.Distance(obj.transform.position, savedPosition);

            if (result.ConfidenceScore < MIN_CONFIDENCE_THRESHOLD)
            {
                result.ValidationErrors.Add($"Confidence {result.ConfidenceScore:F2} below threshold {MIN_CONFIDENCE_THRESHOLD}");
            }

            if (result.PositionDistance > POSITION_TOLERANCE)
            {
                result.ValidationErrors.Add($"Position distance {result.PositionDistance:F2} exceeds tolerance {POSITION_TOLERANCE}");
            }

            return result;
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

        // Get all available validators in priority order
        private static List<IObjectValidator> GetValidators()
        {
            return new List<IObjectValidator>
            {
                new AssetIdValidator(),
                new PrefabHashValidator(),
                new ComponentTypeValidator(),
                new PositionValidator()
            };
        }
    }
}
