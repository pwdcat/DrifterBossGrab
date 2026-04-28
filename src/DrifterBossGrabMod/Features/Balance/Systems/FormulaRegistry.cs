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
    // central registry for formula variables supporting static values and dynamic providers
    public static class FormulaRegistry
    {
        private static readonly ConcurrentDictionary<string, float> _staticVariables = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Func<CharacterBody?, float>> _dynamicProviders = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, VariableInfo> _variableInfo = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _eventLock = new object();

        // Reserved keywords that cannot be used as variable names
        private static readonly HashSet<string> _reservedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Functions
            "FLOOR", "CEIL", "ROUND", "ABS", "SQRT", "LOG", "LN", "MIN", "MAX", "CLAMP",
            "SIN", "COS", "TAN", "SIGN", "POW",
            // Constants
            "PI", "E", "INF", "INFINITY",
            // Operators (for safety)
            "AND", "OR", "NOT", "XOR"
        };

        private const int MaxVariableNameLength = 50;

        // fired when variable is registered
        // variableName: name of variable that was registered
        public static event Action<string>? OnVariableRegistered;

        // fired when variable is unregistered
        // variableName: name of variable that was unregistered
        public static event Action<string>? OnVariableUnregistered;

        // register static variable with constant value
        // name: variable name case-insensitive
        // value: constant value
        // description: optional info
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

            // Thread-safe event invocation
            Action<string>? handler;
            lock (_eventLock)
            {
                handler = OnVariableRegistered;
            }
            handler?.Invoke(normalizedName);
        }

        // register dynamic variable provider evaluated when needed
        // name: variable name case-insensitive
        // provider: function returning value given CharacterBody
        // description: optional info
        // fallbackValue: value if provider throws
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

            // Thread-safe event invocation
            Action<string>? handler;
            lock (_eventLock)
            {
                handler = OnVariableRegistered;
            }
            handler?.Invoke(normalizedName);
        }

        // unregister variable by name
        // name: variable name case-insensitive
        // returns true if found and removed
        public static bool UnregisterVariable(string name)
        {
            string normalizedName = NormalizeVariableName(name);
            bool removed = _staticVariables.TryRemove(normalizedName, out _) || _dynamicProviders.TryRemove(normalizedName, out _);
            _variableInfo.TryRemove(normalizedName, out _);

            if (removed)
            {
                // Thread-safe event invocation
                Action<string>? handler;
                lock (_eventLock)
                {
                    handler = OnVariableUnregistered;
                }
                handler?.Invoke(normalizedName);
            }

            return removed;
        }

        // check if variable is registered
        // name: variable name case-insensitive
        // returns true if registered
        public static bool IsVariableRegistered(string name)
        {
            string normalizedName = NormalizeVariableName(name);
            return _staticVariables.ContainsKey(normalizedName) || _dynamicProviders.ContainsKey(normalizedName);
        }

        // get variable metadata including type description source mod and registration time
        // name: variable name case-insensitive
        // returns VariableInfo if registered else null
        public static VariableInfo? GetVariableInfo(string name)
        {
            string normalizedName = NormalizeVariableName(name);
            return _variableInfo.TryGetValue(normalizedName, out var info) ? info : null;
        }

        // get all registered variable names
        public static IEnumerable<string> GetRegisteredVariableNames()
        {
            return _staticVariables.Keys.Concat(_dynamicProviders.Keys).Distinct();
        }

        // get all variables for character body including static dynamic and local
        // body: character body for dynamic variable evaluation
        // localVars: optional overrides with highest priority
        // returns dictionary of all variables and values
        public static Dictionary<string, float> GetVariables(CharacterBody? body, Dictionary<string, float>? localVars = null)
        {
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            // Add static variables
            foreach (var kvp in _staticVariables)
            {
                result[kvp.Key] = kvp.Value;
            }

            // Add dynamic variables
            foreach (var kvp in _dynamicProviders)
            {
                try
                {
                    result[kvp.Key] = kvp.Value(body);
                }
                catch (Exception ex)
                {
                    Log.Error($"[FormulaRegistry] Error evaluating dynamic variable '{kvp.Key}': {ex.Message}");
                    // Use fallback value if available, otherwise default to 0f
                    float fallbackValue = 0f;
                    if (_variableInfo.TryGetValue(kvp.Key, out var info) && info.FallbackValue.HasValue)
                    {
                        fallbackValue = info.FallbackValue.Value;
                    }
                    result[kvp.Key] = fallbackValue;
                }
            }

            // Add local variables (highest priority)
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

            // Check length limit
            if (upperName.Length > MaxVariableNameLength)
                throw new ArgumentException($"Variable name cannot exceed {MaxVariableNameLength} characters", nameof(name));

            // Check for reserved keywords
            if (_reservedKeywords.Contains(upperName))
                throw new ArgumentException($"Variable name '{name}' is a reserved keyword and cannot be used", nameof(name));

            // Check for valid characters (only letters, numbers, and underscores allowed)
            foreach (char c in upperName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    throw new ArgumentException($"Variable name '{name}' contains invalid character '{c}'. Only letters, numbers, and underscores are allowed.", nameof(name));
                }
            }

            // Check that name starts with a letter or underscore
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
