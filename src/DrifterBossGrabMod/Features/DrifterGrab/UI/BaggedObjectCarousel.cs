#nullable enable
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.Patches;
using UnityEngine.AddressableAssets;
using TMPro;
using System.Reflection;
using System.IO;
using System.Collections;
using System.Linq;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.UI
{

    // ========================================================================================
    // BAGGED OBJECT CAROUSEL
    // ========================================================================================
    public class BaggedObjectCarousel : MonoBehaviour, IConfigObserver
    {

        // ========================================================================================
        // RESOURCE LOADING
        // ========================================================================================
        public GameObject? slotPrefab;

        public float sideScale = 0.8f;

        private static Texture2D? _weightIconTexture;
        private static Texture2D? WeightIconTexture => _weightIconTexture ??= LoadWeightIconTexture();
        private static Sprite? _newWeightIconSprite;
        private static Sprite? NewWeightIconSprite => _newWeightIconSprite ??= (WeightIconTexture != null ? Sprite.Create(WeightIconTexture, new Rect(0, 0, WeightIconTexture.width, WeightIconTexture.height), new Vector2(0.5f, 0.5f)) : null);
        private static Sprite? _oldWeightIconSprite;
        private static Sprite OldWeightIconSprite => _oldWeightIconSprite ??= Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/texMovespeedBuffIcon.tif").WaitForCompletion();
        private static Sprite? _overencumbranceIconSprite;
        private static Sprite? OverencumbranceIconSprite => _overencumbranceIconSprite ??= LoadOverencumbranceIconSprite();
        private static Sprite? LoadOverencumbranceIconSprite()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("DrifterBossGrabMod.Resources.Arrow.png"))
            {
                if (stream == null)
                {
                    Debug.LogError("Could not find embedded resource: DrifterBossGrabMod.Resources.Arrow.png");
                    return null;
                }
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2);
                texture.LoadImage(bytes);
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
        }
        private static Texture2D? LoadWeightIconTexture()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("DrifterBossGrabMod.WeightIcon.png"))
            {
                if (stream == null)
                {
                    Debug.LogError("Could not find embedded resource: DrifterBossGrabMod.WeightIcon.png");
                    return null;
                }
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2);
                texture.LoadImage(bytes);
                return texture;
            }
        }

        private CanvasGroup? _rootCanvasGroup;
        private float _timeSinceLastActivity = 0f;

        public void ResetInactivityTimer()
        {
            _timeSinceLastActivity = 0f;
            if (_rootCanvasGroup != null && !PluginConfig.Instance.EnableCarouselInactivityFade.Value)
            {
                _rootCanvasGroup.alpha = 1f;
            }
        }

        private void OnEnable()
        {
            _rootCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            ResetInactivityTimer();
            BagCarouselUpdater.ActiveCarousels.Add(this);
            ConfigChangeNotifier.AddObserver(this);
        }

        private void OnDisable()
        {
            BagCarouselUpdater.ActiveCarousels.Remove(this);
            ConfigChangeNotifier.RemoveObserver(this);
        }

        private void Update()
        {
            if (_rootCanvasGroup == null)
            {
                _rootCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            }

            bool fadeEnabled = PluginConfig.Instance.EnableCarouselInactivityFade.Value;
            bool isEditorActive = HudEditorManager.IsEditorActive;

            if (!fadeEnabled || isEditorActive)
            {
                _timeSinceLastActivity = 0f;
                _rootCanvasGroup.alpha = 1f;
                return;
            }

            _timeSinceLastActivity += Time.deltaTime;
            float delay = PluginConfig.Instance.CarouselInactivityFadeDelay.Value;
            float duration = PluginConfig.Instance.CarouselInactivityFadeDuration.Value;
            float targetOpacity = PluginConfig.Instance.CarouselInactivityFadeOpacity.Value;

            if (_timeSinceLastActivity < delay)
            {
                _rootCanvasGroup.alpha = Mathf.MoveTowards(_rootCanvasGroup.alpha, 1f, Time.deltaTime * 5f);
            }
            else
            {
                float fadeElapsed = _timeSinceLastActivity - delay;
                float t = duration > 0.001f ? Mathf.Clamp01(fadeElapsed / duration) : 1f;
                _rootCanvasGroup.alpha = Mathf.Lerp(1f, targetOpacity, t);
            }
        }

        public void OnConfigChanged(string key, object value)
        {
            ResetInactivityTimer();
            UpdateToggles();
            UpdateParentPosition();
            PopulateCarousel();
        }

        public void UpdateScales()
        {
            PopulateCarousel();
        }

        public void UpdateToggles()
        {
            bool isEnabled = HudEditorManager.IsEditorActive || PluginConfig.Instance.EnableCarouselHUD.Value;

            if (!isEnabled)
            {
                if (aboveInstance) aboveInstance!.SetActive(false);
                if (centerInstance) centerInstance!.SetActive(false);
                if (belowInstance) belowInstance!.SetActive(false);
                foreach (var slot in _slots) slot.SetActive(false);

                return;
            }

            if (aboveInstance)
            {
                aboveInstance!.SetActive(true);
                ToggleSlotElements(aboveInstance!, false);
            }
            if (centerInstance)
            {
                centerInstance!.SetActive(true);
                ToggleSlotElements(centerInstance!, true);
            }
            if (belowInstance)
            {
                belowInstance!.SetActive(true);
                ToggleSlotElements(belowInstance!, false);
            }

            if (isEnabled && (!centerInstance || !centerInstance!.activeSelf))
            {
                PopulateCarousel();
            }

            UpdateParentPosition();
        }

        public void UpdateParentPosition()
        {
            var rect = GetComponent<RectTransform>();
            if (rect)
            {
                var draggable = GetComponent<HudDraggable>();
                if (draggable == null || !draggable.IsDragging)
                {
                    rect.anchoredPosition = new Vector2(PluginConfig.Instance.CenterSlotX.Value, PluginConfig.Instance.CenterSlotY.Value);
                }
            }
        }

        // ========================================================================================
        // CAROUSEL CONFIGURATION
        // ========================================================================================
        private GameObject? aboveInstance;
        private GameObject? centerInstance;
        private GameObject? belowInstance;

        private List<GameObject> _slots = new();
        private Dictionary<GameObject, GameObject?> _slotToPassenger = new();
        private Dictionary<GameObject, int> _slotToIndex = new();

        // ========================================================================================
        // CONTROLLER ACCESS
        // ========================================================================================
        private DrifterBagController? _cachedBagController = null;

        private DrifterBagController? GetOrRefreshBagController()
        {

            if (_cachedBagController != null && _cachedBagController.hasAuthority)
            {
                return _cachedBagController;
            }

            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bc in bagControllers)
            {
                if (bc.hasAuthority)
                {
                    _cachedBagController = bc;
                    return _cachedBagController;
                }
            }

            _cachedBagController = null;
            return null;
        }

        private int GetAnimationCapacity(DrifterBagController? bagController)
        {
            if (bagController == null) return 1;
            return BagCapacityCalculator.GetUtilityMaxStock(bagController);
        }

        private static GameObject? _emptySlotMarker;
        private static GameObject EmptySlotMarker => _emptySlotMarker ??= new GameObject("EmptySlotMarker");

        // ========================================================================================
        // INITIALIZATION
        // ========================================================================================
        private void Start()
        {

            Transform a = transform.Find("aboveSlot");
            Transform c = transform.Find("centerSlot");
            Transform b = transform.Find("belowSlot");

            if (a) _slots.Add(a.gameObject);
            if (c) _slots.Add(c.gameObject);
            if (b) _slots.Add(b.gameObject);

            GameObject? template = (c != null) ? c.gameObject : slotPrefab;
            if (template)
            {
                for (int i = 0; i < 6; i++)
                {
                    GameObject extra = Instantiate(template!, transform);
                    extra!.name = $"extraSlot_{i}";
                    _slots.Add(extra);
                }
            }

            foreach (var s in _slots)
            {
                s.SetActive(false);
                if (!s.GetComponent<CanvasGroup>()) s.AddComponent<CanvasGroup>();
                ApplyWeightIconTransform(s);
                CleanIncompatibleSlotComponents(s);
            }

            PopulateCarousel();
            UpdateToggles();
        }

        private static void CleanIncompatibleSlotComponents(GameObject slot)
        {
            foreach (var mb in slot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == "AllyCardData")
                {
                    DestroyImmediate(mb);
                }
            }

            var extraSlot = slot.transform.Find("WhatchaGotThere EquipmentSlot");
            if (extraSlot != null)
            {
                DestroyImmediate(extraSlot.gameObject);
            }
        }

        // ========================================================================================
        // POPULATION LOGIC
        // ========================================================================================
        public void PopulateCarousel(int direction = 0)
        {
            ResetInactivityTimer();
            DrifterBagController? bagController = GetOrRefreshBagController();

            if (bagController == null)
            {
                if (!HudEditorManager.IsEditorActive)
                {
                    foreach (var s in _slots) s.SetActive(false);
                    _slotToPassenger.Clear();
                    _slotToIndex.Clear();
                    return;
                }
            }

            List<GameObject> passengerList = new List<GameObject>();
            GameObject? mainPassenger = null;

            if (bagController != null)
            {
                var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                var localList = BagPatches.GetState(bagController).BaggedObjects;

                if (bagController.hasAuthority && localList != null)
                {
                    localList.RemoveAll(obj => obj == null || !obj);
                    passengerList = localList;
                    mainPassenger = BagPatches.GetMainSeatObject(bagController);
                }
                else if (netController != null && (!NetworkServer.active || BagPatches.GetState(bagController).BaggedObjects == null))
                {

                    passengerList = netController.GetBaggedObjects();
                    int selectedIdx = netController.selectedIndex;
                    if (selectedIdx >= 0 && selectedIdx < passengerList.Count)
                    {
                        mainPassenger = passengerList[selectedIdx];
                    }
                }
                else
                {
                    var fallbackList = BagPatches.GetState(bagController).BaggedObjects;
                    if (fallbackList != null)
                    {

                        fallbackList.RemoveAll(obj => obj == null || !obj);
                        passengerList = fallbackList;
                        mainPassenger = BagPatches.GetMainSeatObject(bagController);
                    }
                }
            }

            if (passengerList.Count == 0 && mainPassenger == null)
            {
                if (!HudEditorManager.IsEditorActive)
                {
                    foreach (var s in _slots) s.SetActive(false);
                    _slotToPassenger.Clear();
                    _slotToIndex.Clear();
                    return;
                }

            }

            if (mainPassenger != null && !passengerList.Contains(mainPassenger))
            {
                mainPassenger = null;
            }

            int currentIndex = -1;
            for (int i = 0; i < passengerList.Count; i++)
            {
                if (passengerList[i] == mainPassenger)
                {
                    currentIndex = i;
                    break;
                }
            }
            Dictionary<int, GameObject?> targetPassengers = new();

            int capacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
            bool isBagFull = passengerList.Count >= capacity;

            int animationCapacity = GetAnimationCapacity(bagController);

            if (mainPassenger == null)
            {

                targetPassengers[0] = EmptySlotMarker;

                targetPassengers[1] = (passengerList.Count > 0) ? passengerList[0] : null;

                targetPassengers[-1] = (passengerList.Count > 0) ? passengerList[passengerList.Count - 1] : null;

                targetPassengers[2] = (passengerList.Count > 1) ? passengerList[1] : null;

                targetPassengers[-2] = (passengerList.Count > 1) ? passengerList[passengerList.Count - 2] : null;
            }
            else
            {

                targetPassengers[0] = mainPassenger;

                int aboveIndex = currentIndex + 1;
                if (aboveIndex < passengerList.Count)
                {
                    targetPassengers[1] = passengerList[aboveIndex];
                }
                else if (isBagFull && passengerList.Count > 0)
                {

                    targetPassengers[1] = passengerList[0];
                }
                else
                {
                    targetPassengers[1] = EmptySlotMarker;
                }

                int belowIndex = currentIndex - 1;
                if (belowIndex >= 0)
                {
                    targetPassengers[-1] = passengerList[belowIndex];
                }
                else if (isBagFull && passengerList.Count > 0)
                {

                    targetPassengers[-1] = passengerList[passengerList.Count - 1];
                }
                else
                {
                    targetPassengers[-1] = EmptySlotMarker;
                }

                int hiddenAbove = currentIndex + 2;
                if (hiddenAbove < passengerList.Count) targetPassengers[2] = passengerList[hiddenAbove];
                else if (hiddenAbove == passengerList.Count) targetPassengers[2] = EmptySlotMarker;
                else if (passengerList.Count > 0) targetPassengers[2] = passengerList[0];
                else targetPassengers[2] = EmptySlotMarker;

                int hiddenBelow = currentIndex - 2;
                if (hiddenBelow >= 0) targetPassengers[-2] = passengerList[hiddenBelow];
                else if (hiddenBelow == -1) targetPassengers[-2] = EmptySlotMarker;
                else if (passengerList.Count > 0) targetPassengers[-2] = passengerList[passengerList.Count - 1];
                else targetPassengers[-2] = EmptySlotMarker;
            }

            Dictionary<GameObject?, int> passengerToIndex = new();
            for (int pi = 0; pi < passengerList.Count; pi++)
            {
                passengerToIndex[passengerList[pi]] = pi + 1;
            }

            float sideScaleVal = PluginConfig.Instance.SideSlotScale.Value;
            float sideOpacityVal = PluginConfig.Instance.SideSlotOpacity.Value;

            HashSet<GameObject> usedSlots = new();
            HashSet<GameObject?> foundPassengers = new();

            var slotsToProcess = _slots.ToList();
            foreach (var slot in slotsToProcess)
            {
                if (!_slotToPassenger.TryGetValue(slot, out var passenger)) continue;

                int newState = -99;
                foreach (var kvp in targetPassengers)
                {
                    if (kvp.Value == passenger) { newState = kvp.Key; break; }
                }

                if (newState != -99 && !foundPassengers.Contains(passenger))
                {

                    int slotIndex = (passenger != null && passenger != EmptySlotMarker && passengerToIndex.TryGetValue(passenger, out int idx)) ? idx : -1;

                    var cg = slot.GetComponent<CanvasGroup>();
                    float savedAlpha = cg != null ? cg.alpha : 1f;

                    SetSlotData(slot, passenger, bagController, newState == 0, slotIndex, passengerList.Count);

                    if (cg != null) cg.alpha = savedAlpha;

                    AnimateToState(slot, newState, animationCapacity, bagController);
                    usedSlots.Add(slot);
                    foundPassengers.Add(passenger);
                }
                else
                {

                    int exitState = (direction > 0) ? -2 : 2;
                    if (direction == 0) exitState = -2;

                    AnimateToState(slot, exitState, animationCapacity, bagController, true);
                }
            }

            foreach (var kvp in targetPassengers)
            {
                int state = kvp.Key;
                GameObject? targetP = kvp.Value;

                if (targetP != null && targetP != EmptySlotMarker && foundPassengers.Contains(targetP)) continue;
                if (targetP == EmptySlotMarker && foundPassengers.Contains(EmptySlotMarker)) continue;

                GameObject? freeSlot = null;
                foreach (var slot in _slots)
                {
                    if (!usedSlots.Contains(slot) && !_slotToPassenger.ContainsKey(slot))
                    {
                        freeSlot = slot;
                        break;
                    }
                }
                if (freeSlot == null)
                {

                    foreach (var slot in _slots)
                    {
                        if (!usedSlots.Contains(slot))
                        {
                            freeSlot = slot;
                            break;
                        }
                    }
                }

                if (freeSlot)
                {
                    _slotToPassenger[freeSlot!] = targetP;
                    int slotIndex = (targetP != null && targetP != EmptySlotMarker && passengerToIndex.TryGetValue(targetP!, out int idx)) ? idx : -1;
                    _slotToIndex[freeSlot!] = slotIndex;
                    SetSlotData(freeSlot!, targetP, bagController, state == 0, slotIndex, passengerList.Count);

                    int startState = (direction > 0) ? state + 1 : state - 1;
                    if (direction == 0) startState = state;

                    var startParams = GetStateParams(startState, animationCapacity);
                    SetSlotInitialState(freeSlot!, startParams.pos.x, startParams.pos.y, startParams.scale, 0f);
                    freeSlot!.SetActive(true);

                    AnimateToState(freeSlot!, state, animationCapacity, bagController);
                    usedSlots.Add(freeSlot);
                    foundPassengers.Add(targetP);
                }
            }

            centerInstance = null;
            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var p) && p == targetPassengers[0])
                {
                    centerInstance = slot;
                    break;
                }
            }
            aboveInstance = null;
            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var p) && p == targetPassengers[1])
                {
                    aboveInstance = slot;
                    break;
                }
            }
            belowInstance = null;
            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var p) && p == targetPassengers[-1])
                {
                    belowInstance = slot;
                    break;
                }
            }

            if (centerInstance) centerInstance!.transform.SetAsLastSibling();

            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var slotPassenger) && slot.activeSelf)
                {
                    bool isCenter = slot == centerInstance;
                    ToggleSlotElements(slot, isCenter);

                    var baggedCard = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
                    if (baggedCard && baggedCard.healthBar)
                    {
                        bool showPreview = PluginConfig.Instance.EnableDamagePreview.Value &&
                            (isCenter || PluginConfig.Instance.AoEDamageDistribution.Value != DrifterBossGrabMod.AoEDamageMode.None);
                        var overlay = baggedCard.healthBar.GetComponent<DamagePreviewOverlay>();
                        if (showPreview)
                        {
                            if (!overlay)
                                overlay = baggedCard.healthBar.gameObject.AddComponent<DamagePreviewOverlay>();
                            if (slotPassenger != null && slotPassenger != EmptySlotMarker && bagController != null)
                                overlay.SetTarget(slotPassenger, bagController);
                        }
                        else if (overlay)
                        {
                            Destroy(overlay);
                        }
                    }
                }
            }

            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var passenger) && passenger != null && passenger != EmptySlotMarker)
                {
                    int idx = passengerToIndex.TryGetValue(passenger!, out int i) ? i : -1;
                    _slotToIndex[slot] = idx;
                    SetSlotNumberLabel(slot, idx, passengerList.Count);
                }
                else if (_slotToPassenger.ContainsKey(slot))
                {
                    _slotToIndex[slot] = -1;
                    SetSlotNumberLabel(slot, -1, passengerList.Count);
                }
            }
        }

        // ========================================================================================
        // ANIMATION HELPERS
        // ========================================================================================
        private void AnimateToState(GameObject slot, int state, int capacity, DrifterBagController? bagController, bool hideAfter = false)
        {
            var p = GetStateParams(state, capacity);

            float targetOpacity = p.opacity;
            if (_slotToPassenger.TryGetValue(slot, out var passenger) && (passenger == null || passenger == EmptySlotMarker))
            {
                targetOpacity = 0f;
            }

            bool useFading = capacity > 1;
            AnimateSlot(slot, p.pos.x, p.pos.y, p.scale, targetOpacity, hideAfter, useFading);
        }

        private (Vector2 pos, float scale, float opacity) GetStateParams(int state, int capacity)
        {
            float sideX = PluginConfig.Instance.SideSlotX.Value;
            float sideY = PluginConfig.Instance.SideSlotY.Value;
            float spacing = PluginConfig.Instance.CarouselSpacing.Value;

            float scale = (state == 0) ? PluginConfig.Instance.CenterSlotScale.Value : PluginConfig.Instance.SideSlotScale.Value;
            float opacity = (state == 0) ? PluginConfig.Instance.CenterSlotOpacity.Value : PluginConfig.Instance.SideSlotOpacity.Value;

            if (Mathf.Abs(state) > 1)
            {
                opacity = 0f;
                scale *= 0.8f;
            }

            bool isHorizontal = PluginConfig.Instance.CarouselOrientation.Value == CarouselOrientation.Horizontal;

            Vector2 pos;
            if (isHorizontal)
            {
                switch (state)
                {
                    case 0: pos = Vector2.zero; break;
                    case 1: pos = new Vector2(sideX + spacing, sideY); break;
                    case -1: pos = new Vector2(sideX - spacing, sideY); break;
                    case 2: pos = new Vector2(sideX + 2 * spacing, sideY); break;
                    case -2: pos = new Vector2(sideX - 2 * spacing, sideY); break;
                    case 3: pos = new Vector2(sideX + 3 * spacing, sideY); break;
                    case -3: pos = new Vector2(sideX - 3 * spacing, sideY); break;
                    default: pos = Vector2.zero; break;
                }
            }
            else
            {
                switch (state)
                {
                    case 0: pos = Vector2.zero; break;
                    case 1: pos = new Vector2(sideX, sideY - spacing); break;
                    case -1: pos = new Vector2(sideX, sideY + spacing); break;
                    case 2: pos = new Vector2(sideX, sideY - 2 * spacing); break;
                    case -2: pos = new Vector2(sideX, sideY + 2 * spacing); break;
                    case 3: pos = new Vector2(sideX, sideY - 3 * spacing); break;
                    case -3: pos = new Vector2(sideX, sideY + 3 * spacing); break;
                    default: pos = Vector2.zero; break;
                }
            }
            return (pos, scale, opacity);
        }

        private Dictionary<GameObject, Coroutine> _activeCoroutines = new();

        private void AnimateSlot(GameObject slot, float x, float y, float scale, float opacity)
        {
            AnimateSlot(slot, x, y, scale, opacity, false, true);
        }

        private void AnimateSlot(GameObject slot, float x, float y, float scale, float opacity, bool hideAfter, bool useFading)
        {
            if (_activeCoroutines.TryGetValue(slot, out var existing) && existing != null)
            {
                StopCoroutine(existing);

            }

            if (PluginConfig.Instance.CarouselAnimationDuration.Value <= 0.001f || !useFading)
            {
                ApplySlotStateImmediate(slot, x, y, scale, opacity);
                if (hideAfter)
                {
                    slot.SetActive(false);
                    _slotToPassenger.Remove(slot);
                }
                if (_activeCoroutines.ContainsKey(slot)) _activeCoroutines.Remove(slot);
                return;
            }

            _activeCoroutines[slot] = StartCoroutine(AnimateSlotPosition(slot, x, y, scale, opacity, hideAfter, useFading));
        }

        private void ApplySlotStateImmediate(GameObject slot, float x, float y, float scale, float opacity)
        {
            var rectTransform = slot.GetComponent<RectTransform>();
            if (rectTransform) rectTransform.anchoredPosition = new Vector2(x, y);
            slot.transform.localScale = Vector3.one * scale;
            var canvasGroup = slot.GetComponent<CanvasGroup>() ?? slot.AddComponent<CanvasGroup>();
            canvasGroup.alpha = opacity;
        }

        private void SetSlotInitialState(GameObject slot, float x, float y, float scale, float opacity)
        {
            var rectTransform = slot.GetComponent<RectTransform>();
            if (rectTransform) rectTransform.anchoredPosition = new Vector2(x, y);
            slot.transform.localScale = Vector3.one * scale;
            var group = slot.GetComponent<CanvasGroup>() ?? slot.AddComponent<CanvasGroup>();
            group.alpha = opacity;
        }

        private System.Collections.IEnumerator AnimateSlotPosition(GameObject slot, float targetXOffset, float targetYOffset, float targetScale, float targetOpacity, bool hideAfter, bool useFading)
        {
            var rectTransform = slot.GetComponent<RectTransform>();
            var canvasGroup = slot.GetComponent<CanvasGroup>() ?? slot.AddComponent<CanvasGroup>();

            float duration = PluginConfig.Instance.CarouselAnimationDuration.Value;
            float elapsed = 0f;

            Vector2 startPosition = rectTransform ? rectTransform.anchoredPosition : Vector2.zero;
            Vector2 targetPosition = new Vector2(targetXOffset, targetYOffset);
            float startScale = slot.transform.localScale.x;
            float startOpacity = canvasGroup.alpha;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float easeT = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

                if (rectTransform) rectTransform.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, easeT);
                slot.transform.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, easeT);
                if (useFading)
                {
                    canvasGroup.alpha = Mathf.Lerp(startOpacity, targetOpacity, easeT);
                }

                yield return null;
            }

            if (rectTransform) rectTransform.anchoredPosition = targetPosition;
            slot.transform.localScale = Vector3.one * targetScale;
            canvasGroup.alpha = targetOpacity;

            if (hideAfter)
            {
                slot.SetActive(false);
                _slotToPassenger.Remove(slot);
            }

            _activeCoroutines.Remove(slot);
        }

        // ========================================================================================
        // DATA BINDING
        // ========================================================================================
        private void SetSlotData(GameObject slot, GameObject? passenger, DrifterBagController? bagController, bool isCenter, int slotIndex = -1, int totalCount = 0)
        {
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
                var canvasGroup = slot.GetComponent<CanvasGroup>();

                if (passenger == EmptySlotMarker || passenger == null)
                {

                    baggedCardController.sourceBody = null;
                    baggedCardController.sourceMaster = null;
                    baggedCardController.sourcePassengerAttributes = null;
                    baggedCardController.ForceUpdate();

                    if (baggedCardController.nameLabel) baggedCardController.nameLabel.gameObject.SetActive(false);
                    if (baggedCardController.portraitIconImage) baggedCardController.portraitIconImage.gameObject.SetActive(false);
                    if (baggedCardController.healthBar)
                    {
                        baggedCardController.healthBar.gameObject.SetActive(false);
                        if (baggedCardController.healthBar.deadImage) baggedCardController.healthBar.deadImage.enabled = false;
                    }

                    if (canvasGroup) canvasGroup.alpha = HudEditorManager.IsEditorActive ? 0.3f : 0f;

                    var childLocator = slot.GetComponent<ChildLocator>();
                    if (childLocator)
                    {
                        var weightIconTransform = childLocator.FindChild("WeightIcon");
                        if (weightIconTransform)
                        {
                            weightIconTransform.gameObject.SetActive(false);
                            var tmp = weightIconTransform.Find("WeightText")?.GetComponent<TextMeshProUGUI>();
                            if (tmp) tmp!.gameObject.SetActive(false);

                            var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();
                            if (unitLabel) unitLabel!.gameObject.SetActive(false);
                        }
                    }
                }
                else
                {
                    if (passenger == null || !passenger)
                    {
                        if (baggedCardController.healthBar)
                        {
                            baggedCardController.healthBar.gameObject.SetActive(false);
                        }
                        return;
                    }

                    var specialObjectAttributes = passenger!.GetComponent<SpecialObjectAttributes>();
                    var body = passenger.GetComponent<CharacterBody>();
                    var master = passenger.GetComponent<CharacterMaster>();
                    var healthComponent = passenger.GetComponent<HealthComponent>();

                    baggedCardController.sourceBody = body;
                    baggedCardController.sourceMaster = master;
                    baggedCardController.sourcePassengerAttributes = specialObjectAttributes;
                    baggedCardController.ForceUpdate();

                    if (healthComponent != null && body != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[BaggedObjectCarousel] Health info for {passenger.name}: health={healthComponent.health}, fullHealth={healthComponent.fullHealth}, fullCombinedHealth={healthComponent.fullCombinedHealth}, baseMaxHealth={body.baseMaxHealth}");
                        }

                        if (baggedCardController.healthBar != null && baggedCardController.healthBar.source == healthComponent)
                        {
                            try
                            {
                                baggedCardController.healthBar.Update();
                            }
                            catch (NullReferenceException)
                            {
                                Log.Warning($"[BaggedObjectCarousel] Failed to update health bar for passenger {passenger?.name} (health bar in invalid state)");
                            }
                        }
                    }
                    else if (specialObjectAttributes != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[BaggedObjectCarousel] SpecialObjectAttributes for {passenger!.name}: durability={specialObjectAttributes.durability}, maxDurability={specialObjectAttributes.maxDurability}");
                        }

                        if (baggedCardController.healthBar != null && baggedCardController.healthBar.altSource == specialObjectAttributes)
                        {
                            try
                            {
                                baggedCardController.healthBar.Update();
                            }
                            catch (NullReferenceException)
                            {
                                Log.Warning($"[BaggedObjectCarousel] Failed to update health bar for passenger {passenger?.name} (health bar in invalid state)");
                            }
                        }
                    }

                    if (baggedCardController.healthBar && baggedCardController.healthBar!.deadImage)
                    {
                        baggedCardController.healthBar!.deadImage!.enabled = false;
                    }

                    float mass = 0f;
                    if (bagController != null)
                    {
                        mass = (bagController == passenger) ? bagController.baggedMass : bagController.CalculateBaggedObjectMass(passenger!);
                    }
                    float baseMass = mass;

                    bool showTotal = PluginConfig.Instance.ShowTotalMassOnWeightIcon.Value;
                    bool isAllMode = PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.All;

                    if (isCenter && showTotal && isAllMode)
                    {

                        mass = bagController ? BagCapacityCalculator.GetBaggedObjectMass(bagController!) : 0f;
                    }

                    var childLocator = slot.GetComponent<ChildLocator>();
                    if (childLocator)
                    {
                        var weightIconTransform = childLocator.FindChild("WeightIcon");
                        if (weightIconTransform)
                        {
                            var image = weightIconTransform.GetComponent<UnityEngine.UI.Image>();
                            if (image)
                            {

                                if (PluginConfig.Instance.UseNewWeightIcon.Value)
                                {
                                    if (NewWeightIconSprite)
                                    {
                                        image.sprite = NewWeightIconSprite;
                                    }
                                }
                                else
                                {
                                    if (OldWeightIconSprite)
                                    {
                                        image.sprite = OldWeightIconSprite;
                                    }
                                }

                                ApplyWeightIconTransform(slot);

                                float percentage = 0f;
                                bool isOverencumbered = false;

                                if (PluginConfig.Instance.EnableBalance.Value)
                                {
                                    float maxCapacity = bagController ? CapacityScalingSystem.CalculateMassCapacity(bagController) : DrifterBagController.maxMass;
                                    if (maxCapacity >= 1000000f)
                                    {
                                        float maxMass = 700f;
                                        if (!PluginConfig.Instance.IsMassCapInfinite && float.TryParse(PluginConfig.Instance.MassCap.Value, out float parsedMassCap))
                                        {
                                            maxMass = parsedMassCap;
                                        }
                                        percentage = maxMass > 0 ? (mass / maxMass) : 0f;
                                        isOverencumbered = false;
                                    }
                                    else
                                    {
                                        percentage = (maxCapacity > 0) ? (mass / maxCapacity) : 0f;
                                        isOverencumbered = (isCenter && showTotal) && percentage > 1.0f;
                                    }
                                }
                                else
                                {
                                    bool isShowingTotal = isCenter && showTotal && isAllMode;
                                    if (isShowingTotal)
                                    {
                                        int totalCapacitySlots = bagController ? CapacityScalingSystem.GetTotalCapacity(bagController) : 0;
                                        int currentSlots = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                                        percentage = totalCapacitySlots > 0 ? ((float)currentSlots / totalCapacitySlots) : 0f;
                                        isOverencumbered = currentSlots > totalCapacitySlots;
                                    }
                                    else
                                    {
                                        float maxMass = 700f;
                                        if (!PluginConfig.Instance.IsMassCapInfinite && float.TryParse(PluginConfig.Instance.MassCap.Value, out float parsedMassCap))
                                        {
                                            maxMass = parsedMassCap;
                                        }
                                        percentage = maxMass > 0 ? (mass / maxMass) : 0f;
                                        isOverencumbered = false;
                                    }
                                }

                                if (PluginConfig.Instance.ScaleWeightColor.Value)
                                {

                                    if (isOverencumbered)
                                    {
                                        float overencumbranceFraction = 0f;

                                        if (PluginConfig.Instance.EnableBalance.Value)
                                        {
                                            float maxOverPercent = PluginConfig.Instance.OverencumbranceMax.Value / 100.0f;
                                            if (maxOverPercent <= 0f) maxOverPercent = 0.01f;
                                            overencumbranceFraction = Mathf.Clamp01((percentage - 1.0f) / maxOverPercent);
                                        }
                                        else
                                        {
                                            int totalCapacitySlots = bagController ? CapacityScalingSystem.GetTotalCapacity(bagController) : 0;
                                            int currentSlots = BagCapacityCalculator.GetCurrentBaggedCount(bagController);
                                            float extraSlots = Mathf.Max(0, currentSlots - totalCapacitySlots);
                                            overencumbranceFraction = Mathf.Clamp01(extraSlots / Mathf.Max(1f, totalCapacitySlots));
                                        }

                                        image.color = GetGradientColor(overencumbranceFraction,
                                            PluginConfig.Instance.OverencumbranceGradientColorStart.Value,
                                            PluginConfig.Instance.OverencumbranceGradientColorMid.Value,
                                            PluginConfig.Instance.OverencumbranceGradientColorEnd.Value);
                                    }
                                    else
                                    {

                                        image.color = GetGradientColor(Mathf.Clamp01(percentage),
                                            PluginConfig.Instance.CapacityGradientColorStart.Value,
                                            PluginConfig.Instance.CapacityGradientColorMid.Value,
                                            PluginConfig.Instance.CapacityGradientColorEnd.Value);
                                    }
                                }

                                var weightDisplayMode = PluginConfig.Instance.WeightDisplayMode.Value;
                                bool showOverencumbranceIcon = isOverencumbered && PluginConfig.Instance.UseNewWeightIcon.Value && PluginConfig.Instance.ShowOverencumberIcon.Value && PluginConfig.Instance.ShowTotalMassOnWeightIcon.Value;

                                var overIconObj = weightIconTransform.Find("OverencumbranceIcon");

                                if (showOverencumbranceIcon)
                                {
                                    if (!overIconObj)
                                    {
                                        var newObj = new GameObject("OverencumbranceIcon", typeof(RectTransform), typeof(UnityEngine.CanvasRenderer), typeof(UnityEngine.UI.Image));
                                        newObj.transform.SetParent(weightIconTransform, false);
                                        overIconObj = newObj.transform;

                                        var img = newObj.GetComponent<UnityEngine.UI.Image>();
                                        img.sprite = OverencumbranceIconSprite;
                                        img.color = new Color(0.8f, 0.9f, 1f, 1f);
                                    }

                                    var overImg = overIconObj.GetComponent<UnityEngine.UI.Image>();
                                    if (overImg && OverencumbranceIconSprite)
                                    {
                                        overImg.sprite = OverencumbranceIconSprite;
                                        overImg.color = new Color(0.8f, 0.9f, 1f, 1f);
                                    }

                                    var rect = overIconObj.GetComponent<RectTransform>();
                                    if (rect)
                                    {

                                        rect.anchorMin = new Vector2(0.5f, 0.5f);
                                        rect.anchorMax = new Vector2(0.5f, 0.5f);
                                        rect.pivot = new Vector2(0.5f, 0.5f);
                                        rect.anchoredPosition = new Vector2(0f, -4f);
                                        rect.sizeDelta = new Vector2(7.5f, 7.5f);
                                    }
                                    overIconObj.gameObject.SetActive(true);

                                    var tmpTextObj = weightIconTransform.Find("WeightText");
                                    if (tmpTextObj) tmpTextObj.gameObject.SetActive(false);

                                    var unitLabelObj = weightIconTransform.Find("WeightUnitLabel");
                                    if (unitLabelObj) unitLabelObj.gameObject.SetActive(false);
                                }
                                else
                                {
                                    if (overIconObj) overIconObj.gameObject.SetActive(false);

                                    if (weightDisplayMode != DrifterBossGrabMod.WeightDisplayMode.None)
                                    {
                                        var tmp = weightIconTransform.Find("WeightText")?.GetComponent<TextMeshProUGUI>();
                                        var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();

                                        if (!tmp)
                                        {
                                            var textObj = new GameObject("WeightText");
                                            textObj.transform.SetParent(weightIconTransform, false);
                                            tmp = textObj.AddComponent<TextMeshProUGUI>();
                                            tmp.font = RoR2.UI.HGTextMeshProUGUI.defaultLanguageFont;
                                            tmp.fontSize = 12;
                                            tmp.alignment = TextAlignmentOptions.Center;
                                            tmp.color = Color.white;
                                            tmp.richText = true;
                                        }
                                        var tmpRectTransform = tmp!.GetComponent<RectTransform>();
                                        if (tmpRectTransform)
                                        {
                                            tmpRectTransform.sizeDelta = new Vector2(50, 20);
                                            tmpRectTransform.localRotation = Quaternion.identity;
                                            if (PluginConfig.Instance.UseNewWeightIcon.Value)
                                            {
                                                tmpRectTransform.anchoredPosition = new Vector2(0f, 2.4f);
                                                tmp.verticalAlignment = VerticalAlignmentOptions.Bottom;
                                                tmp.fontSize = 8.5f;
                                                tmp.characterSpacing = 0f;
                                                tmpRectTransform.localRotation = Quaternion.identity;
                                            }
                                            else
                                            {
                                                tmpRectTransform.anchoredPosition = new Vector2(0f, 0f);
                                                tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
                                                tmp.fontSize = 12f;
                                                tmp.characterSpacing = 0f;
                                                tmpRectTransform.localRotation = Quaternion.Euler(0, 0, 90);
                                            }
                                        }

                                        switch (weightDisplayMode)
                                        {
                                            case DrifterBossGrabMod.WeightDisplayMode.Multiplier:
                                                int multiplier = Mathf.CeilToInt(mass / 100f);
                                                tmp.text = multiplier + "x";
                                                if (unitLabel) unitLabel!.gameObject.SetActive(false);
                                                break;

                                            case DrifterBossGrabMod.WeightDisplayMode.Pounds:
                                                int pounds = Mathf.FloorToInt(mass / 10f);
                                                tmp.text = $"<alpha=#00><size=40%><voffset=-0.2em>lb</voffset></size><space=-0.2em><alpha=#FF>{pounds}<space=-0.2em><size=40%><voffset=-0.2em>lb</voffset></size>";
                                                if (unitLabel) unitLabel!.gameObject.SetActive(false);
                                                break;

                                            case DrifterBossGrabMod.WeightDisplayMode.KiloGrams:
                                                int kiloGrams = Mathf.FloorToInt(mass / 10f);
                                                tmp.text = $"<alpha=#00><size=40%><voffset=-0.2em>kg</voffset></size><space=-0.2em><alpha=#FF>{kiloGrams}<space=-0.2em><size=40%><voffset=-0.2em>kg</voffset></size>";
                                                if (unitLabel) unitLabel!.gameObject.SetActive(false);
                                                break;
                                        }

                                        tmp.gameObject.SetActive(true);
                                    }
                                    else
                                    {
                                        var tmp = weightIconTransform.Find("WeightText")?.GetComponent<TextMeshProUGUI>();
                                        if (tmp) tmp!.gameObject.SetActive(false);

                                        var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();
                                        if (unitLabel) unitLabel!.gameObject.SetActive(false);
                                    }
                                }
                            }
                        }
                    }
                }

                bool showPreview = PluginConfig.Instance.EnableDamagePreview.Value &&
                    (isCenter || PluginConfig.Instance.AoEDamageDistribution.Value != DrifterBossGrabMod.AoEDamageMode.None);
                if (baggedCardController.healthBar)
                {
                    var overlay = baggedCardController.healthBar!.GetComponent<DamagePreviewOverlay>();
                    if (showPreview)
                    {
                        if (!overlay)
                            overlay = baggedCardController.healthBar.gameObject.AddComponent<DamagePreviewOverlay>();
                        if (passenger != null) overlay.SetTarget(passenger, bagController);
                    }
                    else if (overlay)
                    {

                        Destroy(overlay);
                    }
                }

                ToggleSlotElements(slot, isCenter);

                SetSlotNumberLabel(slot, slotIndex, totalCount);
            }
        }

        private void ToggleSlotElements(GameObject slot, bool isCenter)
        {
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {

                bool showIcon = isCenter ? PluginConfig.Instance.CenterSlotShowIcon.Value : PluginConfig.Instance.SideSlotShowIcon.Value;
                bool showBackground = isCenter ? PluginConfig.Instance.CenterSlotShowBackground.Value : PluginConfig.Instance.SideSlotShowBackground.Value;
                bool showWeight = isCenter ? PluginConfig.Instance.CenterSlotShowWeightIcon.Value : PluginConfig.Instance.SideSlotShowWeightIcon.Value;
                bool showName = isCenter ? PluginConfig.Instance.CenterSlotShowName.Value : PluginConfig.Instance.SideSlotShowName.Value;
                bool showHealthBar = isCenter ? PluginConfig.Instance.CenterSlotShowHealthBar.Value : PluginConfig.Instance.SideSlotShowHealthBar.Value;
                bool showSlotNumber = isCenter ? PluginConfig.Instance.CenterSlotShowSlotNumber.Value : PluginConfig.Instance.SideSlotShowSlotNumber.Value;

                var cardImage = baggedCardController.GetComponent<UnityEngine.UI.Image>();
                if (cardImage)
                {
                    cardImage.enabled = showBackground;
                }

                if (baggedCardController.portraitIconImage)
                {

                    baggedCardController.portraitIconImage.gameObject.SetActive(true);
                }

                var layoutElement = baggedCardController.portraitIconImage?.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layoutElement)
                {
                    layoutElement!.gameObject.SetActive(showIcon);
                }

                var childLocator = slot.GetComponent<ChildLocator>();
                if (childLocator)
                {
                    var weightIconTransform = childLocator.FindChild("WeightIcon");
                    if (weightIconTransform)
                    {
                        weightIconTransform.gameObject.SetActive(showWeight);

                        var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();
                        if (unitLabel)
                        {
                            unitLabel!.gameObject.SetActive(false);
                        }
                    }
                }

                if (baggedCardController.nameLabel)
                {
                    baggedCardController.nameLabel.gameObject.SetActive(showName);
                }

                if (baggedCardController.healthBar)
                {
                    baggedCardController.healthBar.gameObject.SetActive(showHealthBar);
                }

                if (baggedCardController.portraitIconImage)
                {
                    var slotNumberBadge = baggedCardController.portraitIconImage!.transform.Find("SlotNumberBadge");
                    if (slotNumberBadge)
                    {
                        slotNumberBadge.gameObject.SetActive(showSlotNumber);
                    }
                }
            }
        }

        private void SetSlotNumberLabel(GameObject slot, int slotIndex, int totalCount)
        {

            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (!baggedCardController || !baggedCardController.portraitIconImage) return;

            Transform? portraitTransform = baggedCardController.portraitIconImage.transform;
            var badgeTransform = portraitTransform.Find("SlotNumberBadge");
            TextMeshProUGUI? slotNumberTmp = null;

            if (badgeTransform)
            {
                slotNumberTmp = badgeTransform.GetComponentInChildren<TextMeshProUGUI>();
            }
            else
            {

                var badgeObj = new GameObject("SlotNumberBadge");
                badgeObj.transform.SetParent(portraitTransform, false);

                var badgeRect = badgeObj.AddComponent<RectTransform>();

                badgeRect.anchorMin = new Vector2(1f, 1f);
                badgeRect.anchorMax = new Vector2(1f, 1f);
                badgeRect.pivot = new Vector2(1f, 1f);
                badgeRect.sizeDelta = new Vector2(16, 16);
                badgeRect.anchoredPosition = new Vector2(-2f, -2f);

                var bgImage = badgeObj.AddComponent<UnityEngine.UI.Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0.85f);
                bgImage.raycastTarget = false;

                var outlineTex = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Texture2D>("RoR2/Base/UI/texDetailPanel.png").WaitForCompletion();
                if (outlineTex)
                {
                    bgImage.sprite = Sprite.Create(outlineTex, new UnityEngine.Rect(0, 0, outlineTex.width, outlineTex.height), new Vector2(0.5f, 0.5f));
                    bgImage.type = UnityEngine.UI.Image.Type.Sliced;
                }

                var textObj = new GameObject("Text");
                textObj.transform.SetParent(badgeObj.transform, false);
                slotNumberTmp = textObj.AddComponent<TextMeshProUGUI>();
                slotNumberTmp.font = RoR2.UI.HGTextMeshProUGUI.defaultLanguageFont;
                slotNumberTmp.fontSize = 12f;
                slotNumberTmp.fontStyle = TMPro.FontStyles.Bold;
                slotNumberTmp.alignment = TextAlignmentOptions.Center;
                slotNumberTmp.color = Color.white;
                slotNumberTmp.raycastTarget = false;
                slotNumberTmp.enableWordWrapping = false;
                slotNumberTmp.overflowMode = TextOverflowModes.Overflow;

                var textRect = slotNumberTmp.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(1f, 0f);
                textRect.offsetMax = new Vector2(-1f, 0f);
            }

            if (badgeTransform == null)
                badgeTransform = portraitTransform.Find("SlotNumberBadge");

            if (slotNumberTmp && badgeTransform)
            {
                if (slotIndex > 0)
                {
                    slotNumberTmp.text = $"{slotIndex}";
                    bool isCenter = slot == centerInstance;
                    bool showSlotNumber = isCenter ? PluginConfig.Instance.CenterSlotShowSlotNumber.Value : PluginConfig.Instance.SideSlotShowSlotNumber.Value;
                    badgeTransform.gameObject.SetActive(showSlotNumber);
                }
                else
                {
                    badgeTransform.gameObject.SetActive(false);
                }
            }
        }

        public static void ApplyWeightIconTransform(GameObject slot)
        {
            var carousel = slot.GetComponentInParent<BaggedObjectCarousel>();
            if (carousel)
            {
                carousel.StartCoroutine(ApplyWeightIconTransformDelayed(slot));
            }
            else
            {

                ApplyWeightIconTransformImmediate(slot);
            }
        }

        private static IEnumerator ApplyWeightIconTransformDelayed(GameObject slot)
        {

            yield return new WaitForEndOfFrame();

            ApplyWeightIconTransformImmediate(slot);

            yield return new WaitForSeconds(0.1f);
            ApplyWeightIconTransformImmediate(slot);
        }

        private static void ApplyWeightIconTransformImmediate(GameObject slot)
        {
            var childLocator = slot.GetComponent<ChildLocator>();
            if (childLocator)
            {
                var weightIconTransform = childLocator.FindChild("WeightIcon");
                if (weightIconTransform)
                {
                    var layoutElement = weightIconTransform.GetComponent<UnityEngine.UI.LayoutElement>();
                    if (layoutElement)
                    {
                        layoutElement.ignoreLayout = true;
                    }

                    if (PluginConfig.Instance.UseNewWeightIcon.Value)
                    {
                        weightIconTransform.localPosition = new Vector3(-23f, 1.5f, 0f);
                        weightIconTransform.localRotation = Quaternion.identity;
                    }
                    else
                    {
                        weightIconTransform.localPosition = new Vector3(-15.4757f, 0.1f, 0f);
                        weightIconTransform.localRotation = Quaternion.Euler(0f, 0f, 270f);
                    }
                }
            }
        }
        private static Color GetGradientColor(float percentage, Color start, Color mid, Color end)
        {
            percentage = Mathf.Clamp01(percentage);
            if (percentage <= 0.5f)
                return Color.Lerp(start, mid, percentage * 2f);
            else
                return Color.Lerp(mid, end, (percentage - 0.5f) * 2f);
        }
    }
}
