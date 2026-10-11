#nullable enable
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;
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
        private static readonly FieldInfo? LowHealthItem = HarmonyLib.AccessTools.Field(typeof(HealthBar), "hasLowHealthItem");
        private static readonly FieldInfo? LowHealthBuff = HarmonyLib.AccessTools.Field(typeof(HealthBar), "hasLowHealthBuff");

        internal static void ResetHealthBarWithoutSource(HealthBar? healthBar)
        {
            if (healthBar == null || healthBar.source != null) return;
            LowHealthItem?.SetValue(healthBar, false);
            LowHealthBuff?.SetValue(healthBar, false);
        }

        public GameObject? slotPrefab;

        public float sideScale = 0.8f;

        private static Texture2D? _weightIconTexture;
        private static Texture2D? WeightIconTexture => _weightIconTexture ??= LoadWeightIconTexture();
        private static Sprite? _newWeightIconSprite;
        public static Sprite? NewWeightIconSprite => _newWeightIconSprite ??= (WeightIconTexture != null ? Sprite.Create(WeightIconTexture, new Rect(0, 0, WeightIconTexture.width, WeightIconTexture.height), new Vector2(0.5f, 0.5f)) : null);
        private static Sprite? _oldWeightIconSprite;
        public static Sprite OldWeightIconSprite => _oldWeightIconSprite ??= Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/texMovespeedBuffIcon.tif").WaitForCompletion();
        private static Sprite? _overencumbranceIconSprite;
        public static Sprite? OverencumbranceIconSprite => _overencumbranceIconSprite ??= LoadOverencumbranceIconSprite();
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
        private int _lastPassengerStateHash;
        private bool _hasPassengerStateHash;
        private readonly List<GameObject> _layoutPassengers = new();
        private GameObject? _layoutMainPassenger;
        private int _layoutCapacity;
        private int _layoutCycleCount = 1;
        private int _layoutEmptySlotIndex = -1;
        private bool _hasLayout;
        private GameObject? _pendingPromotionPassenger;
        private float _promotionWaitUntil;
        private readonly List<CarouselCardExit> _departureVisuals = new();

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
            _hasLayout = false;
            _rootCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            ResetInactivityTimer();
            BagCarouselUpdater.ActiveCarousels.Add(this);
            ConfigChangeNotifier.AddObserver(this);
        }

        private void OnDisable()
        {
            BagCarouselUpdater.ActiveCarousels.Remove(this);
            ConfigChangeNotifier.RemoveObserver(this);
            foreach (var visual in _departureVisuals)
                if (visual != null) Destroy(visual.gameObject);
            _departureVisuals.Clear();
        }

        private void Update()
        {
            if (_promotionWaitUntil > 0f && Time.unscaledTime >= _promotionWaitUntil)
            {
                _promotionWaitUntil = 0f;
                PopulateCarousel();
            }
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
            _hasLayout = false;
            ResetInactivityTimer();
            UpdateToggles();
            UpdateParentPosition();
            PopulateCarousel();
        }

        public void UpdateScales()
        {
            _hasLayout = false;
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
        private readonly Dictionary<GameObject, uint> _slotToPassengerNetId = new();
        private Dictionary<GameObject, int> _slotToIndex = new();
        private Dictionary<GameObject, int> _slotToState = new();
        private readonly HashSet<GameObject> _exitingSlots = new();

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
        internal void AnimatePassengerExit(DrifterBagController controller, uint passengerNetId, bool wasDestroyed)
        {
            if (passengerNetId == 0) return;
            foreach (var slot in _slots)
            {
                if (_slotToPassengerNetId.TryGetValue(slot, out uint id) && id == passengerNetId &&
                    _slotToPassenger.TryGetValue(slot, out var passenger) && !ReferenceEquals(passenger, null))
                {
                    AnimatePassengerExit(controller, passenger!, wasDestroyed);
                    return;
                }
            }
        }

        internal void AnimatePassengerExit(DrifterBagController controller, GameObject passenger, bool wasDestroyed)
        {
            if (!isActiveAndEnabled || HudEditorManager.IsEditorActive || GetOrRefreshBagController() != controller) return;
            bool destroyed = wasDestroyed || IsDestroyedPassenger(passenger);
            var matches = _slots.Where(slot => _slotToPassenger.TryGetValue(slot, out var value) &&
                ReferenceEquals(value, passenger)).ToList();
            var visible = matches.Where(slot => slot.activeInHierarchy)
                .OrderByDescending(slot => slot.GetComponent<CanvasGroup>()?.alpha ?? 0f).FirstOrDefault();
            if (visible != null && (visible.GetComponent<CanvasGroup>()?.alpha ?? 0f) > 0.01f &&
                PluginConfig.Instance.CarouselAnimationDuration.Value > 0.001f &&
                (destroyed ? PluginConfig.Instance.EnableCardDestructionAnimation.Value : PluginConfig.Instance.EnableCardReleaseAnimation.Value))
            {
                foreach (var warning in visible.GetComponentsInChildren<BreakoutWarningOverlay>(true))
                    warning.enabled = false;
                _departureVisuals.RemoveAll(visual => visual == null);
                if (_departureVisuals.Count >= 8)
                {
                    Destroy(_departureVisuals[0].gameObject);
                    _departureVisuals.RemoveAt(0);
                }
                _departureVisuals.Add(CarouselCardExit.Create(visible, destroyed));
            }
            foreach (var slot in matches)
            {
                if (_activeCoroutines.TryGetValue(slot, out var animation)) StopCoroutine(animation);
                _activeCoroutines.Remove(slot);
                _exitingSlots.Remove(slot);
                _slotToPassenger.Remove(slot);
                _slotToPassengerNetId.Remove(slot);
                _slotToState.Remove(slot);
                _slotToIndex.Remove(slot);
                slot.SetActive(false);
            }
            if (matches.Count > 0) _hasLayout = false;
        }

        private static bool IsDestroyedPassenger(GameObject passenger)
        {
            if (passenger == null) return true;
            var health = passenger.GetComponent<HealthComponent>();
            if (health != null) return !health.alive;
            var attributes = passenger.GetComponent<SpecialObjectAttributes>();
            return attributes != null && attributes.durability <= 0f;
        }

        public void PopulateCarousel(int direction = 0)
        {
            if (_slots.Count == 0)
            {
                _hasLayout = false;
                return;
            }
            DrifterBagController? bagController = GetOrRefreshBagController();

            if (bagController == null)
            {
                if (!HudEditorManager.IsEditorActive)
                {
                    foreach (var s in _slots) s.SetActive(false);
                    _slotToPassenger.Clear();
                    _slotToPassengerNetId.Clear();
                    _slotToIndex.Clear();
                    _slotToState.Clear();
                    _hasLayout = false;
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

            foreach (var passenger in _slotToPassenger.Values.Distinct().ToList())
            {
                if (ReferenceEquals(passenger, null) || passenger == EmptySlotMarker) continue;
                if (!passengerList.Contains(passenger!) && bagController != null && IsDestroyedPassenger(passenger!))
                    AnimatePassengerExit(bagController, passenger!, true);
            }

            if (passengerList.Count == 0 && mainPassenger == null)
            {
                _hasLayout = false;
                _layoutPassengers.Clear();
                _layoutMainPassenger = null;
                _pendingPromotionPassenger = null;
                _promotionWaitUntil = 0f;
                if (!HudEditorManager.IsEditorActive)
                {
                    foreach (var s in _slots) s.SetActive(false);
                    _slotToPassenger.Clear();
                    _slotToPassengerNetId.Clear();
                    _slotToIndex.Clear();
                    _slotToState.Clear();
                    return;
                }

            }

            if (mainPassenger != null && !passengerList.Contains(mainPassenger))
            {
                mainPassenger = null;
            }

            if (!_layoutPassengers.SequenceEqual(passengerList)) direction = 0;

            if (mainPassenger == null && passengerList.Count > 0 && direction == 0 &&
                bagController != null && Networking.AuthorityGuard.ShouldAutoPromote(bagController) &&
                !ReferenceEquals(_layoutMainPassenger, null) && !passengerList.Contains(_layoutMainPassenger!))
            {
                if (!ReferenceEquals(_pendingPromotionPassenger, _layoutMainPassenger))
                {
                    _pendingPromotionPassenger = _layoutMainPassenger;
                    _promotionWaitUntil = Time.unscaledTime + 0.12f;
                }
                if (Time.unscaledTime < _promotionWaitUntil) return;
            }
            else
            {
                _pendingPromotionPassenger = null;
                _promotionWaitUntil = 0f;
            }

            unchecked
            {
                int stateHash = passengerList.Count;
                for (int i = 0; i < passengerList.Count; i++)
                {
                    stateHash = (stateHash * 397) ^ (passengerList[i] ? passengerList[i].GetInstanceID() : 0);
                }
                stateHash = (stateHash * 397) ^ (mainPassenger != null ? mainPassenger.GetInstanceID() : 0);
                if (!_hasPassengerStateHash || stateHash != _lastPassengerStateHash || direction != 0)
                {
                    ResetInactivityTimer();
                    _lastPassengerStateHash = stateHash;
                    _hasPassengerStateHash = true;
                }
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
            int cycleCount = Mathf.Max(1, passengerList.Count + (!isBagFull || mainPassenger == null ? 1 : 0));
            _layoutCycleCount = cycleCount;
            var window = GetVisibleWindow(cycleCount);
            int requiredSlots = (window.max - window.min + 1) * 2 + 3;
            if (_slots.Count > 0)
            {
                while (_slots.Count < requiredSlots)
                {
                    var extra = Instantiate(_slots[0], transform);
                    extra.name = $"extraSlot_{_slots.Count}";
                    extra.SetActive(false);
                    if (!extra.GetComponent<CanvasGroup>()) extra.AddComponent<CanvasGroup>();
                    ApplyWeightIconTransform(extra);
                    CleanIncompatibleSlotComponents(extra);
                    _slots.Add(extra);
                }
            }
            var selectionState = bagController != null ? BagPatches.GetState(bagController) : new BagState();
            int emptySlotIndex = selectionState.GetEmptySlotIndex(passengerList.Count);
            bool layoutUnchanged = _hasLayout && _layoutEmptySlotIndex == emptySlotIndex && _layoutMainPassenger == mainPassenger &&
                _layoutCapacity == animationCapacity && _layoutPassengers.SequenceEqual(passengerList);
            if (layoutUnchanged)
            {
                foreach (var slot in _slots)
                {
                    if (!slot.activeSelf || _exitingSlots.Contains(slot) || !_slotToPassenger.TryGetValue(slot, out var passenger)) continue;
                    var group = slot.GetComponent<CanvasGroup>();
                    float alpha = group != null ? group.alpha : 1f;
                    int index = passenger != null ? passengerList.IndexOf(passenger) : -1;
                    SetSlotData(slot, passenger, bagController, slot == centerInstance, index, passengerList.Count);
                    if (group != null) group.alpha = alpha;
                }
                return;
            }
            _layoutPassengers.Clear();
            _layoutPassengers.AddRange(passengerList);
            _layoutMainPassenger = mainPassenger;
            _layoutCapacity = animationCapacity;
            _layoutEmptySlotIndex = emptySlotIndex;
            _hasLayout = true;

            bool includeEmpty = !isBagFull || mainPassenger == null;
            int selectedPosition = mainPassenger == null ? selectionState.GetEmptySlotIndex(passengerList.Count)
                : includeEmpty ? selectionState.ToSequenceIndex(currentIndex, passengerList.Count) : currentIndex;
            var targetStates = GetTargetStates(cycleCount);
            if (cycleCount == 2 && direction > 0 && window.min < 0 && window.max > 0)
                targetStates = targetStates.OrderBy(state => state == 0 ? 0 : state == -1 ? 1 : state == 1 ? 2 : 3).ToList();
            foreach (int state in targetStates)
            {
                int passengerIndex = ((selectedPosition + state) % cycleCount + cycleCount) % cycleCount;
                int objectIndex = includeEmpty ? selectionState.ToObjectIndex(passengerIndex, passengerList.Count) : passengerIndex;
                targetPassengers[state] = objectIndex >= 0 && objectIndex < passengerList.Count ? passengerList[objectIndex] : EmptySlotMarker;
            }

            Dictionary<GameObject?, int> passengerToIndex = new();
            for (int pi = 0; pi < passengerList.Count; pi++)
            {
                passengerToIndex[passengerList[pi]] = pi;
            }

            Vector2 animationOffset = Vector2.zero;
            if (direction != 0)
            {
                foreach (var slot in _slots)
                {
                    if (_exitingSlots.Contains(slot) || !_slotToState.TryGetValue(slot, out int previousState) || !IsVisibleState(previousState, cycleCount) ||
                        !_slotToPassenger.TryGetValue(slot, out var passenger) ||
                        !targetPassengers.TryGetValue(previousState - Math.Sign(direction), out var target) || passenger != target) continue;

                    var rect = slot.GetComponent<RectTransform>();
                    if (rect) animationOffset = rect.anchoredPosition - GetStateParams(previousState, cycleCount).pos;
                    break;
                }
            }

            HashSet<GameObject> usedSlots = new();
            HashSet<GameObject?> foundPassengers = new();
            HashSet<GameObject?> wrappedPassengers = new();

            var slotsToProcess = _slots.Where(slot => !_exitingSlots.Contains(slot)).OrderBy(slot =>
            {
                if (!_slotToPassenger.TryGetValue(slot, out var passenger) ||
                    !_slotToState.TryGetValue(slot, out int previousState)) return int.MaxValue;
                int distance = int.MaxValue;
                foreach (var target in targetPassengers)
                    if (IsVisibleState(target.Key, cycleCount) && target.Value == passenger)
                        distance = Mathf.Min(distance, Mathf.Abs(target.Key - previousState));
                return distance;
            }).ToList();
            foreach (var slot in slotsToProcess)
            {
                if (!_slotToPassenger.TryGetValue(slot, out var passenger)) continue;

                int newState = -99;
                foreach (var kvp in targetPassengers)
                {
                    if (kvp.Value == passenger)
                    {
                        if (newState == -99 || (!IsVisibleState(newState, cycleCount) &&
                            _slotToState.TryGetValue(slot, out int previousState) &&
                            Mathf.Abs(kvp.Key - previousState) < Mathf.Abs(newState - previousState)))
                        {
                            newState = kvp.Key;
                        }
                        if (IsVisibleState(newState, cycleCount)) break;
                    }
                }

                bool wraps = cycleCount > 1 && newState != -99 &&
                    _slotToState.TryGetValue(slot, out int oldState) &&
                    (direction != 0 ? newState != oldState - Math.Sign(direction)
                        : (oldState < 0 && newState > 0) || (oldState > 0 && newState < 0));
                if (wraps) wrappedPassengers.Add(passenger);
                if (newState != -99 && !foundPassengers.Contains(passenger) && !wraps)
                {

                    int slotIndex = (passenger != null && passenger != EmptySlotMarker && passengerToIndex.TryGetValue(passenger, out int idx)) ? idx : -1;

                    var cg = slot.GetComponent<CanvasGroup>();
                    float savedAlpha = cg != null ? cg.alpha : 1f;

                    SetSlotData(slot, passenger, bagController, newState == 0, slotIndex, passengerList.Count);

                    if (cg != null) cg.alpha = savedAlpha;
                    slot.SetActive(true);

                    AnimateToState(slot, newState, animationCapacity, bagController);
                    usedSlots.Add(slot);
                    foundPassengers.Add(passenger);
                }
                else
                {

                    _slotToState.TryGetValue(slot, out int oldExitState);
                    int exitState = GetCycleExitState(oldExitState, direction, cycleCount);

                    if (!_exitingSlots.Contains(slot))
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

                    foreach (var slot in _slots.OrderBy(slot => slot.GetComponent<CanvasGroup>()?.alpha ?? 0f))
                    {
                        if (!usedSlots.Contains(slot) && !_exitingSlots.Contains(slot))
                        {
                            freeSlot = slot;
                            break;
                        }
                    }
                }

                if (freeSlot == null && IsVisibleState(state, cycleCount))
                {
                    freeSlot = Instantiate(_slots[0], transform);
                    freeSlot.name = $"extraSlot_{_slots.Count}";
                    freeSlot.SetActive(false);
                    if (!freeSlot.GetComponent<CanvasGroup>()) freeSlot.AddComponent<CanvasGroup>();
                    ApplyWeightIconTransform(freeSlot);
                    CleanIncompatibleSlotComponents(freeSlot);
                    _slots.Add(freeSlot);
                }

                if (freeSlot)
                {
                    if (_activeCoroutines.TryGetValue(freeSlot!, out var animation))
                    {
                        StopCoroutine(animation);
                        _activeCoroutines.Remove(freeSlot!);
                    }
                    _exitingSlots.Remove(freeSlot!);
                    _slotToPassenger[freeSlot!] = targetP;
                    _slotToPassengerNetId[freeSlot!] = targetP != null ? (targetP.GetComponent<UnityEngine.Networking.NetworkIdentity>()?.netId.Value ?? 0u) : 0u;
                    int slotIndex = (targetP != null && targetP != EmptySlotMarker && passengerToIndex.TryGetValue(targetP!, out int idx)) ? idx : -1;
                    _slotToIndex[freeSlot!] = slotIndex;
                    SetSlotData(freeSlot!, targetP, bagController, state == 0, slotIndex, passengerList.Count);

                    int startState = (direction > 0) ? state + 1 : state - 1;
                    if (direction == 0) startState = wrappedPassengers.Contains(targetP) ? state + Math.Sign(state) : state;

                    var startParams = GetStateParams(startState, cycleCount);
                    _slotToState[freeSlot!] = startState;
                    SetSlotInitialState(freeSlot!, startParams.pos.x + animationOffset.x, startParams.pos.y + animationOffset.y, startParams.scale, 0f);
                    freeSlot!.SetActive(true);

                    AnimateToState(freeSlot!, state, animationCapacity, bagController);
                    usedSlots.Add(freeSlot);
                    foundPassengers.Add(targetP);
                }
            }

            centerInstance = null;
            foreach (var slot in _slots)
            {
                if (usedSlots.Contains(slot) && _slotToState.TryGetValue(slot, out int slotState) && slotState == 0 &&
                    _slotToPassenger.TryGetValue(slot, out var p) && p == targetPassengers[0])
                {
                    centerInstance = slot;
                    break;
                }
            }
            aboveInstance = null;
            foreach (var slot in _slots)
            {
                if (usedSlots.Contains(slot) && _slotToState.TryGetValue(slot, out int slotState) && slotState == 1 &&
                    _slotToPassenger.TryGetValue(slot, out var p) && p == targetPassengers[1])
                {
                    aboveInstance = slot;
                    break;
                }
            }
            belowInstance = null;
            foreach (var slot in _slots)
            {
                if (usedSlots.Contains(slot) && _slotToState.TryGetValue(slot, out int slotState) && slotState == -1 &&
                    _slotToPassenger.TryGetValue(slot, out var p) && p == targetPassengers[-1])
                {
                    belowInstance = slot;
                    break;
                }
            }

            if (centerInstance) centerInstance!.transform.SetAsLastSibling();

            foreach (var slot in _slots)
            {
                if (!_exitingSlots.Contains(slot) && _slotToPassenger.TryGetValue(slot, out var slotPassenger) && slot.activeSelf)
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
                if (_exitingSlots.Contains(slot)) continue;
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
            if (hideAfter) _exitingSlots.Add(slot);
            else _exitingSlots.Remove(slot);
            var p = GetStateParams(state, _layoutCycleCount);
            _slotToState[slot] = state;

            float targetOpacity = hideAfter ? 0f : p.opacity;
            if (_slotToPassenger.TryGetValue(slot, out var passenger) && (passenger == null || passenger == EmptySlotMarker))
            {
                targetOpacity = 0f;
            }

            bool useFading = capacity > 1;
            AnimateSlot(slot, p.pos.x, p.pos.y, p.scale, targetOpacity, hideAfter, useFading);
        }

        internal static float EvaluateCycleEase(float t)
            => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

        internal static (int min, int max) GetVisibleWindow(int availableCount)
        {
            int visibleCount = Mathf.Clamp(PluginConfig.Instance.VisibleCardCount.Value, 1, 100);
            int count = Mathf.Min(visibleCount, Mathf.Max(1, availableCount));
            if (availableCount == 2 && visibleCount >= 3 && PluginConfig.Instance.SelectedCardPlacement.Value == SelectedCardPosition.Center)
                count = 3;
            return PluginConfig.Instance.SelectedCardPlacement.Value switch
            {
                SelectedCardPosition.Start => (0, count - 1),
                SelectedCardPosition.End => (-(count - 1), 0),
                _ => (-((count - 1) / 2), count / 2)
            };
        }

        internal static bool IsVisibleState(int state, int availableCount)
        {
            var window = GetVisibleWindow(availableCount);
            return state >= window.min && state <= window.max;
        }

        internal static List<int> GetTargetStates(int availableCount)
        {
            var window = GetVisibleWindow(availableCount);
            var states = new List<int> { 0 };
            int distanceLimit = Mathf.Max(-window.min, window.max);
            for (int distance = 1; distance <= distanceLimit; distance++)
            {
                if (distance <= window.max) states.Add(distance);
                if (-distance >= window.min) states.Add(-distance);
            }
            states.Add(window.min - 1);
            states.Add(window.max + 1);
            return states;
        }

        internal static int GetPassengerState(int index, int selected, int count)
        {
            int relative = ((index - selected) % count + count) % count;
            return relative <= GetVisibleWindow(count).max ? relative : relative - count;
        }

        internal static int GetCycleExitState(int previous, int direction, int availableCount)
        {
            var window = GetVisibleWindow(availableCount);
            if (direction > 0) return window.min - 1;
            if (direction < 0) return window.max + 1;
            return previous <= (window.min + window.max) / 2f ? window.min - 1 : window.max + 1;
        }

        public static (Vector2 pos, float scale, float opacity) GetStateParams(int state, int capacity)
        {
            var cfg = PluginConfig.Instance;
            int distance = Mathf.Abs(state);
            float scale = state == 0 ? cfg.CenterSlotScale.Value : cfg.SideSlotScale.Value;
            float opacity = state == 0 ? cfg.CenterSlotOpacity.Value : cfg.SideSlotOpacity.Value;
            if (distance > 1)
            {
                scale *= Mathf.Max(0.65f, Mathf.Pow(0.9f, distance - 1));
                opacity *= Mathf.Max(0.35f, Mathf.Pow(0.75f, distance - 1));
            }
            if (!IsVisibleState(state, capacity)) opacity = 0f;
            Vector2 pos = Vector2.zero;
            if (state != 0)
            {
                float offset = state * cfg.CarouselSpacing.Value;
                pos = cfg.CarouselOrientation.Value == CarouselOrientation.Horizontal
                    ? new Vector2(cfg.SideSlotX.Value + offset, cfg.SideSlotY.Value)
                    : new Vector2(cfg.SideSlotX.Value, cfg.SideSlotY.Value - offset);
            }
            return (pos, scale, opacity);
        }

        private Dictionary<GameObject, Coroutine> _activeCoroutines = new();

        internal bool IsSlotAnimating(GameObject slot) => _activeCoroutines.ContainsKey(slot);

        internal bool TryGetInspectionLayout(GameObject passenger, out GameObject? selected, out BaggedCardController? card, out Bounds bounds)
        {
            selected = null;
            card = null;
            bounds = default;
            bool hasBounds = false;
            foreach (var slot in _slots)
            {
                if (!slot.activeInHierarchy || !_slotToState.TryGetValue(slot, out int state) || !IsVisibleState(state, _layoutCycleCount) ||
                    !_slotToPassenger.TryGetValue(slot, out var target) || target == null || target == EmptySlotMarker) continue;
                var slotBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, slot.transform);
                if (!hasBounds) { bounds = slotBounds; hasBounds = true; }
                else bounds.Encapsulate(slotBounds);
                if (target == passenger && state == 0)
                {
                    selected = slot;
                    card = slot.GetComponentInChildren<BaggedCardController>();
                }
            }
            return selected != null && card != null && hasBounds;
        }


        internal bool IsInspectionRowObstructed(GameObject selected, Rect row)
        {
            foreach (var slot in _slots)
            {
                if (slot == selected || !slot.activeInHierarchy || !_slotToState.TryGetValue(slot, out int state) ||
                    !IsVisibleState(state, _layoutCycleCount) || !_slotToPassenger.TryGetValue(slot, out var target) ||
                    target == null || target == EmptySlotMarker) continue;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, slot.transform);
                if (row.Overlaps(new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y))) return true;
            }
            return false;
        }

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

            if (HudEditorManager.IsEditorActive || PluginConfig.Instance.CarouselAnimationDuration.Value <= 0.001f || !useFading)
            {
                ApplySlotStateImmediate(slot, x, y, scale, opacity);
                if (hideAfter)
                {
                    slot.SetActive(false);
                    _slotToPassenger.Remove(slot);
                    _slotToPassengerNetId.Remove(slot);
                    _slotToState.Remove(slot);
                    _exitingSlots.Remove(slot);
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

        internal static System.Collections.IEnumerator AnimateCardVisuals(GameObject slot, float targetXOffset, float targetYOffset, float targetScale, float targetOpacity, bool useFading, bool unscaled)
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
                if (slot == null) yield break;
                elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float easeT = EvaluateCycleEase(t);

                if (rectTransform) rectTransform.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, easeT);
                slot.transform.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, easeT);
                if (useFading)
                {
                    canvasGroup.alpha = Mathf.Lerp(startOpacity, targetOpacity, easeT);
                }

                yield return null;
            }

            if (slot == null) yield break;
            if (rectTransform) rectTransform.anchoredPosition = targetPosition;
            slot.transform.localScale = Vector3.one * targetScale;
            canvasGroup.alpha = targetOpacity;

        }

        private System.Collections.IEnumerator AnimateSlotPosition(GameObject slot, float targetXOffset, float targetYOffset, float targetScale, float targetOpacity, bool hideAfter, bool useFading)
        {
            yield return AnimateCardVisuals(slot, targetXOffset, targetYOffset, targetScale, targetOpacity, useFading, false);

            if (hideAfter && slot != null)
            {
                slot.SetActive(false);
                _slotToPassenger.Remove(slot);
                _slotToPassengerNetId.Remove(slot);
                _slotToState.Remove(slot);
                _exitingSlots.Remove(slot);
            }

            if (slot is not null) _activeCoroutines.Remove(slot);
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
                    ResetHealthBarWithoutSource(baggedCardController.healthBar);

                    if (baggedCardController.nameLabel) baggedCardController.nameLabel.gameObject.SetActive(false);
                    if (baggedCardController.portraitIconImage) baggedCardController.portraitIconImage.gameObject.SetActive(false);
                    if (baggedCardController.droneItemIndicatorObj) baggedCardController.droneItemIndicatorObj.SetActive(false);
                    if (baggedCardController.healthBar)
                    {
                        baggedCardController.healthBar.gameObject.SetActive(false);
                        if (baggedCardController.healthBar.deadImage) baggedCardController.healthBar.deadImage.gameObject.SetActive(false);
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
                    ResetHealthBarWithoutSource(baggedCardController.healthBar);

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
                        baggedCardController.healthBar!.deadImage!.gameObject.SetActive(false);
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

                    UpdateWeightIconVisuals(slot, mass, isCenter, percentage, isOverencumbered);
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

                var warning = slot.GetComponent<BreakoutWarningOverlay>();
                if (warning == null && passenger != null && passenger != EmptySlotMarker)
                    warning = slot.AddComponent<BreakoutWarningOverlay>();
                if (warning != null)
                {
                    warning.enabled = true;
                    warning.SetTarget(passenger == EmptySlotMarker ? null : passenger, bagController, baggedCardController);
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
            bool isCenter = slot == centerInstance;
            bool showSlotNumber = isCenter ? PluginConfig.Instance.CenterSlotShowSlotNumber.Value : PluginConfig.Instance.SideSlotShowSlotNumber.Value;
            SetSlotNumberBadge(slot, slotIndex >= 0 ? slotIndex + 1 : -1, isCenter, showSlotNumber);
        }

        public static void SetSlotNumberBadge(GameObject slot, int displayNumber, bool isCenter, bool showSlotNumber)
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
                if (displayNumber > 0)
                {
                    slotNumberTmp.text = $"{displayNumber}";
                    if (badgeTransform is RectTransform badgeRect)
                        badgeRect.sizeDelta = new Vector2(Mathf.Max(16f, slotNumberTmp.text.Length * 7f + 4f), badgeRect.sizeDelta.y);
                    badgeTransform.gameObject.SetActive(showSlotNumber);
                }
                else
                {
                    badgeTransform.gameObject.SetActive(false);
                }
            }
        }

        public static void UpdateWeightIconVisuals(GameObject slot, float mass, bool isCenter, float percentage, bool isOverencumbered = false)
        {
            var childLocator = slot.GetComponent<ChildLocator>();
            if (!childLocator) return;

            var weightIconTransform = childLocator.FindChild("WeightIcon");
            if (!weightIconTransform) return;

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
                            overencumbranceFraction = Mathf.Clamp01(percentage - 1.0f);
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
                                tmp.text = $"<alpha=#00><size=50%><voffset=-0.2em>lb</voffset></size><space=-0.2em><alpha=#FF>{pounds}<space=-0.2em><size=50%><voffset=-0.2em>lb</voffset></size>";
                                if (unitLabel) unitLabel!.gameObject.SetActive(false);
                                break;

                            case DrifterBossGrabMod.WeightDisplayMode.KiloGrams:
                                int kiloGrams = Mathf.FloorToInt(mass / 10f);
                                tmp.text = $"<alpha=#00><size=50%><voffset=-0.2em>kg</voffset></size><space=-0.2em><alpha=#FF>{kiloGrams}<space=-0.2em><size=50%><voffset=-0.2em>kg</voffset></size>";
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

        public static void ApplyWeightIconTransformImmediate(GameObject slot)
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

        public static Color GetGradientColor(float percentage, Color start, Color mid, Color end)
        {
            percentage = Mathf.Clamp01(percentage);
            if (percentage <= 0.5f)
                return Color.Lerp(start, mid, percentage * 2f);
            else
                return Color.Lerp(mid, end, (percentage - 0.5f) * 2f);
        }
    }
}
