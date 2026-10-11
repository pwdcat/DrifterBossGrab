#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using BepInEx.Configuration;
using UnityEngine.UI;

namespace DrifterBossGrabMod.UI
{
    public class HudDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler, IPointerClickHandler
    {
        public ConfigEntry<float>? XConfig;
        public ConfigEntry<float>? YConfig;
        public ConfigEntry<float>? ScaleConfig;
        public Vector2 DragSizePadding = Vector2.zero;
        public Vector2 DragOffset = Vector2.zero;
        public HudElementType ElementType = HudElementType.All;

        private RectTransform? _rectTransform;
        private Image? _highlight;
        private Canvas? _rootCanvas;

        private readonly List<Graphic> _graphics = new();
        private readonly List<Image> _borders = new();
        private readonly Vector3[] _corners = new Vector3[4];
        private static Color NormalHighlightColor => new Color(1f, 1f, 1f, 0.35f);
        private static Color HoverHighlightColor => Color.white;
        private static Color DraggingHighlightColor => GameUiControls.Colors.highlightedColor;

        private bool _isDragging = false;
        public bool IsDragging => _isDragging;
        private bool _isHovering = false;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _rootCanvas = GetComponentInParent<Canvas>();

            var highlightObj = new GameObject("DraggableHighlight");
            highlightObj.transform.SetParent(transform, false);
            _highlight = highlightObj.AddComponent<Image>();
            _highlight.color = new Color(0.1f, 0.18f, 0.24f, 0.025f);
            _highlight.raycastTarget = true;
            var layoutElement = highlightObj.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var highlightRect = _highlight.rectTransform;
            highlightRect.anchorMin = Vector2.zero;
            highlightRect.anchorMax = Vector2.one;
            highlightRect.sizeDelta = DragSizePadding;
            highlightRect.anchoredPosition = DragOffset;

            AddBorder(highlightRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1.5f));
            AddBorder(highlightRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1.5f));
            AddBorder(highlightRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1.5f, 0f));
            AddBorder(highlightRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1.5f, 0f));
            highlightObj.SetActive(false);
        }

        private void Update()
        {
            bool editorActive = HudEditorManager.IsEditorActive;
            if (!editorActive) _isDragging = false;
            if (editorActive) UpdateBounds();
            if (_highlight != null && _highlight.gameObject.activeSelf != editorActive)
            {
                _highlight.gameObject.SetActive(editorActive);
                if (editorActive)
                    _highlight.transform.SetAsLastSibling();

                UpdateHighlightColor();
            }

            if (!_isDragging && _rectTransform != null)
            {

                if (XConfig != null && YConfig != null)
                {
                    _rectTransform.anchoredPosition = new Vector2(XConfig.Value, YConfig.Value);
                }
                if (ScaleConfig != null && ElementType != HudElementType.MainSlot)
                {
                    transform.localScale = Vector3.one * ScaleConfig.Value;
                }
            }

            if (editorActive && _isDragging && ScaleConfig != null)
            {
                float scroll = UnityEngine.Input.mouseScrollDelta.y;
                if (scroll != 0)
                {
                    ApplyScale(scroll);
                }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!HudEditorManager.IsEditorActive) return;

            HudEditorManager.Select(this);
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                HudContextMenu.Show(this);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!HudEditorManager.IsEditorActive) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            HudEditorManager.Select(this);
            _isDragging = true;
            UpdateHighlightColor();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!HudEditorManager.IsEditorActive || _rectTransform == null || _rootCanvas == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;

            Vector2 delta = eventData.delta / _rootCanvas.scaleFactor;
            _rectTransform.anchoredPosition += delta;

            if (XConfig != null) XConfig.Value = _rectTransform.anchoredPosition.x;
            if (YConfig != null) YConfig.Value = _rectTransform.anchoredPosition.y;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _isDragging = false;
            UpdateHighlightColor();

            DrifterBossGrabPlugin.Instance?.Config.Save();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovering = true;
            UpdateHighlightColor();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovering = false;
            UpdateHighlightColor();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!HudEditorManager.IsEditorActive || ScaleConfig == null) return;
            ApplyScale(eventData.scrollDelta.y);
        }

        private void ApplyScale(float scrollDelta)
        {
            if (ScaleConfig == null) return;

            float scaleDelta = scrollDelta * 0.05f;
            float newScale = Mathf.Clamp(ScaleConfig.Value + scaleDelta, 0.2f, 3.0f);

            ScaleConfig.Value = newScale;
            transform.localScale = Vector3.one * newScale;

            DrifterBossGrabPlugin.Instance?.Config.Save();
        }

        private void AddBorder(RectTransform parent, Vector2 min, Vector2 max, Vector2 size)
        {
            var rect = (RectTransform)new GameObject("Outline", typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            _borders.Add(image);
        }

        private void SetBorderColor(Color color)
        {
            foreach (var border in _borders) border.color = color;
        }

        private void UpdateBounds()
        {
            if (_highlight == null || _rectTransform == null) return;
            _graphics.Clear();
            GetComponentsInChildren(false, _graphics);
            bool found = false;
            Bounds bounds = default;
            foreach (var graphic in _graphics)
            {
                if (graphic == null || !graphic.enabled || graphic.color.a <= 0.01f ||
                    graphic.transform.IsChildOf(_highlight.transform)) continue;
                graphic.rectTransform.GetWorldCorners(_corners);
                foreach (var corner in _corners)
                {
                    var local = transform.InverseTransformPoint(corner);
                    if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                    else bounds.Encapsulate(local);
                }
            }
            if (!found) return;
            var rect = _highlight.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(bounds.size.x + 12f, bounds.size.y + 12f);
            rect.anchoredPosition = (Vector2)bounds.center - _rectTransform.rect.center;
            UpdateHighlightColor();
        }

        private void UpdateHighlightColor()
        {
            if (_highlight == null) return;

            if (_isDragging)
                SetBorderColor(DraggingHighlightColor);
            else if (_isHovering)
                SetBorderColor(HoverHighlightColor);
            else
                SetBorderColor(HudEditorManager.Selected == this ? HoverHighlightColor : NormalHighlightColor);
        }
    }
}
