#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RoR2;

namespace DrifterBossGrabMod.Balance
{

    public static class FormulaRegistry
    {
        private static readonly ConcurrentDictionary<string, float> _staticVariables = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Func<CharacterBody?, float>> _dynamicProviders = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, VariableInfo> _variableInfo = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _eventLock = new object();

        private static readonly HashSet<string> _reservedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {

            "FLOOR", "CEIL", "ROUND", "ABS", "SQRT", "LOG", "LN", "MIN", "MAX", "CLAMP",
            "SIN", "COS", "TAN", "SIGN", "POW",

            "PI", "E", "INF", "INFINITY",

            "AND", "OR", "NOT", "XOR"
        };

        private const int MaxVariableNameLength = 50;

        public static event Action<string>? OnVariableRegistered;

        public static event Action<string>? OnVariableUnregistered;

        public static void RegisterVariable(string name, float value, string? description = null)
        {
            string normalizedName = NormalizeVariableName(name);

            if (_staticVariables.ContainsKey(normalizedName) || _dynamicProviders.ContainsKey(normalizedName))
            {
                Log.Warning($"[FormulaRegistry] Variable '{normalizedName}' is already registered. Overwriting.");
            }

            _staticVariables[normalizedName] = value;
            _dynamicProviders.TryRemove(normalizedName, out _);
            _variableInfo[normalizedName] = new VariableInfo(normalizedName, VariableType.Static, description);

            Action<string>? handler;
            lock (_eventLock)
            {
                handler = OnVariableRegistered;
            }
            handler?.Invoke(normalizedName);
        }

        public static bool RegisterVariableSafe(string name, float value, string? description = null, bool overwrite = false)
        {
            string normalizedName = NormalizeVariableName(name);

            bool exists = _staticVariables.ContainsKey(normalizedName) || _dynamicProviders.ContainsKey(normalizedName);
            if (exists && !overwrite)
            {
                Log.Warning($"[FormulaRegistry] Variable '{normalizedName}' already registered. Use overwrite: true to replace.");
                return false;
            }

            if (exists)
            {
                Log.Warning($"[FormulaRegistry] Variable '{normalizedName}' is already registered. Overwriting.");
            }

            _staticVariables[normalizedName] = value;
            _dynamicProviders.TryRemove(normalizedName, out _);
            _variableInfo[normalizedName] = new VariableInfo(normalizedName, VariableType.Static, description);

            Action<string>? handler;
            lock (_eventLock)
            {
                handler = OnVariableRegistered;
            }
            handler?.Invoke(normalizedName);
            return true;
        }

        public static void RegisterVariable(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null)
        {
            string normalizedName = NormalizeVariableName(name);

            if (_staticVariables.ContainsKey(normalizedName) || _dynamicProviders.ContainsKey(normalizedName))
            {
                Log.Warning($"[FormulaRegistry] Variable '{normalizedName}' is already registered. Overwriting.");
            }

            _dynamicProviders[normalizedName] = provider;
            _staticVariables.TryRemove(normalizedName, out _);
            _variableInfo[normalizedName] = new VariableInfo(normalizedName, VariableType.Dynamic, description, fallbackValue);

            Action<string>? handler;
            lock (_eventLock)
            {
                handler = OnVariableRegistered;
            }
            handler?.Invoke(normalizedName);
        }

        public static bool RegisterVariableSafe(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null, bool overwrite = false)
        {
            string normalizedName = NormalizeVariableName(name);

            bool exists = _staticVariables.ContainsKey(normalizedName) || _dynamicProviders.ContainsKey(normalizedName);
            if (exists && !overwrite)
            {
                Log.Warning($"[FormulaRegistry] Variable '{normalizedName}' already registered. Use overwrite: true to replace.");
                return false;
            }

            if (exists)
            {
                Log.Warning($"[FormulaRegistry] Variable '{normalizedName}' is already registered. Overwriting.");
            }

            _dynamicProviders[normalizedName] = provider;
            _staticVariables.TryRemove(normalizedName, out _);
            _variableInfo[normalizedName] = new VariableInfo(normalizedName, VariableType.Dynamic, description, fallbackValue);

            Action<string>? handler;
            lock (_eventLock)
            {
                handler = OnVariableRegistered;
            }
            handler?.Invoke(normalizedName);
            return true;
        }

        public static bool UnregisterVariable(string name)
        {
            string normalizedName = NormalizeVariableName(name);
            bool removed = _staticVariables.TryRemove(normalizedName, out _) || _dynamicProviders.TryRemove(normalizedName, out _);
            _variableInfo.TryRemove(normalizedName, out _);

            if (removed)
            {

                Action<string>? handler;
                lock (_eventLock)
                {
                    handler = OnVariableUnregistered;
                }
                handler?.Invoke(normalizedName);
            }

            return removed;
        }

        public static bool IsVariableRegistered(string name)
        {
            string normalizedName = NormalizeVariableName(name);
            return _staticVariables.ContainsKey(normalizedName) || _dynamicProviders.ContainsKey(normalizedName);
        }

        public static VariableInfo? GetVariableInfo(string name)
        {
            string normalizedName = NormalizeVariableName(name);
            return _variableInfo.TryGetValue(normalizedName, out var info) ? info : null;
        }

        public static IEnumerable<string> GetRegisteredVariableNames()
        {
            return _staticVariables.Keys.Concat(_dynamicProviders.Keys).Distinct();
        }

        public static Dictionary<string, float> GetVariables(CharacterBody? body, Dictionary<string, float>? localVars = null)
        {
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _staticVariables)
            {
                result[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in _dynamicProviders)
            {
                try
                {
                    result[kvp.Key] = kvp.Value(body);
                }
                catch (Exception ex)
                {
                    Log.Error($"[FormulaRegistry] Error evaluating dynamic variable '{kvp.Key}': {ex.Message}");

                    float fallbackValue = 0f;
                    if (_variableInfo.TryGetValue(kvp.Key, out var info) && info.FallbackValue.HasValue)
                    {
                        fallbackValue = info.FallbackValue.Value;
                    }
                    result[kvp.Key] = fallbackValue;
                }
            }

            if (localVars != null)
            {
                foreach (var kvp in localVars)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }

        private static string NormalizeVariableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Variable name cannot be null or whitespace", nameof(name));

            string trimmedName = name.Trim();
            string upperName = trimmedName.ToUpperInvariant();

            if (upperName.Length > MaxVariableNameLength)
                throw new ArgumentException($"Variable name cannot exceed {MaxVariableNameLength} characters", nameof(name));

            if (_reservedKeywords.Contains(upperName))
                throw new ArgumentException($"Variable name '{name}' is a reserved keyword and cannot be used", nameof(name));

            foreach (char c in upperName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    throw new ArgumentException($"Variable name '{name}' contains invalid character '{c}'. Only letters, numbers, and underscores are allowed.", nameof(name));
                }
            }

            if (!char.IsLetter(upperName[0]) && upperName[0] != '_')
            {
                throw new ArgumentException($"Variable name '{name}' must start with a letter or underscore", nameof(name));
            }

            return upperName;
        }

    }

    public class VariableInfo
    {
        public string Name { get; }
        public VariableType Type { get; }
        public string? Description { get; }
        public float? FallbackValue { get; }

        public VariableInfo(string name, VariableType type, string? description, float? fallbackValue = null)
        {
            Name = name;
            Type = type;
            Description = description;
            FallbackValue = fallbackValue;
        }
    }

    public enum VariableType
    {
        Static,
        Dynamic
    }
}
