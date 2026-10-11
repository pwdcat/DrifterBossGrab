#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrifterBossGrabMod.UI
{
    internal sealed class CarouselCardExit : MonoBehaviour
    {
        private RectTransform _rect = null!;
        private CanvasGroup _group = null!;
        private Vector2 _startPosition;
        private Vector3 _startScale;
        private Quaternion _startRotation;
        private float _startOpacity;
        private float _duration;
        private float _distance;
        private float _age;
        private bool _destroyed;

        internal static CarouselCardExit Create(GameObject source, bool destroyed)
        {
            var root = CreateSnapshot(source);
            var visual = root.AddComponent<CarouselCardExit>();
            visual._rect = (RectTransform)root.transform;
            visual._group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            visual._group.interactable = false;
            visual._group.blocksRaycasts = false;
            CenterPivot(visual._rect);
            visual._startPosition = visual._rect.anchoredPosition;
            visual._startScale = root.transform.localScale;
            visual._startRotation = root.transform.localRotation;
            visual._startOpacity = visual._group.alpha;
            visual._destroyed = destroyed;
            visual._duration = Mathf.Max(0.05f, destroyed ? PluginConfig.Instance.CardDestructionDuration.Value : PluginConfig.Instance.CardReleaseDuration.Value);
            visual._distance = PluginConfig.Instance.CardReleaseDistance.Value;
            Destroy(root, visual._duration + 0.1f);
            root.SetActive(true);
            return visual;
        }

        internal static GameObject CreateSnapshot(GameObject source)
        {
            var root = new GameObject("Carousel card snapshot", typeof(RectTransform));
            root.SetActive(false);
            root.transform.SetParent(source.transform.parent, false);
            CopyVisuals(source.transform, root.transform);
            var group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            root.AddComponent<LayoutElement>().ignoreLayout = true;
            return root;
        }

        private static void CenterPivot(RectTransform root)
        {
            var corners = new Vector3[4];
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.enabled || !graphic.gameObject.activeSelf || graphic.color.a <= 0f) continue;
                graphic.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = root.InverseTransformPoint(corner);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else bounds.Encapsulate(point);
                }
            }
            var size = root.rect.size;
            if (!hasBounds || Mathf.Abs(size.x) < 0.001f || Mathf.Abs(size.y) < 0.001f) return;
            Vector3 center = bounds.center;
            center.z = 0f;
            Vector3 position = root.localPosition;
            root.pivot += new Vector2(center.x / size.x, center.y / size.y);
            root.localPosition = position + root.localRotation * Vector3.Scale(center, root.localScale);
        }

        private static void CopyVisuals(Transform source, Transform target)
        {
            var from = source as RectTransform;
            var to = (RectTransform)target;
            if (from != null)
            {
                to.anchorMin = from.anchorMin;
                to.anchorMax = from.anchorMax;
                to.pivot = from.pivot;
                to.sizeDelta = from.sizeDelta;
                to.anchoredPosition3D = from.anchoredPosition3D;
            }
            to.localScale = source.localScale;
            to.localRotation = source.localRotation;
            var sourceGroup = source.GetComponent<CanvasGroup>();
            if (sourceGroup != null) target.gameObject.AddComponent<CanvasGroup>().alpha = sourceGroup.alpha;
            var graphic = source.GetComponent<Graphic>();
            Graphic? copy = null;
            if (graphic != null && graphic.enabled)
            {
                if (graphic is Image image)
                {
                    var result = target.gameObject.AddComponent<Image>();
                    result.sprite = image.sprite;
                    result.type = image.type;
                    result.preserveAspect = image.preserveAspect;
                    result.fillMethod = image.fillMethod;
                    result.fillOrigin = image.fillOrigin;
                    result.fillAmount = image.fillAmount;
                    result.fillClockwise = image.fillClockwise;
                    result.fillCenter = image.fillCenter;
                    result.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
                    copy = result;
                }
                else if (graphic is RawImage raw)
                {
                    var result = target.gameObject.AddComponent<RawImage>();
                    result.texture = raw.texture;
                    result.uvRect = raw.uvRect;
                    copy = result;
                }
                else if (graphic is TextMeshProUGUI text)
                {
                    var result = target.gameObject.AddComponent<TextMeshProUGUI>();
                    result.font = text.font;
                    result.fontSharedMaterial = text.fontSharedMaterial;
                    result.text = text.text;
                    result.fontSize = text.fontSize;
                    result.fontStyle = text.fontStyle;
                    result.alignment = text.alignment;
                    result.margin = text.margin;
                    result.richText = text.richText;
                    result.enableWordWrapping = text.enableWordWrapping;
                    result.overflowMode = text.overflowMode;
                    result.enableAutoSizing = text.enableAutoSizing;
                    result.fontSizeMin = text.fontSizeMin;
                    result.fontSizeMax = text.fontSizeMax;
                    result.characterSpacing = text.characterSpacing;
                    result.wordSpacing = text.wordSpacing;
                    copy = result;
                }
                if (copy != null)
                {
                    copy.color = graphic.color;
                    copy.raycastTarget = false;
                }
            }
            foreach (Transform child in source)
            {
                if (!child.gameObject.activeSelf) continue;
                var node = new GameObject(child.name, typeof(RectTransform));
                node.transform.SetParent(target, false);
                CopyVisuals(child, node.transform);
            }
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_age / _duration);
            if (_destroyed)
            {
                float eased = t * t;
                _rect.localScale = Vector3.Scale(_startScale, new Vector3(1f - 0.3f * eased, 1f - 0.8f * eased, 1f));
                _group.alpha = _startOpacity * (1f - eased);
            }
            else
            {
                float travel = 1f - (1f - t) * (1f - t);
                _rect.anchoredPosition = _startPosition + new Vector2(_distance * travel, 12f * Mathf.Sin(t * Mathf.PI * 0.85f));
                _rect.localRotation = _startRotation * Quaternion.Euler(0f, 0f, -4f * travel);
                _rect.localScale = _startScale * (1f - 0.08f * travel);
                _group.alpha = _startOpacity * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, t)));
            }
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
