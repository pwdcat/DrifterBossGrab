#nullable enable
using BepInEx.Configuration;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;

namespace DrifterBossGrabMod.Config.UI
{
    public enum FormulaEditorKind { Slots, Capacity, Penalty, Slam, Flag }

    public class FormulaEditorOption : StringInputFieldOption
    {
        private readonly ConfigEntry<string> _entry;
        private readonly FormulaEditorKind _kind;

        public FormulaEditorOption(ConfigEntry<string> entry, string name, string category, FormulaEditorKind kind)
            : base(entry, new InputFieldConfig { name = name, category = category })
        {
            _entry = entry;
            _kind = kind;
        }

        public override GameObject CreateOptionGameObject(GameObject prefab, Transform parent)
        {
            var row = new GameObject("Mod Option Formula, " + Name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var controller = row.AddComponent<FormulaEditorController>();
            controller.Entry = _entry;
            controller.Title = Name;
            controller.Kind = _kind;
            controller.settingToken = Identifier;
            controller.nameToken = GetNameToken();
            controller.UsePrefabAppearance(prefab);
            return row;
        }
    }
}
