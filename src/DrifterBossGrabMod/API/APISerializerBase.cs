#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using DrifterBossGrabMod.ProperSave.Serializers;

namespace DrifterBossGrabMod.API
{
    public abstract class APISerializerBase : IObjectSerializerPlugin
    {
        public abstract int Priority { get; }
        public abstract string PluginName { get; }

        public bool CanHandle(GameObject obj)
        {
            return CanHandleObject(obj);
        }

        public Dictionary<string, object>? CaptureState(GameObject obj)
        {
            var state = new Dictionary<string, object>();
            try
            {
                CaptureObjectState(obj, state);
                return state.Count > 0 ? state : null;
            }
            catch (Exception ex)
            {
                Log.Error($"[{PluginName}] Failed to capture state for {obj.name}: {ex.Message}");
                return null;
            }
        }

        public bool RestoreState(GameObject obj, Dictionary<string, object> state)
        {
            try
            {
                RestoreObjectState(obj, state);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[{PluginName}] Failed to restore state for {obj.name}: {ex.Message}");
                return false;
            }
        }

        protected abstract bool CanHandleObject(GameObject obj);
        protected abstract void CaptureObjectState(GameObject obj, Dictionary<string, object> state);
        protected abstract void RestoreObjectState(GameObject obj, Dictionary<string, object> state);

        protected void CaptureComponent<T>(GameObject obj, Dictionary<string, object> state, string key, Action<T, Dictionary<string, object>> capture) where T : Component
        {
            var component = obj.GetComponent<T>();
            if (component != null)
            {
                capture(component, state);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[{PluginName}] Captured {typeof(T).Name} for {obj.name}");
                }
            }
        }

        protected void RestoreComponent<T>(GameObject obj, Dictionary<string, object> state, Action<T, Dictionary<string, object>> restore) where T : Component
        {
            var component = obj.GetComponent<T>();
            if (component != null)
            {
                restore(component, state);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[{PluginName}] Restored {typeof(T).Name} for {obj.name}");
                }
            }
        }

        protected bool TryGetValue<T>(Dictionary<string, object> state, string key, out T? value)
        {
            if (state.TryGetValue(key, out var obj) && obj != null)
            {
                try
                {
                    if (typeof(T).IsEnum)
                    {
                        if (obj is int intVal)
                        {
                            value = (T)Enum.ToObject(typeof(T), intVal);
                            return true;
                        }
                    }
                    else
                    {
                        value = (T)Convert.ChangeType(obj, typeof(T));
                        return true;
                    }
                }
                catch
                {
                    value = default;
                    return false;
                }
            }
            value = default;
            return false;
        }
    }

    public class ComponentAPISerializer<TComponent> : APISerializerBase where TComponent : Component
    {
        private readonly int _priority;
        private readonly List<SerializerAction> _actions = new List<SerializerAction>();

        public override int Priority => _priority;
        public override string PluginName => $"ComponentAPISerializer<{typeof(TComponent).Name}>";

        public ComponentAPISerializer(int priority)
        {
            _priority = priority;
        }

        protected override bool CanHandleObject(GameObject obj)
        {
            return obj.GetComponent<TComponent>() != null;
        }

        protected override void CaptureObjectState(GameObject obj, Dictionary<string, object> state)
        {
            var component = obj.GetComponent<TComponent>();
            if (component == null) return;

            state["ObjectType"] = obj.name.Replace("(Clone)", "").Trim();

            foreach (var action in _actions)
            {
                action.Capture(component, state);
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ComponentAPISerializer<{typeof(TComponent).Name}>] Captured {state.Count} values for {obj.name}");
            }
        }

        protected override void RestoreObjectState(GameObject obj, Dictionary<string, object> state)
        {
            var component = obj.GetComponent<TComponent>();
            if (component == null) return;

            foreach (var action in _actions)
            {
                action.Restore(component, state);
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ComponentAPISerializer<{typeof(TComponent).Name}>] Successfully restored {obj.name}");
            }
        }

        public ComponentAPISerializer<TComponent> AddAction<TValue>(
            string key,
            Func<TComponent, TValue> getter,
            Action<TComponent, TValue>? setter = null,
            bool asInt = false)
        {
            _actions.Add(new SerializerAction
            {
                Key = key,
                Capture = (component, state) =>
                {
                    var value = getter(component);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Capturing field '{key}': {value?.ToString() ?? "null"}");
                    }

                    if (asInt && value is Enum enumValue)
                    {
                        state[key] = Convert.ToInt32(enumValue);
                    }
                    else
                    {
                        state[key] = value!;
                    }
                },
                Restore = (component, state) =>
                {
                    if (setter == null) return;

                    if (TryGetValue(state, key, out TValue? value))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            var currentValue = getter(component);
                            Log.Info($"  - Restoring field '{key}': {value?.ToString() ?? "null"} -> {currentValue?.ToString() ?? "null"}");
                        }

                        setter(component, value!);
                    }
                }
            });

            return this;
        }

        public ComponentAPISerializer<TComponent> AddCustomAction(
            Action<TComponent, Dictionary<string, object>> capture,
            Action<TComponent, Dictionary<string, object>> restore)
        {
            _actions.Add(new SerializerAction
            {
                Capture = capture,
                Restore = restore
            });

            return this;
        }

        private class SerializerAction
        {
            public string Key { get; set; } = string.Empty;
            public Action<TComponent, Dictionary<string, object>> Capture { get; set; } = (_, _) => { };
            public Action<TComponent, Dictionary<string, object>> Restore { get; set; } = (_, _) => { };
        }
    }
}
