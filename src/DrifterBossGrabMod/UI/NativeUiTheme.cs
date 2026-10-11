#nullable enable
using RiskOfOptions.Resources;
using System.Linq;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrifterBossGrabMod.UI
{
    internal static class NativeUiTheme
    {
        internal static ColorBlock Colors
        {
            get
            {
                var source = Prefabs.inputFieldButton != null ? Prefabs.inputFieldButton.GetComponent<Selectable>() : null;
                return source != null ? source.colors : ColorBlock.defaultColorBlock;
            }
        }
        internal static Color PanelColor => Color.Lerp(Colors.normalColor, Color.black, 0.65f);

        internal static Image Panel(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<Image>();
            var source = Prefabs.modListPanel != null
                ? Prefabs.modListPanel.GetComponentsInChildren<Image>(true).FirstOrDefault(item => item.name == "ImagePanel") : null;
            if (source != null)
            {
                image.sprite = source.sprite;
                image.type = source.type;
                image.color = source.color;
                image.material = source.material;
                image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            }
            else
            {
                image.color = PanelColor;
                CopyOutline(rect, Prefabs.inputFieldButton != null
                    ? Prefabs.inputFieldButton.transform.Find("BaseOutline")?.GetComponent<Image>() : null, "BaseOutline");
            }
            return image;
        }

        internal static HGButton Button(RectTransform rect, Image background, GameObject? prefab = null)
        {
            prefab = prefab != null ? prefab : Prefabs.inputFieldButton;
            if (rect.GetComponentInParent<MPEventSystemProvider>() == null)
            {
                var provider = rect.gameObject.AddComponent<MPEventSystemProvider>();
                provider.eventSystem = UnityEngine.EventSystems.EventSystem.current as MPEventSystem
                    ?? RoR2.MPEventSystemManager.primaryEventSystem;
            }
            bool active = rect.gameObject.activeSelf;
            rect.gameObject.SetActive(false);
            var source = prefab != null ? prefab.GetComponent<Image>() : null;
            if (source != null)
            {
                background.sprite = source.sprite;
                background.type = source.type;
            }
            background.color = Color.white;
            var button = rect.gameObject.AddComponent<HGButton>();
            button.targetGraphic = background;
            var selectable = prefab != null ? prefab.GetComponent<Selectable>() : null;
            button.colors = selectable != null ? selectable.colors : Colors;
            if (selectable != null)
            {
                button.transition = selectable.transition;
                button.spriteState = selectable.spriteState;
                button.animationTriggers = selectable.animationTriggers;
            }
            button.allowAllEventSystems = true;
            button.onSelect = new UnityEvent();
            button.onDeselect = new UnityEvent();
            button.onFindSelectableLeft = new UnityEvent();
            button.onFindSelectableRight = new UnityEvent();
            button.onDebugSelect = new UnityEvent();
            CopyOutline(rect, prefab != null ? prefab.transform.Find("BaseOutline")?.GetComponent<Image>() : null, "BaseOutline");
            var nativeButton = prefab != null ? prefab.GetComponent<HGButton>() : null;
            var hover = CopyOutline(rect, nativeButton != null ? nativeButton.imageOnHover : null, "HoverOutline");
            if (hover != null)
            {
                hover.color = new Color(1f, 1f, 1f, 0f);
                button.imageOnHover = hover;
                button.showImageOnHover = true;
            }
            rect.gameObject.SetActive(active);
            return button;
        }

        private static Image? CopyOutline(RectTransform parent, Image? source, string name)
        {
            if (source == null) return null;
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var sourceRect = source.rectTransform;
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.anchoredPosition3D = sourceRect.anchoredPosition3D;
            rect.localRotation = sourceRect.localRotation;
            rect.localScale = sourceRect.localScale;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = source.sprite;
            image.type = source.type;
            image.color = source.color;
            image.material = source.material;
            image.preserveAspect = source.preserveAspect;
            image.fillCenter = source.fillCenter;
            image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            image.raycastTarget = false;
            return image;
        }
    }
}
