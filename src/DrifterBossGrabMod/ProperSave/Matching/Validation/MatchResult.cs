using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod.ProperSave.Matching
{
    // Result of a match operation with confidence metrics
    public class MatchResult
    {
        // The matched GameObject reference
        public GameObject? MatchedObject { get; set; }

        // Method used to achieve match
        public MatchMethod MatchMethod { get; set; }

        // Confidence level of match (0.0 to 1.0)
        public float ConfidenceScore { get; set; }

        // Distance between expected and actual position in world units
        public float PositionDistance { get; set; }

        // Human-readable explanation of the match result
        public string MatchReason { get; set; } = string.Empty;

        // List of validation errors encountered during matching
        public List<string> ValidationErrors { get; set; }

        // Initialize with empty validation errors list
        public MatchResult()
        {
            ValidationErrors = new List<string>();
        }
    }
}
