#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DrifterBossGrabMod.ProperSave;

namespace DrifterBossGrabMod.ProperSave.Serializers.Plugins
{
    public class QualityIntegrationPlugin : IObjectSerializerPlugin
    {
        public int Priority => 60;

        public string PluginName => "QualityIntegrationPlugin";

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

        public bool CanHandle(GameObject obj)
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

        public Dictionary<string, object>? CaptureState(GameObject obj)
        {
            EnsureTypesLoaded();
            var state = new Dictionary<string, object>();

            try
            {
                if (_qualityDuplicatorType != null)
                {
                    var duplicator = obj.GetComponent(_qualityDuplicatorType);
                    if (duplicator != null)
                    {
                        var qualityTier = _qualityDuplicatorType.GetField("qualityTier",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var qualityTierIndex = _qualityDuplicatorType.GetField("qualityTierIndex",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (qualityTier != null)
                            state["DuplicatorQuality"] = qualityTier.GetValue(duplicator);

                        if (qualityTierIndex != null)
                            state["DuplicatorQualityTier"] = qualityTierIndex.GetValue(duplicator);
                    }
                }

                if (_qualityItemBodyType != null)
                {
                    var qualityBody = obj.GetComponent(_qualityItemBodyType);
                    if (qualityBody != null)
                    {
                        var qualityTier = _qualityItemBodyType.GetField("qualityTier",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var qualityTierIndex = _qualityItemBodyType.GetField("qualityTierIndex",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (qualityTier != null)
                            state["ItemQuality"] = qualityTier.GetValue(qualityBody);

                        if (qualityTierIndex != null)
                            state["ItemQualityIndex"] = qualityTierIndex.GetValue(qualityBody);
                    }
                }

                if (_itemQualityGroupType != null)
                {
                    var qualityGroup = obj.GetComponent(_itemQualityGroupType);
                    if (qualityGroup != null)
                    {
                        var nameProp = _itemQualityGroupType.GetProperty("name",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var indexProp = _itemQualityGroupType.GetProperty("index",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (nameProp != null)
                        {
                            var name = nameProp.GetValue(qualityGroup);
                            if (name != null)
                                state["QualityGroupName"] = name.ToString();
                        }

                        if (indexProp != null)
                        {
                            var index = indexProp.GetValue(qualityGroup);
                            if (index != null)
                                state["QualityGroupIndex"] = index;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return state.Count > 0 ? state : null;
        }

        public bool RestoreState(GameObject obj, Dictionary<string, object> state)
        {
            EnsureTypesLoaded();

            try
            {
                if (_qualityDuplicatorType != null && state.TryGetValue("DuplicatorQuality", out var duplicatorQuality))
                {
                    var duplicator = obj.GetComponent(_qualityDuplicatorType);
                    if (duplicator != null)
                    {
                        var qualityTierField = _qualityDuplicatorType.GetField("qualityTier",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (qualityTierField != null && duplicatorQuality != null)
                        {
                            var convertedValue = Convert.ChangeType(duplicatorQuality, qualityTierField.FieldType);
                            qualityTierField.SetValue(duplicator, convertedValue);
                        }
                    }
                }

                if (_qualityDuplicatorType != null && state.TryGetValue("DuplicatorQualityTier", out var duplicatorTierIndex))
                {
                    var duplicator = obj.GetComponent(_qualityDuplicatorType);
                    if (duplicator != null)
                    {
                        var qualityTierIndexField = _qualityDuplicatorType.GetField("qualityTierIndex",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (qualityTierIndexField != null && duplicatorTierIndex != null)
                        {
                            var convertedValue = Convert.ChangeType(duplicatorTierIndex, qualityTierIndexField.FieldType);
                            qualityTierIndexField.SetValue(duplicator, convertedValue);
                        }
                    }
                }

                if (_qualityItemBodyType != null && state.TryGetValue("ItemQuality", out var itemQuality))
                {
                    var qualityBody = obj.GetComponent(_qualityItemBodyType);
                    if (qualityBody != null)
                    {
                        var qualityTierField = _qualityItemBodyType.GetField("qualityTier",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (qualityTierField != null && itemQuality != null)
                        {
                            var convertedValue = Convert.ChangeType(itemQuality, qualityTierField.FieldType);
                            qualityTierField.SetValue(qualityBody, convertedValue);
                        }
                    }
                }

                if (_qualityItemBodyType != null && state.TryGetValue("ItemQualityIndex", out var itemQualityIndex))
                {
                    var qualityBody = obj.GetComponent(_qualityItemBodyType);
                    if (qualityBody != null)
                    {
                        var qualityTierIndexField = _qualityItemBodyType.GetField("qualityTierIndex",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (qualityTierIndexField != null && itemQualityIndex != null)
                        {
                            var convertedValue = Convert.ChangeType(itemQualityIndex, qualityTierIndexField.FieldType);
                            qualityTierIndexField.SetValue(qualityBody, convertedValue);
                        }
                    }
                }

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

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
