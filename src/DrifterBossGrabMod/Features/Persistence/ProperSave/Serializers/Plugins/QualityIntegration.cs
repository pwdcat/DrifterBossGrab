#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DrifterBossGrabMod.ProperSave;
using DrifterBossGrabMod;
using DrifterBossGrabMod.API;

namespace DrifterBossGrabMod.ProperSave.Serializers.Plugins
{
    public class QualityIntegration : APISerializerBase
    {
        public override int Priority => 60;
        public override string PluginName => "QualityIntegration";

        private Assembly? _qualityAssembly;
        private Type? _qualityDuplicatorType;
        private Type? _qualityItemBodyType;
        private Type? _itemQualityGroupType;
        private Type? _qualityCatalogType;
        private Type? _qualityContentManagerType;

        private bool IsQualityModLoaded()
        {
            if (_qualityAssembly != null) return true;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            _qualityAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "ItemQualities");
            return _qualityAssembly != null;
        }

        private void EnsureTypesLoaded()
        {
            if (!IsQualityModLoaded()) return;
            if (_qualityAssembly == null) return;

            _qualityDuplicatorType = _qualityAssembly.GetType("ItemQualities.QualityDuplicatorBehavior");
            _qualityItemBodyType = _qualityAssembly.GetType("ItemQualities.Items.QualityItemBodyBehavior");
            _itemQualityGroupType = _qualityAssembly.GetType("ItemQualities.ItemQualityGroup");
            _qualityCatalogType = _qualityAssembly.GetType("ItemQualities.QualityCatalog");
            _qualityContentManagerType = _qualityAssembly.GetType("ItemQualities.ContentManagement.QualityContentManager");
        }

        protected override bool CanHandleObject(GameObject obj)
        {
            if (!IsQualityModLoaded()) return false;

            EnsureTypesLoaded();

            if (_qualityDuplicatorType != null && obj.GetComponent(_qualityDuplicatorType) != null)
                return true;

            return false;
        }

        protected override void CaptureObjectState(GameObject obj, Dictionary<string, object> state)
        {
            EnsureTypesLoaded();

            try
            {
                if (_qualityDuplicatorType != null)
                {
                    var duplicator = obj.GetComponent(_qualityDuplicatorType);
                    if (duplicator != null)
                    {
                        CaptureFieldValue(duplicator, state, "_available", "Available");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QualityIntegration] Failed to serialize quality data: {ex.Message}");
            }
        }

        protected override void RestoreObjectState(GameObject obj, Dictionary<string, object> state)
        {
            EnsureTypesLoaded();

            try
            {
                if (_qualityDuplicatorType != null)
                {
                    var duplicator = obj.GetComponent(_qualityDuplicatorType);
                    if (duplicator != null && state.TryGetValue("Available", out var availableObj) && availableObj is bool available)
                    {
                        var setAvailableMethod = _qualityDuplicatorType.GetMethod("SetAvailable", BindingFlags.Public | BindingFlags.Instance);
                        if (setAvailableMethod != null)
                        {
                            setAvailableMethod.Invoke(duplicator, new object[] { available });
                        }
                        else
                        {
                            // Fallback to setting field
                            RestoreFieldValue(duplicator, state, "Available", "_available");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QualityIntegration] Failed to restore quality data: {ex.Message}");
            }
        }

        private void CaptureFieldValue(object obj, Dictionary<string, object> dict, string fieldName, string stateKey)
        {
            try
            {
                var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(obj);
                    dict[stateKey] = value!;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QualityIntegration] Failed to capture field {fieldName}: {ex.Message}");
            }
        }

        private void RestoreFieldValue(object obj, Dictionary<string, object> dict, string stateKey, string fieldName)
        {
            try
            {
                if (dict.TryGetValue(stateKey, out var value) && value != null)
                {
                    var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var convertedValue = Convert.ChangeType(value, field.FieldType);
                        field.SetValue(obj, convertedValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QualityIntegration] Failed to restore field {fieldName}: {ex.Message}");
            }
        }
    }
}
