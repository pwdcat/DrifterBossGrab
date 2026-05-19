#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using RiskOfOptions;
using RiskOfOptions.Options;
using System.Reflection;
using System;
using RoR2.UI;
using HarmonyLib;
using RiskOfOptions.Components.Panel;

namespace DrifterBossGrabMod.UI
{
    [HarmonyPatch]
    internal static class RiskOfOptionsDummyPatches
    {
        [HarmonyPatch(typeof(ModOptionPanelController), nameof(ModOptionPanelController.OptionChanged))]
        [HarmonyPatch(typeof(ModOptionPanelController), "CheckIfRestartNeeded")]
        [HarmonyPatch(typeof(ModOptionPanelController), "ShowRestartWarning")]
        [HarmonyPatch(typeof(ModOptionPanelController), "HideRestartWarning")]
        [HarmonyPrefix]
        internal static bool Prefix(ModOptionPanelController __instance)
        {
            if (__instance != null && __instance.gameObject != null && __instance.gameObject.name == "DrifterHUD_DummyController")
            {
                return false;
            }
            return true;
        }
    }

    public class HudContextMenu : MonoBehaviour
    {
        private static GameObject? _menuInstance;

        public static void Show(HudDraggable draggable)
        {
            if (_menuInstance != null)
            {
                Destroy(_menuInstance);
            }

            var canvas = draggable.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            if (PluginConfig.Instance.SelectedHudElement != null)
            {
                PluginConfig.Instance.SelectedHudElement.Value = draggable.ElementType;
            }

            _menuInstance = new GameObject("HudContextMenu");
            _menuInstance.AddComponent<HudContextMenu>();
            _menuInstance.transform.SetParent(canvas.transform, false);

            var rect = _menuInstance.AddComponent<RectTransform>();

            float pivotX = 0f;
            float pivotY = 1f;

            if (UnityEngine.Input.mousePosition.x > UnityEngine.Screen.width * 0.5f)
            {
                pivotX = 1f;
            }
            if (UnityEngine.Input.mousePosition.y < UnityEngine.Screen.height * 0.5f)
            {
                pivotY = 0f;
            }

            rect.pivot = new Vector2(pivotX, pivotY);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                UnityEngine.Input.mousePosition,
                canvas.worldCamera,
                out Vector2 localPoint);

            float menuWidth = 516f;
            float menuHeight = 348f;
            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null)
            {
                float canvasWidth = canvasRect.rect.width;
                float canvasHeight = canvasRect.rect.height;

                float minX, maxX;
                if (pivotX == 0f)
                {
                    minX = -canvasWidth * 0.5f;
                    maxX = canvasWidth * 0.5f - menuWidth;
                }
                else
                {
                    minX = -canvasWidth * 0.5f + menuWidth;
                    maxX = canvasWidth * 0.5f;
                }

                float minY, maxY;
                if (pivotY == 0f)
                {
                    minY = -canvasHeight * 0.5f;
                    maxY = canvasHeight * 0.5f - menuHeight;
                }
                else
                {
                    minY = -canvasHeight * 0.5f + menuHeight;
                    maxY = canvasHeight * 0.5f;
                }

                if (minX > maxX)
                {
                    float temp = minX;
                    minX = maxX;
                    maxX = temp;
                }
                if (minY > maxY)
                {
                    float temp = minY;
                    minY = maxY;
                    maxY = temp;
                }

                localPoint.x = Mathf.Clamp(localPoint.x, minX, maxX);
                localPoint.y = Mathf.Clamp(localPoint.y, minY, maxY);
            }

            rect.anchoredPosition = localPoint;

