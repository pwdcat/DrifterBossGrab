#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace DrifterBossGrabMod.ProperSave.Serializers.Plugins
{
    public class GenericComponentSerializerPlugin : IObjectSerializerPlugin
    {
        public int Priority => 0;

        public string PluginName => "GenericComponentSerializerPlugin";

        private static readonly Type[] _skippedTypes = new Type[]
        {
            typeof(Transform),
            typeof(Rigidbody),
            typeof(Renderer),
            typeof(MeshRenderer),
            typeof(SkinnedMeshRenderer),
            typeof(ParticleSystem),
            typeof(Light),
            typeof(Camera),
            typeof(AudioSource),
            typeof(Animation),
            typeof(Animator)
        };

        public bool CanHandle(GameObject obj)
        {
            return obj != null;
        }

        public Dictionary<string, object>? CaptureState(GameObject obj)
        {
            var state = new Dictionary<string, object>();

            // Scan all components on the object
            foreach (var component in obj.GetComponents<Component>())
            {
                if (component == null) continue;

                var componentType = component.GetType();
                if (_skippedTypes.Contains(componentType)) continue;

                var componentState = new Dictionary<string, object>();
                state[componentType.Name] = componentState;

                // Capture NetworkBehaviour synced fields
                if (component is NetworkBehaviour networkBehaviour)
                {
                    CaptureNetworkBehaviourFields(networkBehaviour, componentState);
                }

                // Capture [SerializeField] fields
                CaptureSerializableFields(component, componentState);
            }

            return state;
        }

        private void CaptureNetworkBehaviourFields(NetworkBehaviour networkBehaviour, Dictionary<string, object> state)
        {
            var properties = networkBehaviour.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                // Look for properties that start with "Network" (synced fields in UNET)
                if (prop.Name.StartsWith("Network") && prop.CanRead)
                {
                    try
                    {
                        var value = prop.GetValue(networkBehaviour);
                        if (value != null && IsTypeSerializable(prop.PropertyType))
                        {
                            state[prop.Name] = value;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public bool RestoreState(GameObject obj, Dictionary<string, object> state)
        {
            foreach (var kvp in state)
            {
                var componentName = kvp.Key;
                var componentState = kvp.Value as Dictionary<string, object>;

                if (componentState == null) continue;

                // Find component by type name
                var component = obj.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetType().Name == componentName);

                if (component == null) continue;

                // Restore NetworkBehaviour fields
                if (component is NetworkBehaviour networkBehaviour)
                {
                    RestoreNetworkBehaviourFields(networkBehaviour, componentState);
                }

                // Restore serializable fields
                RestoreSerializableFields(component, componentState);
            }

            return true;
        }

        private void RestoreNetworkBehaviourFields(NetworkBehaviour networkBehaviour, Dictionary<string, object> state)
        {
            var properties = networkBehaviour.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (prop.Name.StartsWith("Network") && prop.CanWrite &&
                    state.TryGetValue(prop.Name, out var value))
                {
                    try
                    {
                        var convertedValue = Convert.ChangeType(value, prop.PropertyType);
                        prop.SetValue(networkBehaviour, convertedValue);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private void CaptureSerializableFields(Component component, Dictionary<string, object> state)
        {
            var fields = component.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsStatic) continue;
                if (field.IsLiteral) continue;
                if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField))) continue;

                if (!IsTypeSerializable(field.FieldType)) continue;

                try
                {
                    var value = field.GetValue(component);
                    if (value != null)
                    {
                        state[field.Name] = ConvertFieldValueToString(value, field.FieldType);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private bool IsTypeSerializable(Type type)
        {
            if (type.IsPrimitive || type == typeof(string)) return true;
            if (type == typeof(Vector3) || type == typeof(Quaternion)) return true;
            if (type.IsEnum) return true;
            if (type.IsArray || type.IsGenericType) return false;
            return false;
        }

        private object ConvertFieldValueToString(object value, Type fieldType)
        {
            if (value == null) return string.Empty;

            if (fieldType == typeof(Vector3))
            {
                var v = (Vector3)value;
                return $"{v.x}|{v.y}|{v.z}";
            }

            if (fieldType == typeof(Quaternion))
            {
                var q = (Quaternion)value;
                return $"{q.x}|{q.y}|{q.z}|{q.w}";
            }

            return value;
        }

        private void RestoreSerializableFields(Component component, Dictionary<string, object> state)
        {
            var componentType = component.GetType();

            foreach (var kvp in state)
            {
                var fieldName = kvp.Key;
                var value = kvp.Value;

                var field = componentType.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null && !field.IsLiteral && !field.IsStatic)
                {
                    try
                    {
                        var convertedValue = ConvertStringToFieldValue(value?.ToString(), field.FieldType);
                        if (convertedValue != null)
                        {
                            field.SetValue(component, convertedValue);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private object? ConvertStringToFieldValue(string? value, Type fieldType)
        {
            if (string.IsNullOrEmpty(value)) return null;

            if (fieldType == typeof(Vector3))
            {
                var parts = value.Split('|');
                if (parts.Length == 3)
                {
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
                    return new Vector3(x, y, z);
                }
                return Vector3.zero;
            }

            if (fieldType == typeof(Quaternion))
            {
                var parts = value.Split('|');
                if (parts.Length == 4)
                {
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
                    float.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w);
                    return new Quaternion(x, y, z, w);
                }
                return Quaternion.identity;
            }

            try
            {
                return Convert.ChangeType(value, fieldType);
            }
            catch
            {
                return null;
            }
        }
    }
}
