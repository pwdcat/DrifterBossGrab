#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoR2;
using EntityStates;

namespace DrifterBossGrabMod.ProperSave.Serializers
{
    // Declarative serializer for component fields
    // TComponent: The component type to serialize
    public class ComponentFieldMap<TComponent> : IObjectSerializerPlugin where TComponent : Component
    {
        private readonly int _priority;
        private readonly List<FieldMapping> _fields = new List<FieldMapping>();
        private bool _includePurchaseInteraction;
        private bool _includeHealthComponent;
        private bool _includeEntityStateMachine;

        public int Priority => _priority;

        public string PluginName => $"ComponentFieldMap<{typeof(TComponent).Name}>";

        private Action<TComponent, Dictionary<string, object>>? _capturePurchaseInteraction;
        private Action<TComponent, Dictionary<string, object>>? _captureHealthComponent;
        private Action<TComponent, Dictionary<string, object>>? _captureEntityStateMachine;
        private Action<TComponent, Dictionary<string, object>>? _restorePurchaseInteraction;
        private Action<TComponent, Dictionary<string, object>>? _restoreHealthComponent;
        private Action<TComponent, Dictionary<string, object>>? _restoreEntityStateMachine;

        public ComponentFieldMap(int priority)
        {
            _priority = priority;
        }

        // Register a simple field for serialization
        public ComponentFieldMap<TComponent> Field<TField>(
            Func<TComponent, TField> getter,
            string key,  // Make key REQUIRED, remove default null
            Action<TComponent, TField>? restore = null,
            bool asInt = false)
        {
            var fieldKey = key;  // Use the explicit key instead of extracting from lambda
            _fields.Add(new FieldMapping
            {
                Key = fieldKey,
                Getter = c => getter(c),
                Restore = (c, value) =>
                {
                    if (restore != null)
                    {
                        var convertedValue = SafeConvert<TField>(value);
                        if (convertedValue != null)
                        {
                            restore(c, convertedValue);
                        }
                    }
                },
                AsInt = asInt
            });

            return this;
        }

        // Register a UniquePickup field with decay value serialization
        public ComponentFieldMap<TComponent> UniquePickup(
            Func<TComponent, UniquePickup> getter,
            string key = "pickup",
            Action<TComponent, UniquePickup>? restore = null)
        {
            _fields.Add(new FieldMapping
            {
                Key = key + "Index",
                Getter = c => getter(c).isValid ? getter(c).pickupIndex.ToString() : string.Empty,
                Restore = (c, value) =>
                {
                    var pickupIndexStr = value?.ToString();
                    if (!string.IsNullOrEmpty(pickupIndexStr))
                    {
                        var pickupIndex = PickupCatalog.FindPickupIndex(pickupIndexStr);
                        var decayField = _fields.FirstOrDefault(f => f.Key == key + "DecayValue");
                        if (pickupIndex != PickupIndex.none && decayField != null)
                        {
                            var decayValue = SafeConvertToUInt32(decayField.LastCapturedValue);
                            restore?.Invoke(c, new UniquePickup
                            {
                                pickupIndex = pickupIndex,
                                decayValue = decayValue
                            });
                        }
                    }
                }
            });

            _fields.Add(new FieldMapping
            {
                Key = key + "DecayValue",
                Getter = c => getter(c).isValid ? getter(c).decayValue : 0u,
                Restore = null, // Restored with the pickup index
                AsInt = false
            });

            return this;
        }

