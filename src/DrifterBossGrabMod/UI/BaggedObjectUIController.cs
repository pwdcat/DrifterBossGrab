using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.UI
{
    public class BaggedObjectUIController : MonoBehaviour
    {
        public GameObject carouselPrefab;
        public GameObject slotPrefab; // The Bag UI prefab
        private GameObject carouselInstance;
        private GameObject aboveInstance;
        private GameObject centerInstance;
        private GameObject belowInstance;

        private void Start()
        {
            Debug.Log($"[BaggedObjectUIController] Start: slotPrefab={slotPrefab}");
            if (slotPrefab)
            {
                var hud = RoR2.UI.HUD.readOnlyInstanceList.Count > 0 ? RoR2.UI.HUD.readOnlyInstanceList[0] : null;
                Debug.Log($"[BaggedObjectUIController] HUD instance: {hud}");
                if (hud && hud.mainContainer)
                {
                    // Find the DisplayRoot transform in the HUD hierarchy
                    var displayRoot = FindDeepChild(hud.mainContainer.transform, "DisplayRoot");
                    Debug.Log($"[BaggedObjectUIController] displayRoot: {displayRoot}");
                    if (displayRoot)
                    {
                        // Create the carousel GameObject at runtime
                        carouselInstance = new GameObject("BaggedObjectCarousel");
                        carouselInstance.transform.SetParent(displayRoot, false);
                        carouselInstance.AddComponent<UnityEngine.RectTransform>();
                        var carousel = carouselInstance.AddComponent<BaggedObjectCarousel>();
                        carousel.slotPrefab = slotPrefab;
                        carousel.sideScale = PluginConfig.Instance.BagUIScale.Value;

                        // Instantiate the slot instances directly
                        aboveInstance = Instantiate(slotPrefab, carouselInstance.transform);
                        aboveInstance.name = "aboveSlot";
                        aboveInstance.GetComponent<UnityEngine.RectTransform>().anchoredPosition = new Vector2(0, -PluginConfig.Instance.CarouselSpacing.Value);
                        aboveInstance.SetActive(false);

                        centerInstance = Instantiate(slotPrefab, carouselInstance.transform);
                        centerInstance.name = "centerSlot";
                        centerInstance.GetComponent<UnityEngine.RectTransform>().anchoredPosition = new Vector2(0, 0);
                        centerInstance.SetActive(false);

                        belowInstance = Instantiate(slotPrefab, carouselInstance.transform);
                        belowInstance.name = "belowSlot";
                        belowInstance.GetComponent<UnityEngine.RectTransform>().anchoredPosition = new Vector2(0, PluginConfig.Instance.CarouselSpacing.Value);
                        belowInstance.SetActive(false);

                        // Set weight icon positions
                        SetWeightIconPosition(aboveInstance);
                        SetWeightIconPosition(centerInstance);
                        SetWeightIconPosition(belowInstance);

                        Debug.Log($"[BaggedObjectUIController] Created carousel in HUD");
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (carouselInstance)
            {
                Destroy(carouselInstance);
            }
        }

        public void ToggleBagUIElements(GameObject bagUI)
        {
            var baggedCardController = bagUI.GetComponentInChildren<RoR2.UI.BaggedCardController>();
            if (baggedCardController)
            {
                // Toggle portrait (assuming portraitIconImage is the portrait)
                if (baggedCardController.portraitIconImage)
                {
                    baggedCardController.portraitIconImage.gameObject.SetActive(PluginConfig.Instance.BagUIShowPortrait.Value);
                }

                // Toggle icon (LayoutElement on portrait?)
                var layoutElement = baggedCardController.portraitIconImage?.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layoutElement)
                {
                    layoutElement.gameObject.SetActive(PluginConfig.Instance.BagUIShowIcon.Value);
                }

                // Toggle weight icon
                var childLocator = bagUI.GetComponent<ChildLocator>();
                if (childLocator)
                {
                    var weightIconTransform = childLocator.FindChild("WeightIcon");
                    if (weightIconTransform)
                    {
                        weightIconTransform.gameObject.SetActive(PluginConfig.Instance.BagUIShowWeight.Value);
                    }
                }

                // Toggle name label
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

        private Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                var result = FindDeepChild(child, name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void SetWeightIconPosition(GameObject slot)
        {
            var childLocator = slot.GetComponent<ChildLocator>();
            if (childLocator)
            {
                var weightIconTransform = childLocator.FindChild("WeightIcon");
                if (weightIconTransform)
                {
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