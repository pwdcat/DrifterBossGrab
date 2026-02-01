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

namespace DrifterBossGrabMod.UI
{
    public class BaggedObjectCarousel : MonoBehaviour
    {
        public GameObject? slotPrefab; // The Bag UI prefab for each slot
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
            using (var stream = assembly.GetManifestResourceStream("DrifterBossGrabMod.weighticon.png"))
            {
                if (stream == null)
                {
                    Debug.LogError("Could not find embedded resource: DrifterBossGrabMod.weighticon.png");
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

        // Modern 5-slot carousel management
        private List<GameObject> _slots = new();
        private Dictionary<GameObject, GameObject?> _slotToPassenger = new();

        // Sentinel for the "empty" state in the carousel cycle
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

            // Add extra slots for exit transitions if we have a template
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
            DrifterBagController? bagController = null;
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bc in bagControllers)
            {
                // Prioritize controller with authority (local player)
                if (bc.hasAuthority)
                {
                    bagController = bc;
                    break;
                }
            }

            if (bagController == null)
            {
                foreach (var s in _slots) s.SetActive(false);
                _slotToPassenger.Clear();
                return;
            }

            List<GameObject> passengerList = new List<GameObject>();
            GameObject? mainPassenger = null;

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            // Prioritize local knowledge if we have authority (local player)
            if (bagController.hasAuthority && BagPatches.baggedObjectsDict.TryGetValue(bagController, out var localList))
            {
                passengerList = localList;
                mainPassenger = BagPatches.GetMainSeatObject(bagController);
            }
            else if (netController != null && (!NetworkServer.active || !BagPatches.baggedObjectsDict.ContainsKey(bagController)))
            {
                // Use networked state for other players or as fallback
                passengerList = netController.GetBaggedObjects();
                int selectedIdx = netController.selectedIndex;
                if (selectedIdx >= 0 && selectedIdx < passengerList.Count)
                {
                    mainPassenger = passengerList[selectedIdx];
                }
            }
            else if (BagPatches.baggedObjectsDict.TryGetValue(bagController, out var fallbackList))
            {
                // Use local state on host/server for NPCs or if somehow we missed authority
                passengerList = fallbackList;
                mainPassenger = BagPatches.GetMainSeatObject(bagController);
            }

            if (passengerList.Count == 0 && mainPassenger == null)
            {
                foreach (var s in _slots) s.SetActive(false);
                _slotToPassenger.Clear();
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
            int capacity = BagPatches.GetUtilityMaxStock(bagController);
            bool isBagFull = passengerList.Count >= capacity;
            
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

            float sideScaleVal = PluginConfig.Instance.CarouselSideScale.Value;
            float sideOpacityVal = PluginConfig.Instance.CarouselSideOpacity.Value;

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
                    AnimateToState(slot, newState, capacity, bagController);
                    usedSlots.Add(slot);
                    foundPassengers.Add(passenger);
                }
                else
                {
                    // No longer in window or redundant - Animate to exit state
                    int exitState = (direction > 0) ? -2 : 2; // Move down if next, up if prev
                    if (direction == 0) exitState = -2; // Default
                    
                    AnimateToState(slot, exitState, capacity, bagController, true); // Hide after
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
                GameObject? freeSlot = _slots.FirstOrDefault(s => !usedSlots.Contains(s) && !_slotToPassenger.ContainsKey(s));
                if (freeSlot == null) freeSlot = _slots.FirstOrDefault(s => !usedSlots.Contains(s)); // Steal an exit slot if needed

                if (freeSlot)
                {
                    _slotToPassenger[freeSlot] = targetP;
                    SetSlotData(freeSlot, targetP, bagController);
                    
                    // Set initial position based on where it's coming from
                    int startState = (direction > 0) ? state + 1 : state - 1;
                    if (direction == 0) startState = state; // Snap if no direction

                    var startParams = GetStateParams(startState, capacity);
                    SetSlotInitialState(freeSlot, startParams.pos.x, startParams.pos.y, startParams.scale, 0f);
                    freeSlot.SetActive(true);

                    AnimateToState(freeSlot, state, capacity, bagController);
                    usedSlots.Add(freeSlot);
                    foundPassengers.Add(targetP);
                }
            }

            // 3. Update compatibility references (for UpdateToggles)
            centerInstance = _slots.FirstOrDefault(s => _slotToPassenger.TryGetValue(s, out var p) && p == targetPassengers[0]);
            aboveInstance = _slots.FirstOrDefault(s => _slotToPassenger.TryGetValue(s, out var p) && p == targetPassengers[1]);
            belowInstance = _slots.FirstOrDefault(s => _slotToPassenger.TryGetValue(s, out var p) && p == targetPassengers[-1]);

            // Ensure Center is on top
            if (centerInstance) centerInstance.transform.SetAsLastSibling();
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
            float centerY = capacity == 1 ? 0 : PluginConfig.Instance.CarouselCenterOffsetY.Value;
            float centerX = PluginConfig.Instance.CarouselCenterOffsetX.Value;
            float sideX = PluginConfig.Instance.CarouselSideOffsetX.Value;
            float sideY = PluginConfig.Instance.CarouselSideOffsetY.Value;
            float spacing = PluginConfig.Instance.CarouselSpacing.Value;

            float scale = (state == 0) ? 1.0f : PluginConfig.Instance.CarouselSideScale.Value;
            float opacity = (state == 0) ? 1.0f : PluginConfig.Instance.CarouselSideOpacity.Value;

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

        private void SetSlotData(GameObject slot, GameObject? passenger, DrifterBagController bagController)
        {
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
                var canvasGroup = slot.GetComponent<CanvasGroup>();
                if (canvasGroup) canvasGroup.alpha = 1f;

                // Check for empty slot marker first
                if (passenger == EmptySlotMarker || passenger == null)
                {
                    // Empty slot state - fully invisible
                    baggedCardController.sourceBody = null;
                    baggedCardController.sourceMaster = null;
                    baggedCardController.sourcePassengerAttributes = null;

                    if (baggedCardController.nameLabel) baggedCardController.nameLabel.gameObject.SetActive(false);
                    if (baggedCardController.portraitIconImage) baggedCardController.portraitIconImage.gameObject.SetActive(false);
                    if (baggedCardController.healthBar) baggedCardController.healthBar.gameObject.SetActive(false);

                    if (canvasGroup) canvasGroup.alpha = 0f;

                    var childLocator = slot.GetComponent<ChildLocator>();
                    if (childLocator)
                    {
                        var weightIconTransform = childLocator.FindChild("WeightIcon");
                        if (weightIconTransform)
                        {
                            weightIconTransform.gameObject.SetActive(false);
                            var tmp = weightIconTransform.GetComponentInChildren<TextMeshProUGUI>();
                            if (tmp) tmp.gameObject.SetActive(false);
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

                    // Set weight icon
                    float mass = (bagController == passenger) ? bagController.baggedMass : bagController.CalculateBaggedObjectMass(passenger);
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
                                    UnityEngine.Color color;
                                    if (mass <= 100f)
                                    {
                                        float num = mass / 100f;
                                        color = new UnityEngine.Color(1f - num, 1f, 1f - num);
                                    }
                                    else if (mass <= 350f)
                                    {
                                        float r = (mass - 100f) / 250f;
                                        color = new UnityEngine.Color(r, 1f, 0f);
                                    }
                                    else
                                    {
                                        float num2 = (mass - 350f) / 350f;
                                        color = new UnityEngine.Color(1f, 1f - num2, 0f);
                                    }
                                    image.color = color;
                                }
                                else
                                {
                                    image.color = Color.white;
                                }

                                // Set text
                                if (PluginConfig.Instance.ShowWeightText.Value)
                                {
                                    var tmp = weightIconTransform.GetComponentInChildren<TextMeshProUGUI>();
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
                                            tmpRectTransform.anchoredPosition = new Vector2(-0.29f, 2.4112f);
                                            tmp.verticalAlignment = VerticalAlignmentOptions.Bottom;
                                            tmp.fontSize = 8.5f;
                                            tmp.characterSpacing = 0.5f;
                                            tmpRectTransform.localRotation = Quaternion.identity;
                                        }
                                        else
                                        {
                                            tmpRectTransform.anchoredPosition = Vector2.zero;
                                            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
                                            tmp.fontSize = 12f;
                                            tmp.characterSpacing = 0f;
                                            tmpRectTransform.localRotation = Quaternion.Euler(0, 0, 90);
                                        }
                                    }
                                    int multiplier = Mathf.CeilToInt(mass / 100f);
                                    tmp.text = multiplier + "x";
                                    tmp.gameObject.SetActive(true);
                                }
                                else
                                {
                                    var tmp = weightIconTransform.GetComponentInChildren<TextMeshProUGUI>();
                                    if (tmp) tmp.gameObject.SetActive(false);
                                }
                            }
                        }
                    }
                }
                // Normal passenger logic continues below... (removed redundant else)

                // Apply toggles
                bool isCenter = slot == centerInstance;
                ToggleSlotElements(slot, isCenter);
            }
        }

        private void ToggleSlotElements(GameObject slot, bool isCenter)
        {
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
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
                    layoutElement.gameObject.SetActive(PluginConfig.Instance.BagUIShowIcon.Value);
                }

                // Toggle weight
                var childLocator = slot.GetComponent<ChildLocator>();
                if (childLocator)
                {
                    var weightIconTransform = childLocator.FindChild("WeightIcon");
                    if (weightIconTransform)
                    {
                        weightIconTransform.gameObject.SetActive(PluginConfig.Instance.BagUIShowWeight.Value);
                    }
                }

                // Toggle name
                if (baggedCardController.nameLabel)
                {
                    baggedCardController.nameLabel.gameObject.SetActive(PluginConfig.Instance.BagUIShowName.Value);
                }

                // Toggle health bar
                if (baggedCardController.healthBar)
                {
                    baggedCardController.healthBar.gameObject.SetActive(PluginConfig.Instance.BagUIShowHealthBar.Value);
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
    }
}