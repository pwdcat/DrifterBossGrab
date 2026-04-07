#nullable enable
using UnityEngine;
using RoR2;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod.UI
{
    public class BaggedObjectUIController : MonoBehaviour
    {
        public GameObject? carouselPrefab;
        public GameObject? slotPrefab; // The Bag UI prefab
        private GameObject? carouselInstance;
        private GameObject? aboveInstance;
        private GameObject? centerInstance;
        private GameObject? belowInstance;

        private void Start()
        {
            if (slotPrefab)
            {
                // Check if this body is Drifter
                var body = GetComponent<CharacterBody>();
                if (body == null || !body!.name.StartsWith("DrifterBody") || !body.hasAuthority)
                {
                    return;
                }
                var localUser = RoR2.LocalUserManager.GetFirstLocalUser();
                if (localUser == null)
                {
                    return;
                }
                var hud = localUser.cameraRigController?.hud;
                if (hud && hud!.mainContainer)
                {
                    // Find the DisplayRoot transform in the HUD hierarchy
                    var safeHud = hud!;
                    var displayRoot = FindDeepChild(safeHud.mainContainer!.transform, "DisplayRoot");
                    if (displayRoot)
                    {
                        // Create the carousel GameObject at runtime
                        carouselInstance = new GameObject("BaggedObjectCarousel");
                        carouselInstance.transform.SetParent(displayRoot, false);
                        carouselInstance.AddComponent<UnityEngine.RectTransform>();
                        var carousel = carouselInstance.AddComponent<BaggedObjectCarousel>();
                        carousel.slotPrefab = slotPrefab;

                        // Instantiate the slot instances directly
                        aboveInstance = Instantiate(slotPrefab, carouselInstance.transform);
                        aboveInstance!.name = "aboveSlot";
                        aboveInstance!.GetComponent<UnityEngine.RectTransform>().anchoredPosition = new Vector2(0, -PluginConfig.Instance.CarouselSpacing.Value);
                        aboveInstance.SetActive(false);

                        centerInstance = Instantiate(slotPrefab, carouselInstance.transform);
                        centerInstance!.name = "centerSlot";
                        centerInstance!.GetComponent<UnityEngine.RectTransform>().anchoredPosition = new Vector2(0, 0);
                        centerInstance.SetActive(false);

                        belowInstance = Instantiate(slotPrefab, carouselInstance.transform);
                        belowInstance!.name = "belowSlot";
                        belowInstance!.GetComponent<UnityEngine.RectTransform>().anchoredPosition = new Vector2(0, PluginConfig.Instance.CarouselSpacing.Value);
                        belowInstance.SetActive(false);

                        // Set weight icon positions
                        BaggedObjectCarousel.ApplyWeightIconTransform(aboveInstance!);
                        BaggedObjectCarousel.ApplyWeightIconTransform(centerInstance!);
                        BaggedObjectCarousel.ApplyWeightIconTransform(belowInstance!);
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
                    baggedCardController.portraitIconImage.gameObject.SetActive(true);
                }

                // Toggle icon (LayoutElement on portrait?)
                var layoutElement = baggedCardController.portraitIconImage?.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layoutElement)
                {
                    layoutElement!.gameObject.SetActive(PluginConfig.Instance.CenterSlotShowIcon.Value);
                }

                // Toggle weight icon
                var childLocator = bagUI.GetComponent<ChildLocator>();
                if (childLocator)
                {
                    var weightIconTransform = childLocator.FindChild("WeightIcon");
                    if (weightIconTransform)
                    {
                        weightIconTransform.gameObject.SetActive(PluginConfig.Instance.CenterSlotShowWeightIcon.Value);
                    }
                }

                // Toggle name label
                if (baggedCardController.nameLabel)
                {
                    baggedCardController.nameLabel.gameObject.SetActive(PluginConfig.Instance.CenterSlotShowName.Value);
                }

                // Toggle health bar
                if (baggedCardController.healthBar)
                {
                    baggedCardController.healthBar.gameObject.SetActive(PluginConfig.Instance.CenterSlotShowHealthBar.Value);
                }
            }
        }

        private Transform? FindDeepChild(Transform parent, string name)
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
    }
}
