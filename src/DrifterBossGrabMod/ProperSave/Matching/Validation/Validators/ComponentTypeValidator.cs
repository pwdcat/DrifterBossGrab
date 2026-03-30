#nullable enable
using System;
using UnityEngine;
using DrifterBossGrabMod.ProperSave.Core;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave;

namespace DrifterBossGrabMod.ProperSave.Matching.Validators
{
    using DrifterBossGrabMod.ProperSave.Matching;

    public class ComponentTypeValidator : IObjectValidator
    {
        public int Priority => 80;

        public ValidationResult Validate(GameObject obj, BaggedObjectSaveData saveData)
        {
            if (string.IsNullOrEmpty(saveData.ComponentType))
                return NoMatch();

            var componentType = Type.GetType(saveData.ComponentType);
            if (componentType == null)
                return NoMatch();

            var component = obj.GetComponent(componentType);
            if (component == null)
                return NoMatch();

            var savedPosition = ParseVector3(saveData.Position);
            var distance = Vector3.Distance(obj.transform.position, savedPosition);

            if (distance <= ObjectValidator.POSITION_TOLERANCE)
            {
                var confidence = 1.0f - (distance / ObjectValidator.POSITION_TOLERANCE) * ProperSaveConstants.Validation.ComponentTypeMatchWeight;

                return new ValidationResult
                {
                    Confidence = confidence,
                    Method = MatchMethod.ComponentTypeAndPosition,
                    Reason = $"Component type '{componentType.Name}' match at {distance:F2}m"
                };
            }

            return NoMatch();
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
