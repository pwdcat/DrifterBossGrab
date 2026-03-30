#nullable enable
using System;
using UnityEngine;
using DrifterBossGrabMod.ProperSave.Data;

namespace DrifterBossGrabMod.ProperSave.Matching
{
    // Validation result with confidence scoring
    public class ValidationResult
    {
        // Match confidence from 0.0 to 1.0
        public float Confidence { get; set; }

        // Method used to determine match
        public MatchMethod Method { get; set; }

        // Human-readable explanation of match
        public string Reason { get; set; } = string.Empty;

        // Whether this is a valid match (any positive confidence)
        public bool IsMatch => Confidence > 0.0f;
    }

    // Methods for matching objects
    public enum MatchMethod
    {
        ExactAssetId,           // 1.0 - Perfect match by GUID
        ExactPrefabHash,        // 0.95 - Nearly perfect by prefab hash
        ComponentTypeAndPosition, // 0.8-0.9 - High confidence
        PrefabNameAndPosition,  // 0.7-0.8 - Good confidence
        Fallback,               // 0.5-0.7 - Low confidence
        None                    // 0.0 - No match
    }

    // Interface for object validation strategies
    public interface IObjectValidator
    {
        // Higher priority validators run first
        int Priority { get; }

        // Validate object against save data, returning confidence score
        ValidationResult Validate(GameObject obj, BaggedObjectSaveData saveData);
    }
}
