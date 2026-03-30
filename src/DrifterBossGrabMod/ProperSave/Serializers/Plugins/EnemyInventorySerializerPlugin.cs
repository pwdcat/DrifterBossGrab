#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.ProperSave.Serializers.Plugins
{
    // Testing Required
    public class EnemyInventorySerializerPlugin : IObjectSerializerPlugin
    {
        public int Priority => 105;

        public string PluginName => "EnemyInventorySerializerPlugin";

        public bool CanHandle(GameObject obj)
        {
            var inventory = obj.GetComponent<Inventory>();
            if (inventory == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[EnemyInventory] {obj.name}: No Inventory component found");
                }
                return false;
            }

            // Only serialize inventories that have items or equipment
            var canHandle = inventory.itemAcquisitionOrder.Any() || inventory.GetEquipmentIndex() != EquipmentIndex.None;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[EnemyInventory] CanHandle({obj.name}): {canHandle}, Items: {inventory.itemAcquisitionOrder.Count}, Equipment: {inventory.GetEquipmentIndex()}");
            }

            return canHandle;
        }

        public Dictionary<string, object>? CaptureState(GameObject obj)
        {
            var inventory = obj.GetComponent<Inventory>();
            if (inventory == null) return null;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[EnemyInventory] Capturing inventory state for {obj.name}");
            }

            var state = new Dictionary<string, object>
            {
                ["ObjectType"] = obj.name.Replace("(Clone)", "").Trim()
            };

            // Capture all item stacks
            var items = new Dictionary<string, int>();
            foreach (var itemIndex in inventory.itemAcquisitionOrder)
            {
                if (itemIndex != ItemIndex.None)
                {
                    var count = inventory.GetItemCountPermanent(itemIndex);
                    if (count > 0)
                    {
                        var itemDef = ItemCatalog.GetItemDef(itemIndex);
                        if (itemDef != null)
                        {
                            items[itemDef.name] = count;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"  - Captured item: {itemDef.name} x{count}");
                            }
                        }
                    }
                }
            }
            state["Items"] = items;

                // Capture equipment
                var equipmentIndex = inventory.GetEquipmentIndex();
                if (equipmentIndex != EquipmentIndex.None)
                {
                    var equipmentDef = EquipmentCatalog.GetEquipmentDef(equipmentIndex);
                    if (equipmentDef != null)
                    {
                        state["Equipment"] = equipmentDef.name;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"  - Captured equipment: {equipmentDef.name}");
                        }
                    }
                }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[EnemyInventory] Captured {items.Count} item types for {obj.name}");
            }

            return state;
        }

        public bool RestoreState(GameObject obj, Dictionary<string, object> state)
        {
            var inventory = obj.GetComponent<Inventory>();
            if (inventory == null) return false;

            try
            {
                // Clear existing inventory
                inventory.itemAcquisitionOrder.Clear();

                // Restore item stacks
                if (state.TryGetValue("Items", out var itemsObj) && itemsObj is Dictionary<string, int> items)
                {
                    foreach (var kvp in items)
                    {
                        var itemIndex = ItemCatalog.FindItemIndex(kvp.Key);
                        var itemDef = ItemCatalog.GetItemDef(itemIndex);
                        if (itemDef != null && kvp.Value > 0)
                        {
                            inventory.GiveItemPermanent(itemDef.itemIndex, kvp.Value);
                        }
                    }
                }

                // Restore equipment
                if (state.TryGetValue("Equipment", out var equipmentNameObj) &&
                    equipmentNameObj is string equipmentName &&
                    !string.IsNullOrEmpty(equipmentName))
                {
                    var equipmentIndex = EquipmentCatalog.FindEquipmentIndex(equipmentName);
                    var equipmentDef = EquipmentCatalog.GetEquipmentDef(equipmentIndex);
                    if (equipmentDef != null)
                    {
                        inventory.SetEquipmentIndex(equipmentDef.equipmentIndex, false);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[InventorySave] Failed to restore inventory state: {ex.Message}");
                return false;
            }
        }
    }
}
