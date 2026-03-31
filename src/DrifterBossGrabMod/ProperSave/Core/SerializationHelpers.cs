#nullable enable
using System;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.ProperSave.Core
{
    public static class SerializationHelpers
    {
        // Serialize methods
        public static string SerializeVector3(Vector3 v) => $"{v.x}|{v.y}|{v.z}";
        public static string SerializeQuaternion(Quaternion q) => $"{q.x}|{q.y}|{q.z}|{q.w}";
        public static string SerializeDateTime(DateTime dt) => dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        public static string SerializeGuid(Guid? guid) => guid?.ToString() ?? string.Empty;
        public static string SerializeValue(object? value)
        {
            if (value == null) return "";

            if (value is bool b) return b.ToString();
            if (value is int i) return i.ToString();
            if (value is uint u) return u.ToString();
            if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is string s) return s;

            return value.ToString() ?? "";
        }

        // Parse methods
        public static Vector3 ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector3.zero;
            var parts = s.Split('|');
            if (parts.Length != 3) return Vector3.zero;
            float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
            return new Vector3(x, y, z);
        }

        public static Quaternion ParseQuaternion(string s)
        {
            if (string.IsNullOrEmpty(s)) return Quaternion.identity;
            var parts = s.Split('|');
            if (parts.Length != 4) return Quaternion.identity;
            float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
            float.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w);
            return new Quaternion(x, y, z, w);
        }

        public static DateTime ParseDateTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.Now;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
            return DateTime.Now;
        }

        public static Guid? ParseGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Guid.TryParse(s, out var guid))
                return guid;
            return null;
        }

        public static NetworkHash128 ParsePrefabHash(string s)
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

        public static object? DeserializeValue(string value, string typeStr)
        {
            if (string.IsNullOrEmpty(value)) return null;

            try
            {
                switch (typeStr)
                {
                    case "System.Boolean":
                    case "bool":
                        return bool.Parse(value);

                    case "System.Int32":
                    case "int":
                        return int.Parse(value);

                    case "System.UInt32":
                    case "uint":
                        return uint.Parse(value);

                    case "System.Single":
                    case "float":
                        return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

                    case "System.Double":
                    case "double":
                        return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

                    case "System.String":
                    case "string":
                        return value;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[SerializationHelpers] Failed to convert value: {ex.Message}");
            }

            return value;
        }
    }
}
