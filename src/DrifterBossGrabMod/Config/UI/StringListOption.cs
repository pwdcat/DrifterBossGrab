#nullable enable
using BepInEx.Configuration;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;

namespace DrifterBossGrabMod.Config.UI
{
    public class StringListOption : StringInputFieldOption
    {
        private readonly ConfigEntry<string> _entry;
        private readonly bool _discoverComponents;

        public StringListOption(ConfigEntry<string> entry, string name, string category, bool discoverComponents = false)
            : base(entry, new InputFieldConfig { name = name, category = category })
        {
            _entry = entry;
            _discoverComponents = discoverComponents;
        }

        public override GameObject CreateOptionGameObject(GameObject prefab, Transform parent)
        {
            var row = new GameObject("Mod Option List, " + Name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var controller = row.AddComponent<StringListController>();
            controller.settingToken = Identifier;
            controller.nameToken = GetNameToken();
            controller.Entry = _entry;
            controller.Title = Name;
            controller.DiscoverComponents = _discoverComponents;
            controller.UsePrefabAppearance(prefab);
            return row;
        }
    }
}