        // Include PurchaseInteraction serialization
        public ComponentFieldMap<TComponent> WithPurchaseInteraction()
        {
            _includePurchaseInteraction = true;

            _capturePurchaseInteraction = (c, state) =>
            {
                var purchase = (c as Component)?.GetComponent<PurchaseInteraction>();
                if (purchase != null)
                {
                    state["purchaseCost"] = purchase.cost;
                    state["purchaseCostType"] = (int)purchase.costType;
                    state["purchaseAvailable"] = purchase.available;
                    state["purchaseLocked"] = purchase.lockGameObject;
                }
            };

            _restorePurchaseInteraction = (c, state) =>
            {
                var purchase = (c as Component)?.GetComponent<PurchaseInteraction>();
                if (purchase == null) return;

                if (state.TryGetValue("purchaseCost", out var cost))
                {
                    purchase.Networkcost = (int)cost;
                }

                if (state.TryGetValue("purchaseAvailable", out var available))
                {
                    purchase.SetAvailable((bool)available);
                }

                if (state.TryGetValue("purchaseLocked", out var locked))
                {
                    var lockObj = locked as bool?;
                    if (lockObj.HasValue)
                    {
                        purchase.lockGameObject = lockObj.Value ? purchase.lockGameObject : null;
                    }
                }
            };

            return this;
        }

        // Include HealthComponent serialization
        public ComponentFieldMap<TComponent> WithHealthComponent()
        {
            _includeHealthComponent = true;

            _captureHealthComponent = (c, state) =>
            {
                var health = (c as Component)?.GetComponent<HealthComponent>();
                if (health != null)
                {
                    state["health"] = health.health;
                    state["shield"] = health.shield;
                    state["barrier"] = health.barrier;
                }
            };

            _restoreHealthComponent = (c, state) =>
            {
                var health = (c as Component)?.GetComponent<HealthComponent>();
                if (health == null) return;

                if (state.TryGetValue("health", out var healthVal))
                {
                    health.Networkhealth = (float)healthVal;
                }

                if (state.TryGetValue("shield", out var shield))
                {
                    health.Networkshield = (float)shield;
                }

                if (state.TryGetValue("barrier", out var barrier))
                {
                    health.Networkbarrier = (float)barrier;
                }
            };

            return this;
        }

        // Include EntityStateMachine serialization (preserves current entity state like idle, attacking, etc.)
        public ComponentFieldMap<TComponent> WithEntityStateMachine(string? machineName = null)
        {
            _includeEntityStateMachine = true;

            _captureEntityStateMachine = (c, state) =>
            {
                var component = c as Component;
                if (component == null) return;

                EntityStateMachine? stateMachine = null;
                if (string.IsNullOrEmpty(machineName))
                {
                    // Get the first EntityStateMachine on the object
                    stateMachine = component.GetComponent<EntityStateMachine>();
                }
                else
                {
                    // Search for a named state machine
                    var allStateMachines = component.GetComponentsInChildren<EntityStateMachine>();
                    stateMachine = System.Array.Find(allStateMachines, sm => sm.customName == machineName);
                }

                if (stateMachine != null && stateMachine.state != null)
                {
                    var stateType = stateMachine.state.GetType();
                    if (stateType != null && !string.IsNullOrEmpty(stateType.FullName))
                    {
                        state["EntityStateType"] = stateType.FullName;
                        state["EntityStateMachineName"] = string.IsNullOrEmpty(machineName) ? stateMachine.customName : machineName;
                    }
                }
            };

            _restoreEntityStateMachine = (c, state) =>
            {
                var component = c as Component;
                if (component == null) return;

                if (!state.TryGetValue("EntityStateType", out var stateTypeObj) ||
                    !state.TryGetValue("EntityStateMachineName", out var machineNameObj))
                {
                    return;
                }

                var stateTypeName = stateTypeObj?.ToString();
                var targetMachineName = machineNameObj?.ToString();

                if (string.IsNullOrEmpty(stateTypeName) || string.IsNullOrEmpty(targetMachineName))
                {
                    return;
                }

                try
                {
                    // Find the state machine
                    var allStateMachines = component.GetComponentsInChildren<EntityStateMachine>();
                    EntityStateMachine? stateMachine = null;

                    // Try to find by custom name first
                    if (!string.IsNullOrEmpty(targetMachineName))
                    {
                        stateMachine = System.Array.Find(allStateMachines, sm => sm.customName == targetMachineName);
                    }

                    // If no custom name specified or not found, use the first state machine
                    if (stateMachine == null && allStateMachines.Length > 0)
                    {
                        stateMachine = allStateMachines[0];
                    }

                    if (stateMachine == null)
                    {
                        Log.Warning($"[EntityStateMachine] Could not find state machine on {component.gameObject.name}");
                        return;
                    }

                    // Find the state type by full name
                    Type? stateType = null;
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            var types = asm.GetTypes();
                            foreach (var t in types)
                            {
                                if (t.FullName == stateTypeName && typeof(EntityState).IsAssignableFrom(t))
                                {
                                    stateType = t;
                                    break;
                                }
                            }
                            if (stateType != null) break;
                        }
                        catch (System.Reflection.ReflectionTypeLoadException)
                        {
                            // Ignore assemblies that can't be loaded
                            continue;
                        }
                    }

                    if (stateType != null)
                    {
                        var newState = EntityStateCatalog.InstantiateState(stateType);
                        if (newState != null)
                        {
                            stateMachine.SetState(newState);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[EntityStateMachine] Restored {component.gameObject.name} to state {stateTypeName}");
                            }
                        }
                    }
                    else
                    {
                        Log.Warning($"[EntityStateMachine] Could not find state type '{stateTypeName}' for {component.gameObject.name}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[EntityStateMachine] Failed to restore state for {component.gameObject.name}: {ex.Message}");
                }
            };

