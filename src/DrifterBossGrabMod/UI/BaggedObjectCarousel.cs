using UnityEngine;
using RoR2;
using System.Collections.Generic;
using DrifterBossGrabMod.Patches;
using UnityEngine.AddressableAssets;
using TMPro;
using System.Reflection;
using System.IO;

namespace DrifterBossGrabMod.UI
{
    public class BaggedObjectCarousel : MonoBehaviour
    {
        public GameObject slotPrefab; // The Bag UI prefab for each slot
        public float sideScale = 0.8f;

        private static Texture2D? _weightIconTexture;
        private static Texture2D WeightIconTexture => _weightIconTexture ??= LoadWeightIconTexture();

        private static Sprite? _newWeightIconSprite;
        private static Sprite NewWeightIconSprite => _newWeightIconSprite ??= Sprite.Create(WeightIconTexture, new Rect(0, 0, WeightIconTexture.width, WeightIconTexture.height), new Vector2(0.5f, 0.5f));

        private static Sprite? _oldWeightIconSprite;
        private static Sprite OldWeightIconSprite => _oldWeightIconSprite ??= Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/texMovespeedBuffIcon.tif").WaitForCompletion();

        private static Texture2D LoadWeightIconTexture()
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
            if (aboveInstance) ToggleSlotElements(aboveInstance, false);
            if (centerInstance) ToggleSlotElements(centerInstance, true);
            if (belowInstance) ToggleSlotElements(belowInstance, false);
        }

        public void UpdateScales()
        {
            PopulateCarousel(); // Refresh positions and scales
        }

        private GameObject aboveInstance;
        private GameObject centerInstance;
        private GameObject belowInstance;

        private void Start()
        {
            // Find the instances created by the controller
            aboveInstance = transform.Find("aboveSlot")?.gameObject;
            centerInstance = transform.Find("centerSlot")?.gameObject;
            belowInstance = transform.Find("belowSlot")?.gameObject;
            PopulateCarousel();
        }

        public void PopulateCarousel()
        {
            DrifterBagController? bagController = null;
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bc in bagControllers)
            {
                if (BagPatches.baggedObjectsDict.ContainsKey(bc) && BagPatches.baggedObjectsDict[bc].Count > 0)
                {
                    bagController = bc;
                    break;
                }
            }
            if (bagController == null)
            {
                // Hide all slots
                if (aboveInstance) aboveInstance.SetActive(false);
                if (centerInstance) centerInstance.SetActive(false);
                if (belowInstance) belowInstance.SetActive(false);
                return;
            }

            var passengerList = BagPatches.baggedObjectsDict[bagController];
            if (passengerList.Count == 0)
            {
                // Hide all slots
                if (aboveInstance) aboveInstance.SetActive(false);
                if (centerInstance) centerInstance.SetActive(false);
                if (belowInstance) belowInstance.SetActive(false);
                return;
            }

            var mainPassenger = BagPatches.GetMainSeatObject(bagController);
            int currentIndex = -1;
            for (int i = 0; i < passengerList.Count; i++)
            {
                if (passengerList[i] == mainPassenger)
                {
                    currentIndex = i;
                    break;
                }
            }
            if (currentIndex < 0)
            {
                // Main seat is empty
                mainPassenger = null;
                currentIndex = 0; // Dummy for below
            }

            int capacity = BagPatches.GetUtilityMaxStock(bagController);
            float centerY = capacity == 1 ? 0 : PluginConfig.Instance.CarouselCenterOffsetY.Value;
            float centerX = PluginConfig.Instance.CarouselCenterOffsetX.Value;
            float aboveX = centerX + PluginConfig.Instance.CarouselSideOffsetX.Value;
            float aboveY = centerY + PluginConfig.Instance.CarouselSideOffsetY.Value - PluginConfig.Instance.CarouselSpacing.Value;
            float belowX = centerX + PluginConfig.Instance.CarouselSideOffsetX.Value;
            float belowY = centerY + PluginConfig.Instance.CarouselSideOffsetY.Value + PluginConfig.Instance.CarouselSpacing.Value;

            // Center (current)
            if (centerInstance)
            {
                SetSlotData(centerInstance, mainPassenger, bagController);
                SetSlotPosition(centerInstance, centerX, centerY, sideScale, 1f);
                centerInstance.SetActive(mainPassenger != null);
            }

            GameObject abovePassenger = null;
            GameObject belowPassenger = null;

            if (mainPassenger == null)
            {
                // When center is null, show first as above, last as below
                if (passengerList.Count > 0)
                {
                    abovePassenger = passengerList[0];
                    belowPassenger = passengerList[passengerList.Count - 1];
                }
            }
            else
            {
                // Normal logic
                int aboveIndex = currentIndex + 1;
                if (aboveIndex < passengerList.Count)
                {
                    abovePassenger = passengerList[aboveIndex];
                }
                int belowIndex = currentIndex - 1;
                if (belowIndex >= 0)
                {
                    belowPassenger = passengerList[belowIndex];
                }
            }

