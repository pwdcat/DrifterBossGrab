#nullable enable
using System.Linq;
using DrifterBossGrabMod.UI;
using BepInEx.Configuration;
using RiskOfOptions.Components.Options;
using RoR2.UI;
using UnityEngine.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrifterBossGrabMod.Config.UI
{
    public abstract class OptionEditorController : ModSetting
    {
        public TMP_FontAsset? Font;
        public ConfigEntry<string> Entry = null!;
        public string Title = "";
        public Sprite? FrameSprite;
        public Image.Type FrameType;
        private RectTransform _header = null!;
        private ColorBlock _nativeColors = ColorBlock.defaultColorBlock;
        protected Color RowColor => _nativeColors.normalColor;
        protected Color PanelColor => Color.Lerp(RowColor, Color.black, 0.15f);

        public void UsePrefabAppearance(GameObject prefab)
        {
            Font = prefab.GetComponentInChildren<TMP_Text>(true)?.font;
            var selectable = prefab.GetComponent<Selectable>() ?? prefab.GetComponentInChildren<Selectable>(true);
            if (selectable != null) _nativeColors = selectable.colors;
            var frame = prefab.GetComponent<Image>();
            if (frame == null || frame.sprite == null)
                frame = prefab.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.sprite != null);
            if (frame != null)
            {
                FrameSprite = frame.sprite;
                FrameType = frame.type;
            }
            _header = Rect("Header", transform);
            Place(_header, 0f, 1f, 0f, 64f);
            _header.gameObject.SetActive(false);
            var background = Surface(_header, Color.white);
            var button = NativeUiTheme.Button(_header, background, prefab);
            button.disablePointerClick = true;
            button.disableGamepadClick = true;
            _header.gameObject.SetActive(true);
        }

        protected RectTransform EditorHeader() => _header;

        protected TMP_Text Text(Transform parent, string value, float size)
        {
            var text = Rect("Label", parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        protected RectTransform MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(label, parent);
            var image = Surface(rect, Color.white);
            var button = NativeUiTheme.Button(rect, image);
            button.onClick.AddListener(action);
            var text = Text(rect, label, 20f);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            return rect;
        }

        protected Toggle CheckBox(Transform parent, string label, UnityEngine.Events.UnityAction<bool> action)
        {
            var rect = Rect(label, parent);
            var hitArea = Surface(rect, Color.white);
            var square = Rect("Box", rect);
            square.anchorMin = square.anchorMax = new Vector2(0f, 0.5f);
            square.anchoredPosition = new Vector2(17f, 0f);
            square.sizeDelta = new Vector2(26f, 26f);
            var nativePrefab = RiskOfOptions.Components.RuntimePrefabs.RuntimePrefabManager
                .Get<RiskOfOptions.Components.RuntimePrefabs.CheckBoxPrefab>().CheckBoxButton;
            var nativeToggle = nativePrefab.GetComponent<RiskOfOptions.Components.Options.ModSettingsBool>();
            var box = square.gameObject.AddComponent<Image>();
            box.sprite = nativeToggle.checkBoxFalse;
            box.preserveAspect = true;
            box.raycastTarget = false;
            var mark = Rect("Selected", square);
            Stretch(mark);
            var graphic = mark.gameObject.AddComponent<Image>();
            graphic.color = Color.white;
            graphic.sprite = nativeToggle.checkBoxTrue;
            graphic.preserveAspect = true;
            graphic.raycastTarget = false;
            var text = Text(rect, label, 20f);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(36f, 0f);
            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = hitArea;
            toggle.colors = _nativeColors;
            toggle.graphic = graphic;
            toggle.SetIsOnWithoutNotify(false);
            toggle.onValueChanged.AddListener(action);
            return toggle;
        }

        protected Image Surface(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = FrameSprite;
            image.type = FrameType;
            image.color = color;
            return image;
        }

        protected TMP_InputField Input(Transform parent, string prompt)
        {
            var rect = Rect("Input", parent);
            rect.gameObject.SetActive(false);
            var image = Surface(rect, Color.white);
            var area = Rect("Text Area", rect);
            Stretch(area);
            area.offsetMin = new Vector2(10f, 2f);
            area.offsetMax = new Vector2(-10f, -2f);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Text(area, "", 20f);
            Stretch(text.rectTransform);
            var placeholder = Text(area, prompt, 20f);
            Stretch(placeholder.rectTransform);
            placeholder.color = new Color(0.76f, 0.81f, 0.86f);
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = (TextMeshProUGUI)text;
            input.placeholder = placeholder;
            input.targetGraphic = image;
            input.colors = _nativeColors;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.text = "";
            rect.gameObject.SetActive(true);
            input.ForceLabelUpdate();
            return input;
        }

        protected static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        protected static void Place(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(left, 1f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, height);
        }

        protected static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
