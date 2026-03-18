using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;
using BepInEx.Bootstrap;

namespace BagCraftingMod.Support
{
    public enum QualityTier
    {
        None = -1,
        Uncommon,
        Rare,
        Epic,
        Legendary,
        Count
    }

    public static class ItemQualitySupport
    {
        private static bool? _isInstalled;
        public static bool IsInstalled
        {
            get
            {
                if (!_isInstalled.HasValue)
                {
                    _isInstalled = Chainloader.PluginInfos.ContainsKey("com.ThinkInvisible.ItemQualities");
                }
                return _isInstalled.Value;
            }
        }

        private static Type? _qualityCatalogType;
        private static MethodInfo? _getQualityTierMethod;
        private static MethodInfo? _getPickupIndexOfQualityMethod;

        private static MethodInfo? _getQualityTierDefMethod;

        private static void EnsureReflection()
        {
            if (_qualityCatalogType != null) return;
            
            try
            {
                _qualityCatalogType = Type.GetType("ItemQualities.QualityCatalog, ItemQualities");
                if (_qualityCatalogType != null)
                {
                    _getQualityTierMethod = _qualityCatalogType.GetMethod("GetQualityTier", new[] { typeof(PickupIndex) });
                    
                    var modQualityTierType = Type.GetType("ItemQualities.QualityTier, ItemQualities");
                    if (modQualityTierType != null)
                    {
                        _getQualityTierDefMethod = _qualityCatalogType.GetMethod("GetQualityTierDef", new[] { modQualityTierType });
                    }

                    _getPickupIndexOfQualityMethod = _qualityCatalogType.GetMethod("GetPickupIndexOfQuality", new[] { typeof(PickupIndex), modQualityTierType });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to reflect ItemQualities: {ex.Message}");
            }
        }

        public static QualityTier GetQuality(GameObject obj)
        {
            if (!IsInstalled || obj == null) return QualityTier.None;

            // 1. Check for PickupDisplay (Items/Equipment)
            var pickupDisplay = obj.GetComponentInChildren<PickupDisplay>();
            if (pickupDisplay != null)
            {
                return (QualityTier)GetQualityTier(pickupDisplay.pickupState.pickupIndex);
            }

            // 2. Check for QualityDuplicatorBehavior
            var duplicator = obj.GetComponent<MonoBehaviour>(); // Fallback if type not found
            if (duplicator != null && duplicator.GetType().Name == "QualityDuplicatorBehavior")
            {
                var costTypeField = duplicator.GetType().GetField("CostTypeIndex");
                if (costTypeField != null)
                {
                    var costType = (CostTypeIndex)costTypeField.GetValue(duplicator);
                    return MapCostTypeToQuality(costType);
                }
            }

            // 3. Fallback to name-based detection for chests
            string name = obj.name;
            if (name.Contains("Quality")) return QualityTier.Rare;
            if (name.Contains("Uncommon")) return QualityTier.Uncommon;
            if (name.Contains("Rare")) return QualityTier.Rare;
            if (name.Contains("Epic")) return QualityTier.Epic;
            if (name.Contains("Legendary")) return QualityTier.Legendary;

            return QualityTier.None;
        }

        private static int GetQualityTier(PickupIndex pickupIndex)
        {
            EnsureReflection();
            if (_getQualityTierMethod != null)
            {
                return (int)_getQualityTierMethod.Invoke(null, new object[] { pickupIndex });
            }
            return -1;
        }

        public static PickupIndex GetQualityPickupIndex(PickupIndex baseIndex, QualityTier tier)
        {
            if (!IsInstalled) return baseIndex;
            EnsureReflection();

            if (_getPickupIndexOfQualityMethod != null)
            {
                var modQualityTierType = Type.GetType("ItemQualities.QualityTier, ItemQualities");
                if (modQualityTierType != null)
                {
                    var enumValue = Enum.ToObject(modQualityTierType, (int)tier);
                    return (PickupIndex)_getPickupIndexOfQualityMethod.Invoke(null, new object[] { baseIndex, enumValue });
                }
            }
            return baseIndex;
        }

        public static Color GetQualityColor(QualityTier tier)
        {
            if (!IsInstalled || tier <= QualityTier.None) return Color.white;
            EnsureReflection();

            if (_getQualityTierDefMethod != null)
            {
                var modQualityTierType = Type.GetType("ItemQualities.QualityTier, ItemQualities");
                if (modQualityTierType != null)
                {
                    var enumValue = Enum.ToObject(modQualityTierType, (int)tier);
                    var tierDef = _getQualityTierDefMethod.Invoke(null, new object[] { enumValue });
                    if (tierDef != null)
                    {
                        var colorField = tierDef.GetType().GetField("color");
                        if (colorField != null)
                        {
                            return (Color)colorField.GetValue(tierDef);
                        }
                    }
                }
            }

            // Fallback colors if reflection fails
            return tier switch
            {
                QualityTier.Uncommon => Color.green,
                QualityTier.Rare => new Color(0.2f, 0.8f, 0.8f), // Teal-ish
                QualityTier.Epic => new Color(0.6f, 0.2f, 0.8f), // Purple
                QualityTier.Legendary => new Color(1f, 0.5f, 0f), // Orange
                _ => Color.white
            };
        }

        private static QualityTier MapCostTypeToQuality(CostTypeIndex costType)
        {
            // Need a better way, but for now we'll match by name
            string costTypeName = costType.ToString();
            if (costTypeName.Contains("WhiteItemQuality")) return QualityTier.Uncommon;
            if (costTypeName.Contains("GreenItemQuality")) return QualityTier.Rare;
            if (costTypeName.Contains("RedItemQuality")) return QualityTier.Epic;
            if (costTypeName.Contains("BossItemQuality")) return QualityTier.Legendary;

            return QualityTier.None;
        }

        public static string GetBaseName(string name)
        {
            string baseName = name.Replace("(Clone)", "").Trim();
            
            // Remove quality prefixes/suffixes
            baseName = baseName.Replace("Quality", "");
            baseName = baseName.Replace("Uncommon", "");
            baseName = baseName.Replace("Rare", "");
            baseName = baseName.Replace("Epic", "");
            baseName = baseName.Replace("Legendary", "");
            
            return baseName.Trim();
        }

        public static string GetQualityResultName(string baseResultName, QualityTier tier)
        {
            if (tier <= QualityTier.None) return baseResultName;

            if (baseResultName.StartsWith("Chest") || baseResultName == "EquipmentBarrel")
            {
                return "Quality" + baseResultName;
            }

            return baseResultName;
        }
    }
}
