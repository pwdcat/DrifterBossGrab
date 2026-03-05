using UnityEngine;
using UnityEngine.Networking;
using RoR2;
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
    public class BaggedObjectCarousel : MonoBehaviour
    {
        public GameObject? slotPrefab;
        public float sideScale = 0.8f;

        private static Texture2D? _weightIconTexture;
        private static Texture2D? WeightIconTexture => _weightIconTexture ??= LoadWeightIconTexture();

        private static Sprite? _newWeightIconSprite;
        private static Sprite? NewWeightIconSprite => _newWeightIconSprite ??= (WeightIconTexture != null ? Sprite.Create(WeightIconTexture, new Rect(0, 0, WeightIconTexture.width, WeightIconTexture.height), new Vector2(0.5f, 0.5f)) : null);

        private static Sprite? _oldWeightIconSprite;
        private static Sprite OldWeightIconSprite => _oldWeightIconSprite ??= Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/texMovespeedBuffIcon.tif").WaitForCompletion();

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

        public void UpdateToggles()
        {
            bool isEnabled = PluginConfig.Instance.EnableCarouselHUD.Value;

            // If disabled, hide everything
            if (!isEnabled)
            {
                if (aboveInstance) aboveInstance.SetActive(false);
                if (centerInstance) centerInstance.SetActive(false);
                if (belowInstance) belowInstance.SetActive(false);
                foreach (var slot in _slots) slot.SetActive(false);
                // Also hide any extra slots we might have tracked
                return;
            }

            if (aboveInstance)
            {
                aboveInstance.SetActive(true);
                ToggleSlotElements(aboveInstance, false);
            }
            if (centerInstance)
            {
                centerInstance.SetActive(true);
                ToggleSlotElements(centerInstance, true);
            }
            if (belowInstance)
            {
                belowInstance.SetActive(true);
                ToggleSlotElements(belowInstance, false);
            }

            // Re-populate if we just re-enabled, to ensure everything is in correct state
            if (isEnabled && (!centerInstance || !centerInstance.activeSelf))
            {
                PopulateCarousel();
            }
        }

        public void UpdateScales()
        {
            PopulateCarousel(); // Refresh positions and scales
        }

        private GameObject? aboveInstance;
        private GameObject? centerInstance;
        private GameObject? belowInstance;

        // Carousel slots management.
        private List<GameObject> _slots = new();
        private Dictionary<GameObject, GameObject?> _slotToPassenger = new();
        private Dictionary<GameObject, int> _slotToIndex = new();

        // Cached bag controller reference to avoid expensive FindObjectsByType calls
        private DrifterBagController? _cachedBagController = null;

        // Gets or refreshes the cached bag controller reference
        private DrifterBagController? GetOrRefreshBagController()
        {
            // Check if cached controller is still valid
            if (_cachedBagController != null && _cachedBagController.hasAuthority)
            {
                return _cachedBagController;
            }

            // Search for a valid controller with authority
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bc in bagControllers)
            {
                if (bc.hasAuthority)
                {
                    _cachedBagController = bc;
                    return _cachedBagController;
                }
            }

            // No valid controller found
            _cachedBagController = null;
            return null;
        }

        // Gets the actual slot capacity for animation decisions (not mass-cap-limited)
        // This ensures animations play normally even when at mass capacity
        private int GetAnimationCapacity(DrifterBagController bagController)
        {
            if (bagController == null) return 1;

            var body = bagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int addedSlots = 0;
                if (int.TryParse(PluginConfig.Instance.AddedCapacity.Value, out int parsedAdded))
                {
                    addedSlots = parsedAdded;
                }
                int baseSlots = body.skillLocator.utility.maxStock + addedSlots;

                int extraSlots = 0;

                // Add Capacity slots using formula-based scaling
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    var vars = new System.Collections.Generic.Dictionary<string, float>
                    {
                        ["H"] = body.maxHealth,
                        ["L"] = body.level,
                        ["C"] = body.skillLocator.utility.maxStock,
                        ["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1
                    };
                    extraSlots = Balance.FormulaParser.EvaluateInt(
                        PluginConfig.Instance.SlotScalingFormula.Value, vars);
                }

                int slotCapacity = baseSlots + extraSlots;

                // If BottomlessBag is enabled with INF capacity, return a large value
                if (PluginConfig.Instance.BottomlessBagEnabled.Value &&
                    (PluginConfig.Instance.AddedCapacity.Value.Trim().ToUpper() == "INF" ||
                     PluginConfig.Instance.AddedCapacity.Value.Trim().ToUpper() == "INFINITY"))
                {
                    return int.MaxValue;
                }

                return slotCapacity;
            }

            return 1;
        }

        // Sentinel for empty slot.
        private static GameObject? _emptySlotMarker;
        private static GameObject EmptySlotMarker => _emptySlotMarker ??= new GameObject("EmptySlotMarker");

        private void Start()
        {
            // Gather existing slots
            Transform a = transform.Find("aboveSlot");
            Transform c = transform.Find("centerSlot");
            Transform b = transform.Find("belowSlot");

            if (a) _slots.Add(a.gameObject);
            if (c) _slots.Add(c.gameObject);
            if (b) _slots.Add(b.gameObject);

            // Add extra slots for exit transitions.
            GameObject? template = (c != null) ? c.gameObject : slotPrefab;
            if (template)
            {
                for (int i = 0; i < 6; i++)
                {
                    GameObject extra = Instantiate(template, transform);
                    extra.name = $"extraSlot_{i}";
                    _slots.Add(extra);
                }
            }

            foreach (var s in _slots)
            {
                s.SetActive(false);
                if (!s.GetComponent<CanvasGroup>()) s.AddComponent<CanvasGroup>();
                ApplyWeightIconTransform(s);
            }

            PopulateCarousel();
            UpdateToggles();
        }

        public void PopulateCarousel(int direction = 0)
        {
            DrifterBagController? bagController = GetOrRefreshBagController();

            if (bagController == null)
            {
                foreach (var s in _slots) s.SetActive(false);
                _slotToPassenger.Clear();
                _slotToIndex.Clear();
                return;
            }

            List<GameObject> passengerList = new List<GameObject>();
            GameObject? mainPassenger = null;

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            var localList = BagPatches.GetState(bagController).BaggedObjects;
            // Prioritize local knowledge if we have authority (local player)
            if (bagController.hasAuthority && localList != null)
            {
                passengerList = localList;
                mainPassenger = BagPatches.GetMainSeatObject(bagController);
            }
            else if (netController != null && (!NetworkServer.active || BagPatches.GetState(bagController).BaggedObjects == null))
            {
                // Use networked state for other players or as fallback
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
                    // Use local state on host/server for NPCs or if somehow we missed authority
                    passengerList = fallbackList;
                    mainPassenger = BagPatches.GetMainSeatObject(bagController);
                }
            }

            if (passengerList.Count == 0 && mainPassenger == null)
            {
                foreach (var s in _slots) s.SetActive(false);
                _slotToPassenger.Clear();
                _slotToIndex.Clear();
                return;
            }

            // If the main passenger isn't in our tracking list, treat it as null (empty slot)
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

            // Calculate capacity and check if bag is full (needed for wrap-around logic below)
            int capacity = BagCapacityCalculator.GetUtilityMaxStock(bagController);
            bool isBagFull = passengerList.Count >= capacity;

            // Get actual slot capacity for animation decisions (not mass-cap-limited)
            // This ensures animations play normally even when at mass capacity
            int animationCapacity = GetAnimationCapacity(bagController);

            if (mainPassenger == null)
            {
                // Current is Empty
                targetPassengers[0] = EmptySlotMarker;
                // Above (+1) is first item
                targetPassengers[1] = (passengerList.Count > 0) ? passengerList[0] : null;
                // Below (-1) is last item
                targetPassengers[-1] = (passengerList.Count > 0) ? passengerList[passengerList.Count - 1] : null;
                // Hidden Above (+2)
                targetPassengers[2] = (passengerList.Count > 1) ? passengerList[1] : null;
                // Hidden Below (-2)
                targetPassengers[-2] = (passengerList.Count > 1) ? passengerList[passengerList.Count - 2] : null;
            }
            else
            {
                // Current is a passenger
                targetPassengers[0] = mainPassenger;

                // Above (+1) - wrap around if bag is full
                int aboveIndex = currentIndex + 1;
                if (aboveIndex < passengerList.Count)
                {
                    targetPassengers[1] = passengerList[aboveIndex];
                }
                else if (isBagFull && passengerList.Count > 0)
                {
                    // Wrap around to first item when bag is full
                    targetPassengers[1] = passengerList[0];
                }
                else
                {
                    targetPassengers[1] = EmptySlotMarker; // Shows empty if next is null
                }

                // Below (-1) - wrap around if bag is full
                int belowIndex = currentIndex - 1;
                if (belowIndex >= 0)
                {
                    targetPassengers[-1] = passengerList[belowIndex];
                }
                else if (isBagFull && passengerList.Count > 0)
                {
                    // Wrap around to last item when bag is full
                    targetPassengers[-1] = passengerList[passengerList.Count - 1];
                }
                else
                {
                    targetPassengers[-1] = EmptySlotMarker; // Shows empty if prev is null
                }

                // Hidden Above (+2)
                int hiddenAbove = currentIndex + 2;
                if (hiddenAbove < passengerList.Count) targetPassengers[2] = passengerList[hiddenAbove];
                else if (hiddenAbove == passengerList.Count) targetPassengers[2] = EmptySlotMarker;
                else if (passengerList.Count > 0) targetPassengers[2] = passengerList[0];
                else targetPassengers[2] = EmptySlotMarker;

                // Hidden Below (-2)
                int hiddenBelow = currentIndex - 2;
                if (hiddenBelow >= 0) targetPassengers[-2] = passengerList[hiddenBelow];
                else if (hiddenBelow == -1) targetPassengers[-2] = EmptySlotMarker;
                else if (passengerList.Count > 0) targetPassengers[-2] = passengerList[passengerList.Count - 1];
                else targetPassengers[-2] = EmptySlotMarker;
            }

            // Build passenger-to-index mapping (1-based)
            Dictionary<GameObject?, int> passengerToIndex = new();
            for (int pi = 0; pi < passengerList.Count; pi++)
            {
                passengerToIndex[passengerList[pi]] = pi + 1; // 1-based
            }

            float sideScaleVal = PluginConfig.Instance.SideSlotScale.Value;
            float sideOpacityVal = PluginConfig.Instance.SideSlotOpacity.Value;

            // 1. Identify which slots are still active and update their target states
            HashSet<GameObject> usedSlots = new();
            HashSet<GameObject?> foundPassengers = new();

            // First, update existing slots showing passengers that are still in our 5-item window
            var slotsToProcess = _slots.ToList(); // Copy to allow modification of dictionary
            foreach (var slot in slotsToProcess)
            {
                if (!_slotToPassenger.TryGetValue(slot, out var passenger)) continue;

                // Where is this passenger now in our 5-item window?
                int newState = -99;
                foreach (var kvp in targetPassengers)
                {
                    if (kvp.Value == passenger) { newState = kvp.Key; break; }
                }

                if (newState != -99 && !foundPassengers.Contains(passenger))
                {
                    // Still in window and not yet assigned to another slot in this update
                    int slotIndex = (passenger != null && passenger != EmptySlotMarker && passengerToIndex.TryGetValue(passenger, out int idx)) ? idx : -1;
                    
                    // Preserve current alpha so SetSlotData doesn't disrupt in-progress animations or starting opacity
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
                    // No longer in window or redundant - Animate to exit state
                    int exitState = (direction > 0) ? -2 : 2; // Move down if next, up if prev
                    if (direction == 0) exitState = -2; // Default

                    AnimateToState(slot, exitState, animationCapacity, bagController, true); // Hide after
                }
            }

            // 2. Assign slots for new passengers entering the window
            foreach (var kvp in targetPassengers)
            {
                int state = kvp.Key;
                GameObject? targetP = kvp.Value;

                if (targetP != null && targetP != EmptySlotMarker && foundPassengers.Contains(targetP)) continue;
                if (targetP == EmptySlotMarker && foundPassengers.Contains(EmptySlotMarker)) continue;

                // Find an idle slot
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
                    // Steal an exit slot if needed
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
                    _slotToPassenger[freeSlot] = targetP;
                    int slotIndex = (targetP != null && targetP != EmptySlotMarker && passengerToIndex.TryGetValue(targetP, out int idx)) ? idx : -1;
                    _slotToIndex[freeSlot] = slotIndex;
                    SetSlotData(freeSlot, targetP, bagController, state == 0, slotIndex, passengerList.Count);

                    // Set initial position based on where it's coming from
                    int startState = (direction > 0) ? state + 1 : state - 1;
                    if (direction == 0) startState = state; // Snap if no direction

                    var startParams = GetStateParams(startState, animationCapacity);
                    SetSlotInitialState(freeSlot, startParams.pos.x, startParams.pos.y, startParams.scale, 0f);
                    freeSlot.SetActive(true);

                    AnimateToState(freeSlot, state, animationCapacity, bagController);
                    usedSlots.Add(freeSlot);
                    foundPassengers.Add(targetP);
                }
            }

            // 3. Update compatibility references (for UpdateToggles)
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

            // Ensure Center is on top
            if (centerInstance) centerInstance.transform.SetAsLastSibling();

            // Re-apply toggles for all active slots now that centerInstance is correctly set
            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var slotPassenger) && slot.activeSelf)
                {
                    bool isCenter = slot == centerInstance;
                    ToggleSlotElements(slot, isCenter);

                    // Refresh damage preview overlay with correct isCenter
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
                            if (slotPassenger != null && slotPassenger != EmptySlotMarker)
                                overlay.SetTarget(slotPassenger, bagController);
                        }
                        else if (overlay)
                        {
                            Destroy(overlay);
                        }
                    }
                }
            }

            // 4. Update slot number labels for ALL active slots (not just new ones)
            // This ensures labels refresh when the passenger list changes (e.g., after throwing)
            foreach (var slot in _slots)
            {
                if (_slotToPassenger.TryGetValue(slot, out var passenger) && passenger != null && passenger != EmptySlotMarker)
                {
                    int idx = passengerToIndex.TryGetValue(passenger, out int i) ? i : -1;
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

        private void AnimateToState(GameObject slot, int state, int capacity, DrifterBagController bagController, bool hideAfter = false)
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
            float centerX = PluginConfig.Instance.CenterSlotX.Value;
            float centerY = PluginConfig.Instance.CenterSlotY.Value;
            float sideX = PluginConfig.Instance.SideSlotX.Value;
            float sideY = PluginConfig.Instance.SideSlotY.Value;
            float spacing = PluginConfig.Instance.CarouselSpacing.Value;

            float scale = (state == 0) ? PluginConfig.Instance.CenterSlotScale.Value : PluginConfig.Instance.SideSlotScale.Value;
            float opacity = (state == 0) ? PluginConfig.Instance.CenterSlotOpacity.Value : PluginConfig.Instance.SideSlotOpacity.Value;

            if (Mathf.Abs(state) > 1) {
                opacity = 0f; // Hidden states
                scale *= 0.8f;
            }

            Vector2 pos;
            switch (state)
            {
                case 0:  pos = new Vector2(centerX, centerY); break;
                case 1:  pos = new Vector2(centerX + sideX, centerY + sideY - spacing); break;
                case -1: pos = new Vector2(centerX + sideX, centerY + sideY + spacing); break;
                case 2:  pos = new Vector2(centerX + sideX, centerY + sideY - 2 * spacing); break;
                case -2: pos = new Vector2(centerX + sideX, centerY + sideY + 2 * spacing); break;
                case 3:  pos = new Vector2(centerX + sideX, centerY + sideY - 3 * spacing); break;
                case -3: pos = new Vector2(centerX + sideX, centerY + sideY + 3 * spacing); break;
                default: pos = new Vector2(centerX, centerY); break;
            }
            return (pos, scale, opacity);
        }

        private Dictionary<GameObject, Coroutine> _activeCoroutines = new();

        private void AnimateSlot(GameObject slot, float x, float y, float scale, float opacity, bool hideAfter, bool useFading)
        {
            if (_activeCoroutines.TryGetValue(slot, out var existing) && existing != null)
            {
                StopCoroutine(existing);
                // Don't remove from dict here, it will be overwritten
            }

            // If duration is effectively zero OR capacity = 1 (useFading = false), apply everything immediately for snappy UI
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

        private void AnimateSlot(GameObject slot, float x, float y, float scale, float opacity)
        {
            AnimateSlot(slot, x, y, scale, opacity, false, true);
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
                // Ease In Out Cubic
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

        private void SetSlotData(GameObject slot, GameObject? passenger, DrifterBagController bagController, bool isCenter, int slotIndex = -1, int totalCount = 0)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[Carousel] SetSlotData called for passenger: {(passenger != null ? passenger.name : "null")}, isCenter: {isCenter}, slotIndex: {slotIndex}");
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
                var canvasGroup = slot.GetComponent<CanvasGroup>();

                // Check for empty slot marker first
                if (passenger == EmptySlotMarker || passenger == null)
                {
                    // Empty slot state - fully invisible
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

                    if (canvasGroup) canvasGroup.alpha = 0f;

                    var childLocator = slot.GetComponent<ChildLocator>();
                    if (childLocator)
                    {
                            var weightIconTransform = childLocator.FindChild("WeightIcon");
                            if (weightIconTransform)
                            {
                                weightIconTransform.gameObject.SetActive(false);
                                var tmp = weightIconTransform.Find("WeightText")?.GetComponent<TextMeshProUGUI>();
                                if (tmp) tmp.gameObject.SetActive(false);

                                var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();
                                if (unitLabel) unitLabel.gameObject.SetActive(false);
                            }
                    }
                }
                else
                {
                    var specialObjectAttributes = passenger.GetComponent<SpecialObjectAttributes>();
                    var body = passenger.GetComponent<CharacterBody>();
                    var master = passenger.GetComponent<CharacterMaster>();

                    baggedCardController.sourceBody = body;
                    baggedCardController.sourceMaster = master;
                    baggedCardController.sourcePassengerAttributes = specialObjectAttributes;
                    baggedCardController.ForceUpdate();

                    if (baggedCardController.healthBar && baggedCardController.healthBar.deadImage)
                    {
                        baggedCardController.healthBar.deadImage.enabled = false;
                    }

                    // Set weight icon
                    // Calculate individual mass for the passenger
                    float mass = (bagController == passenger) ? bagController.baggedMass : bagController.CalculateBaggedObjectMass(passenger);
                    float baseMass = mass;

                    // Override with total bag mass ONLY for center slot when toggle is ON and mode is ALL
                    bool showTotal = PluginConfig.Instance.ShowTotalMassOnWeightIcon.Value;
                    bool isAllMode = PluginConfig.Instance.StateCalculationMode.Value == StateCalculationMode.All;
                    
                    if (isCenter && showTotal && isAllMode)
                    {
                        // Use calculated total mass rather than bagController.baggedMass, 
                        // as bagController.baggedMass is physically clamped by massCap!
                        mass = BagCapacityCalculator.GetBaggedObjectMass(bagController);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Carousel] Center slot mass overridden to total: {mass} (individual: {baseMass})");
                    }
                    else if (isCenter)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[Carousel] Center slot mass NOT overridden. ShowTotal: {showTotal}, AllMode: {isAllMode}, Individual: {mass}, BaggedMass: {bagController.baggedMass}");
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
                                // Set icon and position/rotation
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

                                // Set color
                                if (PluginConfig.Instance.ScaleWeightColor.Value)
                                {
                                    float capacity = CapacityScalingSystem.CalculateMassCapacity(bagController);
                                    float percentage = (capacity > 0) ? (mass / capacity) : 0f;

                                    if (percentage > 1.0f)
                                    {
                                        // Calculate the fraction of overencumbrance we are currently at
                                        // 1.0 = Start (0% over base)
                                        // 1.0 + (Max / 100) = End (100% overencumbered)
                                        float maxOverPercent = PluginConfig.Instance.EnableBalance.Value 
                                            ? PluginConfig.Instance.OverencumbranceMax.Value / 100.0f 
                                            : 0.01f; // Prevent div by zero
                                            
                                        float overencumbranceFraction = Mathf.Clamp01((percentage - 1.0f) / maxOverPercent);

                                        // Use Overencumbrance Gradient for overencumbered display
                                        image.color = GetGradientColor(overencumbranceFraction,
                                            PluginConfig.Instance.OverencumbranceGradientColorStart.Value,
                                            PluginConfig.Instance.OverencumbranceGradientColorMid.Value,
                                            PluginConfig.Instance.OverencumbranceGradientColorEnd.Value);
                                    }
                                    else
                                    {
                                        // Use Capacity Gradient for regular/multiplier display
                                        image.color = GetGradientColor(percentage,
                                            PluginConfig.Instance.CapacityGradientColorStart.Value,
                                            PluginConfig.Instance.CapacityGradientColorMid.Value,
                                            PluginConfig.Instance.CapacityGradientColorEnd.Value);
                                    }
                                }
                                else
                                {
                                    image.color = UnityEngine.Color.white;
                                }

                                    // Set text
                                    var weightDisplayMode = PluginConfig.Instance.WeightDisplayMode.Value;
                                    if (weightDisplayMode != DrifterBossGrabMod.WeightDisplayMode.None)
                                    {
                                        // Find the WeightText specifically (not any TextMeshProUGUI child)
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
                                    }
                                    var tmpRectTransform = tmp.GetComponent<RectTransform>();
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

                                    // Handle different display modes
                                    switch (weightDisplayMode)
                                    {
                                        case DrifterBossGrabMod.WeightDisplayMode.Multiplier:
                                            int multiplier = Mathf.CeilToInt(mass / 100f);
                                            tmp.text = multiplier + "x";
                                            if (unitLabel) unitLabel.gameObject.SetActive(false);
                                            break;

                                        case DrifterBossGrabMod.WeightDisplayMode.Pounds:
                                            int pounds = Mathf.FloorToInt(mass / 10f);
                                            tmp.text = pounds.ToString();
                                            if (unitLabel) unitLabel.gameObject.SetActive(true);
                                            else CreateUnitLabel(weightIconTransform, "lb");
                                            break;

                                        case DrifterBossGrabMod.WeightDisplayMode.KiloGrams:
                                            int kiloGrams = Mathf.FloorToInt(mass / 10f);
                                            tmp.text = kiloGrams.ToString();
                                            if (unitLabel) unitLabel.gameObject.SetActive(true);
                                            else CreateUnitLabel(weightIconTransform, "kg");
                                            break;
                                    }

                                    tmp.gameObject.SetActive(true);
                                }
                                else
                                {
                                    var tmp = weightIconTransform.Find("WeightText")?.GetComponent<TextMeshProUGUI>();
                                    if (tmp) tmp.gameObject.SetActive(false);

                                    var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();
                                    if (unitLabel) unitLabel.gameObject.SetActive(false);
                                }
                            }
                        }
                    }
                }
                // Normal passenger logic continues below... (removed redundant else)

                // Damage preview overlay
                // If AoE slam damage is off, only show preview on the selected (center) slot.
                // If AoE is on, show preview on all slots since damage is distributed.
                bool showPreview = PluginConfig.Instance.EnableDamagePreview.Value &&
                    (isCenter || PluginConfig.Instance.AoEDamageDistribution.Value != DrifterBossGrabMod.AoEDamageMode.None);
                if (baggedCardController.healthBar)
                {
                    var overlay = baggedCardController.healthBar.GetComponent<DamagePreviewOverlay>();
                    if (showPreview)
                    {
                        if (!overlay)
                            overlay = baggedCardController.healthBar.gameObject.AddComponent<DamagePreviewOverlay>();
                        if (passenger != null) overlay.SetTarget(passenger, bagController);
                    }
                    else if (overlay)
                    {
                        // Disable overlay if it exists but shouldn't show
                        Destroy(overlay);
                    }
                }

                // Apply toggles
                ToggleSlotElements(slot, isCenter);

                // Set slot number label
                SetSlotNumberLabel(slot, slotIndex, totalCount);
            }
        }

        private void ToggleSlotElements(GameObject slot, bool isCenter)
        {
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
                // Determine which slot type's config to read based on isCenter
                bool showIcon = isCenter ? PluginConfig.Instance.CenterSlotShowIcon.Value : PluginConfig.Instance.SideSlotShowIcon.Value;
                bool showWeight = isCenter ? PluginConfig.Instance.CenterSlotShowWeightIcon.Value : PluginConfig.Instance.SideSlotShowWeightIcon.Value;
                bool showName = isCenter ? PluginConfig.Instance.CenterSlotShowName.Value : PluginConfig.Instance.SideSlotShowName.Value;
                bool showHealthBar = isCenter ? PluginConfig.Instance.CenterSlotShowHealthBar.Value : PluginConfig.Instance.SideSlotShowHealthBar.Value;
                bool showSlotNumber = isCenter ? PluginConfig.Instance.CenterSlotShowSlotNumber.Value : PluginConfig.Instance.SideSlotShowSlotNumber.Value;

                // Toggle portrait/icon
                if (baggedCardController.portraitIconImage)
                {
                    // Portrait is now always active if the slot is active
                    baggedCardController.portraitIconImage.gameObject.SetActive(true);
                }

                // Toggle icon via LayoutElement
                var layoutElement = baggedCardController.portraitIconImage?.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layoutElement)
                {
                    layoutElement.gameObject.SetActive(showIcon);
                }

                // Toggle weight
                var childLocator = slot.GetComponent<ChildLocator>();
                if (childLocator)
                {
                    var weightIconTransform = childLocator.FindChild("WeightIcon");
                    if (weightIconTransform)
                    {
                        weightIconTransform.gameObject.SetActive(showWeight);

                        // Toggle unit label.
                        var unitLabel = weightIconTransform.Find("WeightUnitLabel")?.GetComponent<TextMeshProUGUI>();
                        if (unitLabel)
                        {
                            unitLabel.gameObject.SetActive(showWeight &&
                                PluginConfig.Instance.WeightDisplayMode.Value != DrifterBossGrabMod.WeightDisplayMode.None &&
                                PluginConfig.Instance.WeightDisplayMode.Value != DrifterBossGrabMod.WeightDisplayMode.Multiplier);
                        }
                    }
                }

                // Toggle name
                if (baggedCardController.nameLabel)
                {
                    baggedCardController.nameLabel.gameObject.SetActive(showName);
                }

                // Toggle health bar
                if (baggedCardController.healthBar)
                {
                    baggedCardController.healthBar.gameObject.SetActive(showHealthBar);
                }

                // Toggle slot number badge (parented to portrait icon)
                if (baggedCardController.portraitIconImage)
                {
                    var slotNumberBadge = baggedCardController.portraitIconImage.transform.Find("SlotNumberBadge");
                    if (slotNumberBadge)
                    {
                        slotNumberBadge.gameObject.SetActive(showSlotNumber);
                    }
                }
            }
        }

        private void SetSlotNumberLabel(GameObject slot, int slotIndex, int totalCount)
        {
            // Find or create the badge - it's parented to the portrait icon
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
                // Create badge container - parented to portrait icon
                var badgeObj = new GameObject("SlotNumberBadge");
                badgeObj.transform.SetParent(portraitTransform, false);

                var badgeRect = badgeObj.AddComponent<RectTransform>();
                // Anchor to top-right corner of portrait, pivot top-right so it stays inside
                badgeRect.anchorMin = new Vector2(1f, 1f);
                badgeRect.anchorMax = new Vector2(1f, 1f);
                badgeRect.pivot = new Vector2(1f, 1f);
                badgeRect.sizeDelta = new Vector2(16, 16);
                badgeRect.anchoredPosition = new Vector2(-2f, -2f); // Inset 2px from the corner

                var bgImage = badgeObj.AddComponent<UnityEngine.UI.Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0.85f);
                bgImage.raycastTarget = false;
                
                // Load requested texture via Addressables
                var outlineTex = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Texture2D>("RoR2/Base/UI/texDetailPanel.png").WaitForCompletion();
                if (outlineTex)
                {
                    bgImage.sprite = Sprite.Create(outlineTex, new UnityEngine.Rect(0, 0, outlineTex.width, outlineTex.height), new Vector2(0.5f, 0.5f));
                    bgImage.type = UnityEngine.UI.Image.Type.Sliced; // Sliced is usually better for UI box outlines
                }

                // Text child
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
                textRect.offsetMin = new Vector2(1f, 0f); // slight left padding
                textRect.offsetMax = new Vector2(-1f, 0f); // slight right padding
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

        private static void CreateUnitLabel(Transform weightIconTransform, string unitText)
        {
            var unitLabelObj = new GameObject("WeightUnitLabel");
            unitLabelObj.transform.SetParent(weightIconTransform, false);
            var unitLabel = unitLabelObj.AddComponent<TextMeshProUGUI>();
            unitLabel.font = RoR2.UI.HGTextMeshProUGUI.defaultLanguageFont;
            unitLabel.text = unitText;
            unitLabel.color = Color.white;
            unitLabel.alignment = TextAlignmentOptions.BottomRight;
            unitLabel.characterSpacing = -6;
            unitLabel.fontSize = 3.5f;

            var unitRectTransform = unitLabel.GetComponent<RectTransform>();
            if (unitRectTransform)
            {
                unitRectTransform.sizeDelta = new Vector2(30, 10);

                // Position unit label at bottom-right of value
                if (PluginConfig.Instance.UseNewWeightIcon.Value)
                {
                    unitRectTransform.anchoredPosition = new Vector2(-10.3f, -2.2f);
                    unitRectTransform.localRotation = Quaternion.identity;
                }
                else
                {
                    unitRectTransform.anchoredPosition = new Vector2(0f, -10f);
                    unitRectTransform.localRotation = Quaternion.Euler(0, 0, 90);
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
                // Fallback if not in carousel (shouldn't happen but just in case)
                ApplyWeightIconTransformImmediate(slot);
            }
        }

        private static IEnumerator ApplyWeightIconTransformDelayed(GameObject slot)
        {
            // Wait for end of frame to let Unity's layout system finish
            yield return new WaitForEndOfFrame();

            ApplyWeightIconTransformImmediate(slot);

            // Set it again after a small delay to ensure it sticks
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
