#nullable enable
using BepInEx.Configuration;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace DrifterBossGrabMod.Config.UI
{
    public class StringListOption : StringInputFieldOption
    {
        private readonly ConfigEntry<string> _entry;
        private readonly bool _discoverComponents;
        private readonly Func<IEnumerable<KeyValuePair<string, string>>>? _choices;
        private readonly Func<string>? _emptyMessage;
        private readonly Func<string>? _currentTitle;
        private readonly bool _eliteEffectColumns;

        public StringListOption(ConfigEntry<string> entry, string name, string category, bool discoverComponents = false,
            Func<IEnumerable<KeyValuePair<string, string>>>? choices = null,
            Func<string>? emptyMessage = null, Func<string>? currentTitle = null, bool eliteEffectColumns = false)
            : base(entry, new InputFieldConfig { name = name, category = category })
        {
            _entry = entry;
            _discoverComponents = discoverComponents;
            _choices = choices;
            _emptyMessage = emptyMessage;
            _currentTitle = currentTitle;
            _eliteEffectColumns = eliteEffectColumns;
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
            controller.Choices = _choices;
            controller.EmptyMessage = _emptyMessage;
            controller.CurrentTitle = _currentTitle;
            controller.EliteEffectColumns = _eliteEffectColumns;
            controller.UsePrefabAppearance(prefab);
            return row;
        }
    }
}