            if (aboveInstance)
            {
                SetSlotData(aboveInstance, abovePassenger, bagController);
                SetSlotPosition(aboveInstance, aboveX, aboveY, PluginConfig.Instance.CarouselSideScale.Value, PluginConfig.Instance.CarouselSideOpacity.Value);
                aboveInstance.SetActive(abovePassenger != null);
            }

            if (belowInstance)
            {
                SetSlotData(belowInstance, belowPassenger, bagController);
                SetSlotPosition(belowInstance, belowX, belowY, PluginConfig.Instance.CarouselSideScale.Value, PluginConfig.Instance.CarouselSideOpacity.Value);
                belowInstance.SetActive(belowPassenger != null);
            }
        }

        private void SetSlotPosition(GameObject slot, float xOffset, float yOffset, float scale, float opacity)
        {
            StartCoroutine(AnimateSlotPosition(slot, xOffset, yOffset, scale, opacity));
        }

        private System.Collections.IEnumerator AnimateSlotPosition(GameObject slot, float targetXOffset, float targetYOffset, float targetScale, float targetOpacity)
        {
            var rectTransform = slot.GetComponent<RectTransform>();
            var canvasGroup = slot.GetComponent<CanvasGroup>();
            if (!canvasGroup)
            {
                canvasGroup = slot.AddComponent<CanvasGroup>();
            }

            float duration = PluginConfig.Instance.CarouselAnimationDuration.Value;
            float elapsed = 0f;

            Vector2 startPosition = rectTransform ? rectTransform.anchoredPosition : Vector2.zero;
            Vector2 targetPosition = new Vector2(targetXOffset, targetYOffset);
            float startScale = slot.transform.localScale.x; // Assuming uniform scale
            float startOpacity = canvasGroup.alpha;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Ease function (simple ease-out)
                t = 1 - (1 - t) * (1 - t);

                if (rectTransform)
                {
                    rectTransform.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, t);
                }
                slot.transform.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, t);
                canvasGroup.alpha = Mathf.Lerp(startOpacity, targetOpacity, t);

                yield return null;
            }

            // Ensure final values
            if (rectTransform)
            {
                rectTransform.anchoredPosition = targetPosition;
            }
            slot.transform.localScale = Vector3.one * targetScale;
            canvasGroup.alpha = targetOpacity;
        }

        private void SetSlotData(GameObject slot, GameObject passenger, DrifterBagController bagController)
        {
            var baggedCardController = slot.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
                if (passenger != null)
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
                                    weightIconTransform.localPosition = new Vector3(-23f, 1.5f, 0f);
                                    weightIconTransform.localRotation = Quaternion.identity;
                                }
                                else
                                {
                                    if (OldWeightIconSprite)
                                    {
                                        image.sprite = OldWeightIconSprite;
                                    }
                                    weightIconTransform.localPosition = new Vector3(-15.4757f, 0.1f, 0f);
                                    weightIconTransform.localRotation = Quaternion.Euler(0f, 0f, 270f);
                                }

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
                                            tmpRectTransform.localRotation = Quaternion.Euler(0, 0, -90);
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
                else
                {
                    // Empty slot
                    baggedCardController.sourceBody = null;
                    baggedCardController.sourceMaster = null;
                    baggedCardController.sourcePassengerAttributes = null;

                    // Hide weight text if exists
                    var childLocator = slot.GetComponent<ChildLocator>();
                    if (childLocator)
                    {
                        var weightIconTransform = childLocator.FindChild("WeightIcon");
                        if (weightIconTransform)
                        {
                            var tmp = weightIconTransform.GetComponentInChildren<TextMeshProUGUI>();
                            if (tmp) tmp.gameObject.SetActive(false);
                        }
                    }
                }

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
                // Toggle portrait
                if (baggedCardController.portraitIconImage)
                {
                    baggedCardController.portraitIconImage.gameObject.SetActive(isCenter || PluginConfig.Instance.BagUIShowPortrait.Value);
                }

                // Toggle icon
                var layoutElement = baggedCardController.portraitIconImage?.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layoutElement)
                {
                    layoutElement.gameObject.SetActive(isCenter || PluginConfig.Instance.BagUIShowIcon.Value);
                }

                // Toggle weight
                var childLocator = slot.GetComponent<ChildLocator>();
                if (childLocator)
                {
                    var weightIconTransform = childLocator.FindChild("WeightIcon");
                    if (weightIconTransform)
                    {
                        weightIconTransform.gameObject.SetActive(isCenter || PluginConfig.Instance.BagUIShowWeight.Value);
                    }
                }

                // Toggle name
                if (baggedCardController.nameLabel)
                {
                    baggedCardController.nameLabel.gameObject.SetActive(isCenter || PluginConfig.Instance.BagUIShowName.Value);
                }

                // Toggle health bar
                if (baggedCardController.healthBar)
                {
                    baggedCardController.healthBar.gameObject.SetActive(isCenter || PluginConfig.Instance.BagUIShowHealthBar.Value);
                }
            }
        }

    }
}