            return this;
        }

        public bool CanHandle(GameObject obj)
        {
            var component = obj.GetComponent<TComponent>();
            var canHandle = component != null;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                if (!canHandle)
                {
                    Log.Warning($"[ComponentFieldMap<{typeof(TComponent).Name}] {obj.name}: Component not found!");
                }
                else
                {
                    Log.Info($"[ComponentFieldMap<{typeof(TComponent).Name}] {obj.name}: Component found");
                }
            }

            return canHandle;
        }

        public Dictionary<string, object>? CaptureState(GameObject obj)
        {
            var component = obj.GetComponent<TComponent>();
            if (component == null) return null;

            var componentType = typeof(TComponent).Name;
            var state = new Dictionary<string, object>
            {
                ["ObjectType"] = obj.name.Replace("(Clone)", "").Trim()
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ComponentFieldMap<{componentType}] Capturing state for {obj.name}");
            }

            try
            {
                // Capture all registered fields
                foreach (var field in _fields)
                {
                    var value = field.Getter(component);
                    field.LastCapturedValue = value;

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Capturing field '{field.Key}': {value?.ToString() ?? "null"}");
                    }

                    if (field.AsInt && value is Enum enumValue)
                    {
                        state[field.Key] = Convert.ToInt32(enumValue);
                    }
                    else
                    {
                        state[field.Key] = value!;
                    }
                }

                // Capture PurchaseInteraction if configured
                if (_includePurchaseInteraction && _capturePurchaseInteraction != null)
                {
                    _capturePurchaseInteraction(component, state);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Captured PurchaseInteraction");
                    }
                }

                // Capture HealthComponent if configured
                if (_includeHealthComponent && _captureHealthComponent != null)
                {
                    _captureHealthComponent(component, state);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Captured HealthComponent");
                    }
                }

                // Capture EntityStateMachine if configured
                if (_includeEntityStateMachine && _captureEntityStateMachine != null)
                {
                    _captureEntityStateMachine(component, state);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Captured EntityStateMachine");
                    }
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ComponentFieldMap<{componentType}] Captured {state.Count} values for {obj.name}");
                }

                return state;
            }
            catch (Exception ex)
            {
                Log.Error($"[{componentType}] Failed to capture state: {ex.Message}");
                return null;
            }
        }

        public bool RestoreState(GameObject obj, Dictionary<string, object> state)
        {
            var component = obj.GetComponent<TComponent>();
            if (component == null) return false;

            var componentType = typeof(TComponent).Name;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ComponentFieldMap<{componentType}] Restoring state for {obj.name}");
            }

            try
            {
                // Restore all registered fields
                foreach (var field in _fields)
                {
                    if (field.Restore == null) continue; // Skip fields without restore logic

                    if (state.TryGetValue(field.Key, out var value))
                    {
                        try
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                var currentValue = field.Getter(component);
                                Log.Info($"  - Restoring field '{field.Key}': {value?.ToString() ?? "null"} -> {currentValue?.ToString() ?? "null"}");
                            }

                            field.Restore(component, value);

                            // Verify the restoration worked
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                var newValue = field.Getter(component);
                                if (!Equals(value, newValue))
                                {
                                    Log.Warning($"  - Field '{field.Key}' not properly restored: expected {value}, got {newValue}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[{componentType}] Failed to restore field '{field.Key}': {ex.Message}");
                        }
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Warning($"  - Field '{field.Key}' not found in saved state");
                    }
                }

                // Restore PurchaseInteraction if configured
                if (_includePurchaseInteraction && _restorePurchaseInteraction != null)
                {
                    _restorePurchaseInteraction(component, state);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Restored PurchaseInteraction");
                    }
                }

                // Restore HealthComponent if configured
                if (_includeHealthComponent && _restoreHealthComponent != null)
                {
                    _restoreHealthComponent(component, state);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Restored HealthComponent");
                    }
                }

                // Restore EntityStateMachine if configured
                if (_includeEntityStateMachine && _restoreEntityStateMachine != null)
                {
                    _restoreEntityStateMachine(component, state);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Restored EntityStateMachine");
                    }
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ComponentFieldMap<{componentType}] Successfully restored {obj.name}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[{componentType}] Failed to restore state: {ex.Message}");
                return false;
            }
        }

        // Safely convert a value to the target type with error handling
        private static T? SafeConvert<T>(object? value)
        {
            if (value == null) return default;

            var targetType = typeof(T);

            try
            {
                // Handle nullable types
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    if (value == DBNull.Value) return default;
                    targetType = Nullable.GetUnderlyingType(targetType)!;
                }

                // Direct type match
                if (value.GetType() == targetType)
                {
                    return (T?)value;
                }

                // Type conversion
                if (targetType == typeof(int))
                {
                    return (T?)(object?)Convert.ToInt32(value);
                }
                if (targetType == typeof(uint))
                {
                    return (T?)(object?)Convert.ToUInt32(value);
                }
                if (targetType == typeof(float))
                {
                    return (T?)(object?)Convert.ToSingle(value);
                }
                if (targetType == typeof(double))
                {
                    return (T?)(object?)Convert.ToDouble(value);
                }
                if (targetType == typeof(bool))
                {
                    return (T?)(object?)Convert.ToBoolean(value);
                }
                if (targetType == typeof(string))
                {
                    return (T?)(object?)value.ToString();
                }

                return (T?)Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to convert value '{value}' ({value?.GetType().Name}) to {targetType.Name}: {ex.Message}");
                return default;
            }
        }

        // Safely convert a value to uint with comprehensive type handling
        private static uint SafeConvertToUInt32(object? value)
        {
            if (value == null) return 0;

            try
            {
                if (value is uint u) return u;
                if (value is int i) return (uint)i;
                if (value is long l) return (uint)l;
                if (value is float f) return (uint)f;
                if (value is double d) return (uint)d;
                if (value is string s && uint.TryParse(s, out var parsed)) return parsed;

                return Convert.ToUInt32(value);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to convert value '{value}' ({value?.GetType().Name}) to uint: {ex.Message}");
                return 0;
            }
        }

        // Extract the field name from a lambda expression
        private static string GetFieldName<TField>(Func<TComponent, TField> getter)
        {
            var body = getter.Target?.ToString();
            if (!string.IsNullOrEmpty(body))
            {
                // Simple heuristic: extract field name from lambda
                var match = System.Text.RegularExpressions.Regex.Match(body, @"\.(\w+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return getter.Method.Name;
        }

        // Internal field mapping record
        private class FieldMapping
        {
            public string Key { get; set; } = string.Empty;
            public Func<TComponent, object?> Getter { get; set; } = c => null!;
            public Action<TComponent, object?>? Restore { get; set; }
            public bool AsInt { get; set; }
            public object? LastCapturedValue { get; set; }
        }
    }
}
