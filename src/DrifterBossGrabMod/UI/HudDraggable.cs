#nullable enable
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

        private static Color NormalHighlightColor = new Color(1f, 1f, 1f, 0.2f);
        private static Color HoverHighlightColor = new Color(1f, 1f, 1f, 0.5f);
        private static Color DraggingHighlightColor = new Color(1f, 1f, 0f, 0.4f);

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
            _highlight.color = NormalHighlightColor;
            _highlight.raycastTarget = true;
            var layoutElement = highlightObj.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var highlightRect = _highlight.rectTransform;
            highlightRect.anchorMin = Vector2.zero;
            highlightRect.anchorMax = Vector2.one;
            highlightRect.sizeDelta = DragSizePadding;
            highlightRect.anchoredPosition = DragOffset;

            highlightObj.SetActive(false);
        }

        private void Update()
        {
            bool editorActive = HudEditorManager.IsEditorActive;
            if (_highlight != null && _highlight.gameObject.activeSelf != editorActive)
            {
                _highlight.gameObject.SetActive(editorActive);
                if (editorActive)
                    _highlight.transform.SetAsLastSibling();

                _highlight.rectTransform.sizeDelta = DragSizePadding;
                _highlight.rectTransform.anchoredPosition = DragOffset;

                UpdateHighlightColor();
            }

            if (!_isDragging && _rectTransform != null)
            {

                if (XConfig != null && YConfig != null)
                {
                    _rectTransform.anchoredPosition = new Vector2(XConfig.Value, YConfig.Value);
                }
                if (ScaleConfig != null)
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

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                HudContextMenu.Show(this);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!HudEditorManager.IsEditorActive) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
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

        private void UpdateHighlightColor()
        {
            if (_highlight == null) return;

            if (_isDragging)
                _highlight.color = DraggingHighlightColor;
            else if (_isHovering)
                _highlight.color = HoverHighlightColor;
            else
                _highlight.color = NormalHighlightColor;
        }
    }
}
