using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;
using RiskOfOptions.Components.Options;
using BepInEx.Configuration;

namespace DrifterBossGrabMod.Config.UI
{
    public class ComponentChooserOption : ChoiceOption
    {
        private string _customName;
        private string _customDescription;
        private string _customCategory;

        public ComponentChooserOption(ConfigEntryBase configEntry, string name, string description) 
            : base(configEntry)
        {
            _customName = name;
            _customDescription = description;
            _customCategory = "General";
        }

        public override BaseOptionConfig GetConfig()
        {
            return new ChoiceConfig { name = _customName, category = _customCategory, description = _customDescription };
        }

        public override GameObject CreateOptionGameObject(GameObject prefab, Transform parent)
        {
            GameObject button = Object.Instantiate(prefab, parent);

            var oldController = button.GetComponentInChildren<DropDownController>(true);
            if (oldController != null)
            {
                oldController.enabled = false;
                Object.DestroyImmediate(oldController);
            }

            var newController = button.AddComponent<DynamicDropDownController>();
            newController.nameToken = GetNameToken();
            
            var nameLabelComponent = button.transform.Find("Label");
            if (nameLabelComponent)
            {
                var textComponent = nameLabelComponent.GetComponent<TMPro.TextMeshProUGUI>();
                if (textComponent != null) textComponent.text = _customName;
            }

            button.name = $"Mod Option ComponentChooser, {_customName}";

            return button;
        }
    }
}
