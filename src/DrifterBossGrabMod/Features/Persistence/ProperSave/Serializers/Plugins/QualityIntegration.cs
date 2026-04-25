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
            _qualityItemBodyType = _qualityAssembly.GetType("ItemQualities.QualityItemBodyBehavior");
            _itemQualityGroupType = _qualityAssembly.GetType("ItemQualities.ItemQualityGroup");
            _qualityCatalogType = _qualityAssembly.GetType("ItemQualities.QualityCatalog");
            _qualityContentManagerType = _qualityAssembly.GetType("ItemQualities.QualityContentManager");
        }

        protected override bool CanHandleObject(GameObject obj)
        {
            if (!IsQualityModLoaded()) return false;

            EnsureTypesLoaded();

            if (_qualityDuplicatorType != null && obj.GetComponent(_qualityDuplicatorType) != null)
                return true;

            if (_qualityItemBodyType != null && obj.GetComponent(_qualityItemBodyType) != null)
                return true;

            if (_itemQualityGroupType != null && obj.GetComponent(_itemQualityGroupType) != null)
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
                        CaptureFieldValue(duplicator, state, "qualityTier", "DuplicatorQuality");
                        CaptureFieldValue(duplicator, state, "qualityTierIndex", "DuplicatorQualityTier");
                    }
                }

                if (_qualityItemBodyType != null)
                {
                    var qualityBody = obj.GetComponent(_qualityItemBodyType);
                    if (qualityBody != null)
                    {
                        CaptureFieldValue(qualityBody, state, "qualityTier", "ItemQuality");
                        CaptureFieldValue(qualityBody, state, "qualityTierIndex", "ItemQualityIndex");
                    }
                }

                if (_itemQualityGroupType != null)
                {
                    var qualityGroup = obj.GetComponent(_itemQualityGroupType);
                    if (qualityGroup != null)
                    {
                        CapturePropertyValue(qualityGroup, state, "name", "QualityGroupName");
                        CapturePropertyValue(qualityGroup, state, "index", "QualityGroupIndex");
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
                    if (duplicator != null)
                    {
                        RestoreFieldValue(duplicator, state, "DuplicatorQuality", "qualityTier");
                        RestoreFieldValue(duplicator, state, "DuplicatorQualityTier", "qualityTierIndex");
                    }
                }

                if (_qualityItemBodyType != null)
                {
                    var qualityBody = obj.GetComponent(_qualityItemBodyType);
                    if (qualityBody != null)
                    {
                        RestoreFieldValue(qualityBody, state, "ItemQuality", "qualityTier");
                        RestoreFieldValue(qualityBody, state, "ItemQualityIndex", "qualityTierIndex");
                    }
                }

                RestoreQualityGroup(obj, state);
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

        private void CapturePropertyValue(object obj, Dictionary<string, object> dict, string propertyName, string stateKey)
        {
            try
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                {
                    var value = property.GetValue(obj);
                    if (value != null)
                    {
                        dict[stateKey] = propertyName == "name" ? value.ToString()! : value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QualityIntegration] Failed to capture property {propertyName}: {ex.Message}");
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

        private void RestoreQualityGroup(GameObject obj, Dictionary<string, object> state)
        {
            try
            {
                if (_qualityCatalogType != null && _qualityContentManagerType != null &&
                    state.TryGetValue("QualityGroupName", out var groupName) &&
                    state.TryGetValue("QualityGroupIndex", out var groupIndex) &&
                    !string.IsNullOrEmpty(groupName?.ToString()))
                {
                    var getQualityGroupMethod = _qualityCatalogType.GetMethod("GetQualityGroupByName",
                        BindingFlags.Public | BindingFlags.Static);
                    var applyQualityGroupMethod = _qualityContentManagerType.GetMethod("ApplyQualityGroup",
                        BindingFlags.Public | BindingFlags.Static);

                    if (getQualityGroupMethod != null && applyQualityGroupMethod != null)
                    {
                        var qualityGroup = getQualityGroupMethod.Invoke(null, new[] { groupName.ToString() });
                        if (qualityGroup != null)
                        {
                            applyQualityGroupMethod.Invoke(null, new[] { obj, qualityGroup });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QualityIntegration] Failed to restore quality group: {ex.Message}");
            }
        }
    }
}
