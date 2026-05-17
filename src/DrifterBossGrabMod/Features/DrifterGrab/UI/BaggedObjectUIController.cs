#nullable enable
using UnityEngine;
using RoR2;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod.UI
{
    public class BaggedObjectUIController : MonoBehaviour
    {
        public GameObject? carouselPrefab;
        public GameObject? slotPrefab;
        private GameObject? carouselInstance;
        private GameObject? aboveInstance;
        private GameObject? centerInstance;
        private GameObject? belowInstance;

        private void Start()
        {
            if (slotPrefab)
            {

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

                    var safeHud = hud!;
                    var displayRoot = FindDeepChild(safeHud.mainContainer!.transform, "DisplayRoot");
                    if (displayRoot)
                    {

                        carouselInstance = new GameObject("BaggedObjectCarousel");
                        carouselInstance.transform.SetParent(displayRoot, false);
                        var rect = carouselInstance.AddComponent<UnityEngine.RectTransform>();
                        var carousel = carouselInstance.AddComponent<BaggedObjectCarousel>();
                        carousel.slotPrefab = slotPrefab;

                        var draggable = carouselInstance.AddComponent<HudDraggable>();
                        draggable.ElementType = HudElementType.MainSlot;
                        draggable.DragSizePadding = new Vector2(75, 50);
                        draggable.DragOffset = new Vector2(-40, -20);
                        draggable.XConfig = PluginConfig.Instance.CenterSlotX;
                        draggable.YConfig = PluginConfig.Instance.CenterSlotY;
                        draggable.ScaleConfig = PluginConfig.Instance.CenterSlotScale;

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