            var bg = _menuInstance.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.98f);
            var mainLayout = _menuInstance.AddComponent<VerticalLayoutGroup>();
            mainLayout.padding = new RectOffset(8, 8, 8, 8);
            mainLayout.spacing = 2;
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;
            mainLayout.childForceExpandWidth = true;
            mainLayout.childForceExpandHeight = false;

            var fitter = _menuInstance.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string title = draggable.ElementType.ToString();
            if (draggable.ElementType == HudElementType.MainSlot || draggable.ElementType == HudElementType.SideSlots)
            {
                title = "Carousel";
            }
            AddHeader(title);

            var scrollViewObj = new GameObject("ScrollView");
            scrollViewObj.transform.SetParent(_menuInstance.transform, false);
            var scrollViewRect = scrollViewObj.AddComponent<RectTransform>();
            var scrollLayoutElement = scrollViewObj.AddComponent<LayoutElement>();
            scrollLayoutElement.preferredHeight = 300;
            scrollLayoutElement.preferredWidth = 500;

            var scrollRect = scrollViewObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 20f;

            var viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            var viewportRect = viewportObj.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.pivot = new Vector2(0, 1);

            var viewportImage = viewportObj.AddComponent<Image>();
            viewportImage.color = new Color(1, 1, 1, 0.01f);
            var mask = viewportObj.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);
            var contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.sizeDelta = new Vector2(0, 0);

            var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(10, 10, 5, 5);
            contentLayout.spacing = 8;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            PopulateOptions(draggable.ElementType, contentObj.transform);

            _menuInstance.transform.SetAsLastSibling();
        }

        private static void AddHeader(string titleText)
        {
            if (_menuInstance == null) return;

            var headerObj = new GameObject("Header");
            headerObj.transform.SetParent(_menuInstance.transform, false);

            var layout = headerObj.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(20, 5, 5, 5);

            headerObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textObj = new GameObject("Title");
            textObj.transform.SetParent(headerObj.transform, false);
            var text = textObj.AddComponent<HGTextMeshProUGUI>();
            text.text = $"<b>{titleText} Config</b>";
            text.fontSize = 20;
            text.color = new Color(0.9f, 0.8f, 0.5f);
        }

        private static void PopulateOptions(HudElementType elementType, Transform contentParent)
        {
            if (_menuInstance == null) return;

            try
            {
                var optionCollectionField = typeof(ModSettingsManager).GetField("OptionCollection", BindingFlags.Static | BindingFlags.NonPublic);
                if (optionCollectionField == null) return;

                var optionCollection = optionCollectionField.GetValue(null);
                if (optionCollection == null) return;

                var getOptionMethod = optionCollection.GetType().GetMethod("GetOption", BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
                if (getOptionMethod == null)
                {
                    Debug.LogError("[HudContextMenu] Could not find GetOption method on OptionCollection.");
                    return;
                }

                var dummyContainer = new GameObject("DummyControllerContainer");
                dummyContainer.transform.SetParent(_menuInstance.transform, false);
                dummyContainer.SetActive(false);

                _menuInstance.AddComponent<RoR2.UI.MPEventSystemLocator>();
                var dummyController = dummyContainer.AddComponent<RiskOfOptions.Components.Panel.ModOptionPanelController>();

                foreach (var kvp in PluginConfig.HudSettingToSubTab)
                {
                    bool match = false;
                    foreach (var type in kvp.Value)
                    {
                        if (type == elementType || elementType == HudElementType.All)
                        {
                            match = true;
                            break;
                        }

                        bool isCarouselElement = elementType == HudElementType.MainSlot || elementType == HudElementType.SideSlots;
                        bool isRelatedType = type == HudElementType.MainSlot || type == HudElementType.SideSlots ||
                                           type == HudElementType.DamagePreview || type == HudElementType.WeightIcon;

                        if (isCarouselElement && isRelatedType)
                        {
                            match = true;
                            break;
                        }
                    }

                    if (match)
                    {
                        string identifier = kvp.Key;
                        if (identifier.Contains("ENABLE_HUD_EDITOR") || identifier.Contains("HUD_FILTER")) continue;

                        try
                        {
                            var baseOption = getOptionMethod.Invoke(optionCollection, new object[] { identifier }) as BaseOption;

                            if (baseOption != null)
                            {
                                GameObject? basePrefab = GetPrefabForIdentifier(identifier);
                                if (basePrefab != null)
                                {
                                    GameObject dummyParent = new GameObject("Dummy");
                                    dummyParent.SetActive(false);

                                    GameObject inactivePrefab = Instantiate(basePrefab, dummyParent.transform);
                                    inactivePrefab.SetActive(false);

                                    GameObject optionObj = baseOption.CreateOptionGameObject(inactivePrefab, contentParent);

                                    Destroy(dummyParent);

                                    if (optionObj != null)
                                    {
                                        optionObj.transform.localScale = Vector3.one;
                                        optionObj.transform.localPosition = Vector3.zero;

                                        var layout = optionObj.GetComponent<LayoutElement>();
                                        if (layout == null) layout = optionObj.AddComponent<LayoutElement>();
                                        layout.minHeight = 45;
                                        layout.preferredHeight = 45;
                                        layout.flexibleHeight = 0;

                                        var modSetting = optionObj.GetComponentInChildren<RiskOfOptions.Components.Options.ModSetting>(true);
                                        if (modSetting != null)
                                        {
                                            modSetting.optionController = dummyController;
                                        }

                                        optionObj.SetActive(true);

                                        if (dummyController.gameObject.name != "DrifterHUD_DummyController")
                                        {
                                            dummyController.gameObject.name = "DrifterHUD_DummyController";
                                        }

                                        if (modSetting != null)
                                        {
                                            var modBool = modSetting as RiskOfOptions.Components.Options.ModSettingsBool;
                                            if (modBool != null)
                                            {

                                                if (modBool.checkBoxTrue == null || modBool.checkBoxFalse == null)
                                                {
                                                    var otherBools = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSettingsBool>(UnityEngine.FindObjectsSortMode.None);
                                                    foreach (var other in otherBools)
                                                    {
                                                        if (other != modBool && other.checkBoxTrue != null && other.checkBoxFalse != null)
                                                        {
                                                            modBool.checkBoxTrue = other.checkBoxTrue;
                                                            modBool.checkBoxFalse = other.checkBoxFalse;
                                                            break;
                                                        }
                                                    }

                                                    if (modBool.checkBoxTrue == null)
                                                    {
                                                        var allObjects = UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>();
                                                        foreach (var obj in allObjects)
                                                        {
                                                            if (obj.name == "SettingsEntryButton, Bool (Audio Focus)")
                                                            {
                                                                var carousel = obj.GetComponentInChildren<RoR2.UI.CarouselController>();
                                                                if (carousel != null && carousel.choices.Length >= 2)
                                                                {
                                                                    modBool.checkBoxFalse = carousel.choices[0].customSprite;
                                                                    modBool.checkBoxTrue = carousel.choices[1].customSprite;
                                                                    break;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }

                                                if (modBool.checkBox != null)
                                                {
                                                    modBool.checkBox.sprite = modBool.IsChecked ? modBool.checkBoxTrue : modBool.checkBoxFalse;
                                                    modBool.checkBox.color = Color.white;
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"[HudContextMenu] Prefab is null for identifier: {identifier}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[HudContextMenu] Failed to load option for {identifier}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HudContextMenu] Error populating options: {ex}");
            }
        }

        private static GameObject? GetPrefabForIdentifier(string identifier)
        {
            if (identifier.EndsWith(".CHECKBOX")) return RiskOfOptions.Resources.Prefabs.boolButton;
            if (identifier.EndsWith(".COLOR")) return RiskOfOptions.Resources.Prefabs.colorPickerButton;
            if (identifier.EndsWith(".FLOAT_FIELD")) return RiskOfOptions.Resources.Prefabs.floatFieldButton;
            if (identifier.EndsWith(".STEP_SLIDER")) return RiskOfOptions.Resources.Prefabs.stepSliderButton;
            if (identifier.EndsWith(".INT_SLIDER")) return RiskOfOptions.Resources.Prefabs.intSliderButton;
            if (identifier.EndsWith(".CHOICE"))
            {
                return RiskOfOptions.Components.RuntimePrefabs.RuntimePrefabManager.Get<RiskOfOptions.Components.RuntimePrefabs.ChoicePrefab>().ChoiceButton;
            }
            return null;
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Destroy(gameObject);
                return;
            }

            if (UnityEngine.Input.GetMouseButtonDown(0) || UnityEngine.Input.GetMouseButtonDown(1))
            {
                var rect = GetComponent<RectTransform>();
                if (rect != null)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rect, UnityEngine.Input.mousePosition))
                    {
                        var hoveredEventData = new PointerEventData(EventSystem.current) { position = UnityEngine.Input.mousePosition };
                        var results = new List<RaycastResult>();
                        EventSystem.current.RaycastAll(hoveredEventData, results);

                        bool clickedInside = false;
                        foreach (var result in results)
                        {
                            if (result.gameObject.transform.IsChildOf(transform) || result.gameObject.name.Contains("Color"))
                            {
                                clickedInside = true;
                                break;
                            }
                        }

                        if (!clickedInside)
                        {
                            Destroy(gameObject);
                        }
                    }
                }
            }
        }
    }
}
