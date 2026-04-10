#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.ProperSave.Data
{
    [Serializable]
    public class DrifterBagSaveData
    {
        public List<BaggedObjectSaveData> BaggedObjects { get; set; } = new();
        public string? MainSeatObjectInstanceId { get; set; }
        public string SaveSceneName { get; set; } = string.Empty;
        public int StageClearCount { get; set; }
    }

    [Serializable]
    public class BaggedObjectSaveData
    {
        public string ObjectName { get; set; } = string.Empty;
        public string SaveType { get; set; } = "Body";
        public string ObjectType { get; set; } = string.Empty;
        public string SpawnCardPath { get; set; } = string.Empty;
        public int ObjectInstanceId { get; set; }
        public string NetworkId { get; set; } = string.Empty;
        public string OwnerPlayerId { get; set; } = string.Empty;
        public string SceneName { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;

        public string PrefabName { get; set; } = string.Empty;
        public string PrefabHash { get; set; } = string.Empty;
        public string? MasterName { get; set; }

        public string ComponentType { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;
        public string Rotation { get; set; } = string.Empty;

        public List<ComponentStateEntry> ComponentStates { get; set; } = new();

        public int? AdditionalSeatIndex { get; set; }
        public bool? IsMainSeatObject { get; set; }
        public int BagSlotIndex { get; set; } = -1;
    }

    [Serializable]
    public class ComponentStateEntry
    {
        public string PluginName { get; set; } = string.Empty;
        public List<StateValue> Values { get; set; } = new();
    }

    [Serializable]
    public class StateValue
    {
        public string Key { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
