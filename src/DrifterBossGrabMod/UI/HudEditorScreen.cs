#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DrifterBossGrabMod.UI
{
    public class HudEditorScreen : MonoBehaviour, IConfigObserver
    {
        private sealed class PreviewCard
        {
            public GameObject Root = null!;
            public TMP_Text? Name;
            public RawImage? Portrait;
            public GameObject? Health;
            public DamagePreviewOverlay? DamageOverlay;
            public GameObject? Weight;
            public Image? Background;
            public CanvasGroup Group = null!;
            public bool Animating;
            public float HiddenUntil;
            public float SampleMass;
            public float SampleFraction;
        }

        private static readonly System.Reflection.FieldInfo? DefaultHealthStyle = AccessTools.Field(typeof(HealthBar), "_style");
        private static readonly System.Reflection.FieldInfo? AllyHealthStyle = AccessTools.Field(typeof(HealthBar), "_allyStyle");
        private readonly List<PreviewCard> _cards = new();
        private readonly List<(CanvasGroup Group, float Alpha, bool Added)> _hidden = new();
        private readonly List<(CanvasGroup Group, float Alpha, bool Interactable, bool BlocksRaycasts, bool Added)> _hiddenMenus = new();
        private GameObject? _createdPauseScreen;
        private RectTransform? _carousel;
        private TMP_Text _selection = null!;
        private TMP_Text _layoutLabel = null!;
        private TMP_Text _status = null!;
        private Button _reset = null!;
        private const int PreviewCardLimit = 100;
        private GameObject? _cardTemplate;
        private GameObject? _previewPanel;
        private GameObject? _cardControls;
        private GameObject? _capacityControls;
        private TMP_InputField? _countInput;
        private TMP_InputField? _loadInput;
        private Slider? _loadSlider;
        private float _previewLoad = 2f / 3f;
        private int _cardCount = 3;
        private int _selectedCard;
        private readonly List<Coroutine> _previewAnimations = new();
        private readonly List<GameObject> _previewGhosts = new();
        private bool _ready;
        private MassCapacityUIController? _meterPreview;
        private GameObject? _liveBackdrop;
        private bool _usingLiveHud;
        private AsyncOperationHandle<GameObject> _bagAsset;
        private AsyncOperationHandle<GameObject> _capacityAsset;
        private AsyncOperationHandle<GameObject> _hudAsset;

        private void Start()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var topCanvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(item => item != canvas && item.isRootCanvas)
                .OrderByDescending(item => SortingLayer.GetLayerValueFromID(item.sortingLayerID))
                .ThenByDescending(item => item.sortingOrder).FirstOrDefault();
            canvas.sortingLayerID = topCanvas != null ? topCanvas.sortingLayerID : 0;
            canvas.sortingOrder = topCanvas != null ? topCanvas.sortingOrder + 10 : 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            bool isInGame = RoR2.Run.instance != null;
            var background = Rect("Backdrop", transform);
            Stretch(background);
            GameUiControls.Panel(background);
            if (isInGame)
            {
                var hud = HUD.readOnlyInstanceList.FirstOrDefault(item => item != null && item.localUserViewer != null);
                if (hud != null && hud.mainContainer != null)
                {
                    _usingLiveHud = true;
                    background.gameObject.SetActive(false);
                    var dim = Rect("Drifter editor backdrop", hud.mainContainer.transform);
                    Stretch(dim);
                    dim.SetAsFirstSibling();
                    var image = GameUiControls.Panel(dim);
                    var tint = image.color;
                    tint.a = 0.78f;
                    image.color = tint;
                    image.raycastTarget = false;
                    _liveBackdrop = dim.gameObject;
                }
            }
            HideGameMenus(isInGame);

            foreach (var draggable in _usingLiveHud ? System.Array.Empty<HudDraggable>() : Object.FindObjectsByType<HudDraggable>(FindObjectsSortMode.None))
            {
                var group = draggable.GetComponent<CanvasGroup>();
                bool added = group == null;
                if (group == null) group = draggable.gameObject.AddComponent<CanvasGroup>();
                _hidden.Add((group, group.alpha, added));
            }
            var toolbar = Rect("Toolbar", transform);
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = Vector2.one;
            toolbar.pivot = new Vector2(0.5f, 1f);
            toolbar.sizeDelta = new Vector2(0f, 72f);
            GameUiControls.Panel(toolbar);
            Label(toolbar, "HUD editor", new Vector2(24f, -18f), new Vector2(210f, 32f), 28f);
            _selection = Label(toolbar, "", new Vector2(250f, -20f), new Vector2(400f, 28f), 20f);
            _status = Label(toolbar, "", new Vector2(24f, -62f), new Vector2(820f, 28f), 18f);
            _reset = CreateButton(toolbar, "Reset selected", new Vector2(-400f, -18f), () => ResetSelected());
            var layout = CreateButton(toolbar, "", new Vector2(-220f, -18f), () =>
            {
                var entry = PluginConfig.Instance.CarouselOrientation;
                entry.Value = entry.Value == CarouselOrientation.Vertical ? CarouselOrientation.Horizontal : CarouselOrientation.Vertical;
            });
            _layoutLabel = layout.GetComponentInChildren<TMP_Text>();
            CreateButton(toolbar, "Done", new Vector2(-40f, -18f), () => HudEditorManager.SetEditorActive(false));
            if (!_usingLiveHud) CreatePreviewPanel();
            SceneManager.activeSceneChanged += OnSceneChanged;
            ConfigChangeNotifier.AddObserver(this);
            if (!_usingLiveHud) StartCoroutine(LoadPreview());
        }

        private void HideGameMenus(bool isInGame)
        {
            if (isInGame && PauseScreenController.instancesList.Count == 0)
                _createdPauseScreen = PauseScreenController.InstantiatePauseScreen(true);
            foreach (var pause in PauseScreenController.instancesList)
                if (pause != null && pause.gameObject.activeInHierarchy) HideMenu(pause.gameObject);
            foreach (var panel in Object.FindObjectsByType<RiskOfOptions.Components.Panel.ModOptionPanelController>(FindObjectsSortMode.None))
                if (panel != null && panel.gameObject.activeInHierarchy) HideMenu(panel.gameObject);
        }

        private void HideMenu(GameObject menu)
        {
            var root = menu.GetComponent<CanvasGroup>();
            bool added = root == null;
            if (root == null) root = menu.AddComponent<CanvasGroup>();
            foreach (var group in menu.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (group != root && !group.ignoreParentGroups) continue;
                if (_hiddenMenus.Any(item => item.Group == group)) continue;
                _hiddenMenus.Add((group, group.alpha, group.interactable, group.blocksRaycasts, added && group == root));
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        private void RestoreGameMenus()
        {
            foreach (var item in _hiddenMenus)
            {
                if (item.Group == null) continue;
                item.Group.alpha = item.Alpha;
                item.Group.interactable = item.Interactable;
                item.Group.blocksRaycasts = item.BlocksRaycasts;
                if (item.Added) Destroy(item.Group);
            }
            _hiddenMenus.Clear();
            DrifterBossGrabPlugin.Instance?.UpdateHudSubTabVisibility();
            if (_createdPauseScreen != null) Destroy(_createdPauseScreen);
            _createdPauseScreen = null;
        }

        private IEnumerator LoadPreview()
        {
            var bag = _bagAsset = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Bag UI.prefab");
            var capacity = _capacityAsset = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/Junk UI.prefab");
            var hud = _hudAsset = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/UI/HUDSimple.prefab");
            try
            {
                yield return hud;
                yield return bag;
                yield return capacity;
                if (hud.Status != AsyncOperationStatus.Succeeded || hud.Result == null || bag.Status != AsyncOperationStatus.Succeeded || bag.Result == null ||
                    capacity.Status != AsyncOperationStatus.Succeeded || capacity.Result == null)
                {
                    _status.text = "Could not load HUD preview assets. Close and try again.";
                    yield break;
                }
                var preview = Rect("Preview", transform);
                Stretch(preview);
                preview.SetSiblingIndex(1);
                preview.gameObject.SetActive(false);
                var nativeHud = hud.Result.GetComponent<HUD>();
                var mainContainer = nativeHud != null ? nativeHud.mainContainer : null;
                var display = mainContainer != null ? mainContainer.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(rect => rect.name == "DisplayRoot") : null;
                var bottom = mainContainer != null ? MassCapacityUIController.GetCapacityParent(mainContainer.transform) as RectTransform : null;
                if (display == null || bottom == null)
                {
                    _status.text = "HUD layout anchors were not found. Close and try again.";
                    yield break;
                }
                var nativeScaler = hud.Result.GetComponentInChildren<CanvasScaler>(true);
                if (nativeScaler != null)
                {
                    var scaler = GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = nativeScaler.uiScaleMode;
                    scaler.referenceResolution = nativeScaler.referenceResolution;
                    scaler.screenMatchMode = nativeScaler.screenMatchMode;
                    scaler.matchWidthOrHeight = nativeScaler.matchWidthOrHeight;
                    scaler.referencePixelsPerUnit = nativeScaler.referencePixelsPerUnit;
                }
                _carousel = Rect("Carousel", CopyAnchorPath(display, hud.Result.transform, preview));
                var drag = _carousel.gameObject.AddComponent<HudDraggable>();
                drag.ElementType = HudElementType.MainSlot;
                drag.DragSizePadding = new Vector2(75, 50);
                drag.DragOffset = new Vector2(-40, -20);
                drag.XConfig = PluginConfig.Instance.CenterSlotX;
                drag.YConfig = PluginConfig.Instance.CenterSlotY;
                drag.ScaleConfig = PluginConfig.Instance.CenterSlotScale;
                _cardTemplate = Object.Instantiate(bag.Result, preview, false);
                _cardTemplate.name = "Preview card template";
                _cardTemplate.SetActive(false);
                for (int i = 0; i < _cardCount; i++) CreateCard(_cardTemplate, i);
                var bottomCenter = CopyAnchorPath(bottom, hud.Result.transform, preview);
                var meter = Object.Instantiate(capacity.Result, bottomCenter, false);
                StripRuntime(meter);
                var meterHost = Rect("Capacity preview renderer", preview);
                _meterPreview = meterHost.gameObject.AddComponent<MassCapacityUIController>();
                _meterPreview.InitializeEditorPreview(meter);
                var meterDrag = meter.AddComponent<HudDraggable>();
                meterDrag.ElementType = HudElementType.CapacityUI;
                meterDrag.XConfig = PluginConfig.Instance.MassCapacityUIPositionX;
                meterDrag.YConfig = PluginConfig.Instance.MassCapacityUIPositionY;
                meterDrag.ScaleConfig = PluginConfig.Instance.MassCapacityUIScale;
                preview.gameObject.SetActive(true);
                _ready = true;
                _status.text = "";
            }
            finally
            {
                ReleaseAssets();
            }
        }

        private void CreateCard(GameObject prefab, int index)
        {
            var root = Object.Instantiate(prefab, _carousel, false);
            var native = root.GetComponentInChildren<BaggedCardController>(true);
            var preview = new PreviewCard
            {
                Root = root,
                Group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>(),
                SampleMass = index == 0 ? 100f : (index == 1 ? 300f : 200f),
                SampleFraction = index == 0 ? 0.25f : (index == 1 ? 0.75f : 0.5f)
            };
            if (native != null)
            {
                preview.Name = native.nameLabel;
                preview.Portrait = native.portraitIconImage;
                preview.Health = native.healthBar != null ? native.healthBar.gameObject : null;
                preview.Background = native.GetComponent<Image>();
                if (native.droneItemIndicatorObj != null) native.droneItemIndicatorObj.SetActive(false);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.gameObject != root &&
                    (t.name.IndexOf("drone", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     t.name.IndexOf("indicator", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    t.gameObject.SetActive(false);
                }
            }

            var locator = root.GetComponent<ChildLocator>();
            preview.Weight = locator != null ? locator.FindChild("WeightIcon")?.gameObject : null;
            var bodyPrefab = BodyCatalog.FindBodyPrefab(new[] { "GolemBody", "TitanBody", "VagrantBody" }[index % 3]);
            var body = bodyPrefab != null ? bodyPrefab.GetComponent<CharacterBody>() : null;
            if (preview.Name != null) preview.Name.text = body != null ? Language.GetString(body.baseNameToken) : "Sample passenger";
            if (preview.Portrait != null && body != null) preview.Portrait.texture = body.portraitIcon;

            bool isCenter = index == 0;
            bool showWeight = isCenter ? PluginConfig.Instance.CenterSlotShowWeightIcon.Value : PluginConfig.Instance.SideSlotShowWeightIcon.Value;
            if (preview.Weight != null) preview.Weight.SetActive(showWeight);

            int displayNumber = isCenter ? 1 : (index == 1 ? 2 : 3);
            bool showSlotNumber = isCenter ? PluginConfig.Instance.CenterSlotShowSlotNumber.Value : PluginConfig.Instance.SideSlotShowSlotNumber.Value;
            BaggedObjectCarousel.SetSlotNumberBadge(root, displayNumber, isCenter, showSlotNumber);
            BaggedObjectCarousel.UpdateWeightIconVisuals(root, preview.SampleMass, isCenter, preview.SampleFraction, false);

            var health = native != null ? native.healthBar : null;
            if (health != null)
            {
                var sample = new GameObject("HUD preview durability");
                sample.SetActive(false);
                sample.transform.SetParent(transform, false);
                var durability = sample.AddComponent<SpecialObjectAttributes>();
                durability.enabled = false;
                durability.maxDurability = 8;
                durability.durability = 8;
                health.source = null;
                health.altSource = durability;
                BaggedObjectCarousel.ResetHealthBarWithoutSource(health);
                if (AllyHealthStyle?.GetValue(health) is HealthBarStyle allyStyle)
                    DefaultHealthStyle?.SetValue(health, allyStyle);
            }

            StripRuntime(root);
            if (health != null)
            {
                health.enabled = true;
                preview.DamageOverlay = health.gameObject.AddComponent<DamagePreviewOverlay>();
                preview.DamageOverlay.enabled = false;
            }
            root.SetActive(true);
            _cards.Add(preview);
        }

        private static void StripRuntime(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is ChildLocator) continue;
                if (behaviour is HealthBar) continue;
                if (behaviour is BaggedCardController)
                {
                    behaviour.enabled = false;
                    continue;
                }
                if (!(behaviour is UIBehaviour)) behaviour.enabled = false;
            }
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var cg in root.GetComponentsInChildren<CanvasGroup>(true)) cg.ignoreParentGroups = false;
        }

        private void LateUpdate()
        {
            foreach (var hidden in _hidden) if (hidden.Group != null) hidden.Group.alpha = 0f;
            var selected = HudEditorManager.Selected;
            _selection.text = selected != null ? selected.ElementType.ToString() : "";
            _reset.interactable = selected != null;
            var cfg = PluginConfig.Instance;
            _layoutLabel.text = "Layout: " + cfg.CarouselOrientation.Value;
            if (_previewPanel != null)
            {
                bool capacitySelected = selected != null && selected.ElementType == HudElementType.CapacityUI;
                _cardControls?.SetActive(!capacitySelected);
                _capacityControls?.SetActive(capacitySelected);
                _status.gameObject.SetActive(_status.text.Length > 0);
                if (_loadSlider != null)
                {
                    float max = 200f;
                    _loadSlider.maxValue = max;
                    _previewLoad = Mathf.Clamp(_previewLoad, 0f, max / 100f);
                    _loadSlider.SetValueWithoutNotify(_previewLoad * 100f);
                    if (_loadInput != null && !_loadInput.isFocused)
                        _loadInput?.SetTextWithoutNotify((_previewLoad * 100f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (_meterPreview != null) _meterPreview.UpdateEditorPreview(_previewLoad, _cardCount);
            if (!_ready || _carousel == null) return;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (i >= _cardCount)
                {
                    card.Root.SetActive(false);
                    continue;
                }
                int state = BaggedObjectCarousel.GetPassengerState(i, _selectedCard, _cardCount);
                bool center = state == 0;
                var p = BaggedObjectCarousel.GetStateParams(state, _cardCount);
                card.Root.SetActive(p.opacity > 0f || card.Animating);
                var rect = (RectTransform)card.Root.transform;
                if (!card.Animating)
                {
                    rect.anchoredPosition = p.pos;
                    rect.localScale = Vector3.one * p.scale;
                    card.Group.alpha = p.opacity;
                }
                if (Time.unscaledTime < card.HiddenUntil) card.Group.alpha = 0f;
                if (!card.Root.activeSelf) continue;

                if (card.Name != null) card.Name.gameObject.SetActive(center ? cfg.CenterSlotShowName.Value : cfg.SideSlotShowName.Value);
                if (card.Portrait != null) card.Portrait.enabled = center ? cfg.CenterSlotShowIcon.Value : cfg.SideSlotShowIcon.Value;
                if (card.Background != null) card.Background.enabled = center ? cfg.CenterSlotShowBackground.Value : cfg.SideSlotShowBackground.Value;

                bool showWeight = center ? cfg.CenterSlotShowWeightIcon.Value : cfg.SideSlotShowWeightIcon.Value;
                if (card.Weight != null) card.Weight.SetActive(showWeight);
                if (showWeight)
                {
                    BaggedObjectCarousel.UpdateWeightIconVisuals(card.Root, card.SampleMass, center, card.SampleFraction, false);
                }

                int displayNumber = i + 1;
                bool showBadge = center ? cfg.CenterSlotShowSlotNumber.Value : cfg.SideSlotShowSlotNumber.Value;
                BaggedObjectCarousel.SetSlotNumberBadge(card.Root, displayNumber, center, showBadge);

                bool showHealth = center ? cfg.CenterSlotShowHealthBar.Value : cfg.SideSlotShowHealthBar.Value;
                if (card.Health != null) card.Health.SetActive(showHealth);
                if (card.DamageOverlay != null)
                {
                    bool showDmg = showHealth && (center || cfg.AoEDamageDistribution.Value != AoEDamageMode.None);
                    card.DamageOverlay.RenderEditorPreview(card.SampleFraction, showDmg);
                }
            }
        }

        private void CreatePreviewPanel()
        {
            var panel = Rect("Preview controls", transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -90f);
            panel.sizeDelta = new Vector2(584f, 140f);
            GameUiControls.Panel(panel);
            _previewPanel = panel.gameObject;
            Label(panel, "Preview", new Vector2(16f, -10f), new Vector2(160f, 28f), 22f);
            var cards = Rect("Cards", panel);
            Stretch(cards);
            _cardControls = cards.gameObject;
            Label(cards, "Count", new Vector2(16f, -52f), new Vector2(80f, 32f), 20f);
            _countInput = NumberField(cards, new Vector2(96f, -48f), new Vector2(88f, 36f), true);
            _countInput.text = _cardCount.ToString();
            _countInput.onEndEdit.AddListener(value =>
            {
                if (int.TryParse(value, out int count))
                {
                    CancelPreviewAnimations();
                    _cardCount = Mathf.Clamp(count, 1, PreviewCardLimit);
                    _selectedCard %= _cardCount;
                    if (_cardTemplate != null)
                        while (_cards.Count < _cardCount) CreateCard(_cardTemplate, _cards.Count);
                }
                _countInput?.SetTextWithoutNotify(_cardCount.ToString());
            });
            CreateButton(cards, "Cycle", new Vector2(-396f, -94f), CyclePreview);
            CreateButton(cards, "Throw", new Vector2(-208f, -94f), () => ExitPreview(false));
            CreateButton(cards, "Destroy", new Vector2(-20f, -94f), () => ExitPreview(true));
            var capacity = Rect("Capacity", panel);
            Stretch(capacity);
            _capacityControls = capacity.gameObject;
            Label(capacity, "Load %", new Vector2(16f, -52f), new Vector2(90f, 32f), 20f);
            _loadInput = NumberField(capacity, new Vector2(110f, -48f), new Vector2(88f, 36f), false);
            _loadInput.onEndEdit.AddListener(value =>
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float load)
                    && !float.IsNaN(load) && !float.IsInfinity(load))
                    _previewLoad = Mathf.Clamp(load, 0f, _loadSlider != null ? _loadSlider.maxValue : 100f) / 100f;
                _loadInput?.SetTextWithoutNotify((_previewLoad * 100f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            });
            _loadSlider = PreviewSlider(capacity);
            _loadSlider.onValueChanged.AddListener(value => _previewLoad = value / 100f);
            capacity.gameObject.SetActive(false);
        }

        private static TMP_InputField NumberField(Transform parent, Vector2 position, Vector2 size, bool whole)
        {
            var rect = Rect("Number", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.gameObject.SetActive(false);
            var image = rect.gameObject.AddComponent<Image>();
            var native = RiskOfOptions.Resources.Prefabs.inputFieldButton.GetComponent<Image>();
            image.sprite = native.sprite;
            image.type = native.type;
            var area = Rect("Text area", rect);
            Stretch(area);
            area.offsetMin = new Vector2(8f, 2f);
            area.offsetMax = new Vector2(-8f, -2f);
            area.gameObject.AddComponent<RectMask2D>();
            var text = area.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = HGTextMeshProUGUI.defaultLanguageFont;
            text.fontSize = 20f;
            text.alignment = TextAlignmentOptions.MidlineRight;
            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.targetGraphic = image;
            field.colors = GameUiControls.Colors;
            field.contentType = whole ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
            field.characterLimit = 6;
            rect.gameObject.SetActive(true);
            return field;
        }

        private static Slider PreviewSlider(Transform parent)
        {
            var rect = Rect("Load", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -98f);
            rect.sizeDelta = new Vector2(548f, 28f);
            var track = Rect("Track", rect);
            Stretch(track);
            track.offsetMin = new Vector2(0f, 10f);
            track.offsetMax = new Vector2(0f, -10f);
            track.gameObject.AddComponent<Image>().color = GameUiControls.Colors.normalColor;
            var handleArea = Rect("Handle area", rect);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);
            var handle = Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(16f, 24f);
            var graphic = handle.gameObject.AddComponent<Image>();
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.targetGraphic = graphic;
            slider.handleRect = handle;
            slider.colors = GameUiControls.Colors;
            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 200f;
            return slider;
        }

        public void OnConfigChanged(string key, object value)
        {
            CancelPreviewAnimations();
        }

        private void CancelPreviewAnimations()
        {
            foreach (var animation in _previewAnimations) if (animation != null) StopCoroutine(animation);
            _previewAnimations.Clear();
            foreach (var ghost in _previewGhosts) if (ghost != null) Destroy(ghost);
            _previewGhosts.Clear();
            foreach (var card in _cards) card.Animating = false;
        }

        private void CyclePreview()
        {
            if (!_ready || _cards.Count == 0) return;
            CancelPreviewAnimations();
            int previousSelection = _selectedCard;
            _selectedCard = (_selectedCard + 1) % _cardCount;
            for (int i = 0; i < _cardCount; i++)
            {
                var card = _cards[i];
                int previous = BaggedObjectCarousel.GetPassengerState(i, previousSelection, _cardCount);
                int target = BaggedObjectCarousel.GetPassengerState(i, _selectedCard, _cardCount);
                var p = BaggedObjectCarousel.GetStateParams(target, _cardCount);
                bool wraps = _cardCount > 1 && target != previous - 1;
                if (wraps)
                {
                    if (card.Root.activeSelf && card.Group.alpha > 0f)
                    {
                        var ghost = CarouselCardExit.CreateSnapshot(card.Root);
                        ghost.SetActive(true);
                        _previewGhosts.Add(ghost);
                        int exit = BaggedObjectCarousel.GetCycleExitState(previous, 1, _cardCount);
                        _previewAnimations.Add(StartCoroutine(AnimatePreviewGhost(ghost, exit)));
                    }
                    if (p.opacity > 0f)
                    {
                        var incoming = BaggedObjectCarousel.GetStateParams(target + 1, _cardCount);
                        var rect = (RectTransform)card.Root.transform;
                        rect.anchoredPosition = incoming.pos;
                        rect.localScale = Vector3.one * incoming.scale;
                        card.Group.alpha = 0f;
                    }
                    else
                    {
                        card.Root.SetActive(false);
                        continue;
                    }
                }
                if (!card.Root.activeSelf && p.opacity <= 0f) continue;
                card.Root.SetActive(true);
                _previewAnimations.Add(StartCoroutine(AnimatePreviewCard(card, target)));
            }
        }

        private IEnumerator AnimatePreviewCard(PreviewCard card, int target)
        {
            card.Animating = true;
            var p = BaggedObjectCarousel.GetStateParams(target, _cardCount);
            yield return BaggedObjectCarousel.AnimateCardVisuals(card.Root, p.pos.x, p.pos.y, p.scale, p.opacity, true, true);
            card.Animating = false;
        }

        private IEnumerator AnimatePreviewGhost(GameObject ghost, int target)
        {
            var p = BaggedObjectCarousel.GetStateParams(target, _cardCount);
            yield return BaggedObjectCarousel.AnimateCardVisuals(ghost, p.pos.x, p.pos.y, p.scale, 0f, true, true);
            if (ghost != null) Destroy(ghost);
        }

        private void ExitPreview(bool destroyed)
        {
            if (!_ready || _cards.Count <= _selectedCard) return;
            var card = _cards[_selectedCard];
            if (Time.unscaledTime < card.HiddenUntil) return;
            bool enabled = destroyed ? PluginConfig.Instance.EnableCardDestructionAnimation.Value : PluginConfig.Instance.EnableCardReleaseAnimation.Value;
            if (!enabled) return;
            CarouselCardExit.Create(card.Root, destroyed);
            card.HiddenUntil = Time.unscaledTime + (destroyed ? PluginConfig.Instance.CardDestructionDuration.Value : PluginConfig.Instance.CardReleaseDuration.Value) + 0.15f;
        }

        private static void ResetSelected()
        {
            var selected = HudEditorManager.Selected;
            if (selected == null) return;
            Reset(selected.XConfig);
            Reset(selected.YConfig);
            Reset(selected.ScaleConfig);
        }

        private static void Reset(ConfigEntry<float>? entry) { if (entry != null) entry.Value = (float)entry.DefaultValue; }
        private static void OnSceneChanged(Scene oldScene, Scene newScene) => HudEditorManager.SetEditorActive(false);

        private void OnDestroy()
        {
            ConfigChangeNotifier.RemoveObserver(this);
            CancelPreviewAnimations();
            RestoreGameMenus();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (_liveBackdrop != null) Destroy(_liveBackdrop);
            StopAllCoroutines();
            ReleaseAssets();
            foreach (var hidden in _hidden)
            {
                if (hidden.Group == null) continue;
                hidden.Group.alpha = hidden.Alpha;
                if (hidden.Added) Destroy(hidden.Group);
            }
        }

        private void ReleaseAssets()
        {
            if (_bagAsset.IsValid()) Addressables.Release(_bagAsset);
            if (_capacityAsset.IsValid()) Addressables.Release(_capacityAsset);
            if (_hudAsset.IsValid()) Addressables.Release(_hudAsset);
            _bagAsset = default;
            _capacityAsset = default;
            _hudAsset = default;
        }

        private static RectTransform CopyAnchorPath(RectTransform source, Transform root, RectTransform parent)
        {
            var ancestors = new Stack<RectTransform>();
            for (Transform? current = source; current != null && current != root; current = current.parent)
                if (current is RectTransform rect) ancestors.Push(rect);
            while (ancestors.Count > 0)
            {
                var original = ancestors.Pop();
                var copy = Rect(original.name, parent);
                copy.anchorMin = original.anchorMin;
                copy.anchorMax = original.anchorMax;
                copy.pivot = original.pivot;
                copy.sizeDelta = original.sizeDelta;
                copy.anchoredPosition3D = original.anchoredPosition3D;
                copy.localScale = original.localScale;
                copy.localRotation = original.localRotation;
                parent = copy;
            }
            return parent;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text Label(Transform parent, string text, Vector2 position, Vector2 size, float fontSize)
        {
            var rect = Rect(text, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var label = rect.gameObject.AddComponent<HGTextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(Transform parent, string text, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(text, parent);
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(164f, 40f);
            var image = rect.gameObject.AddComponent<Image>();
            var button = GameUiControls.Button(rect, image);
            button.onClick.AddListener(action);
            var label = Label(rect, text, Vector2.zero, Vector2.zero, 18f);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
