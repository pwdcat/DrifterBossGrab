#nullable enable
using System;
using UnityEngine;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.UI
{

    // ========================================================================================
    // BAGGED OBJECT INFO UI CONTROLLER
    // ========================================================================================
    public class BaggedObjectInfoUIController : MonoBehaviour
    {
        private GameObject _uiPanel = null!;
        private HGTextMeshProUGUI _statsText = null!;
        private HGTextMeshProUGUI _bagText = null!;
        private HGTextMeshProUGUI _timerText = null!;
        private CanvasGroup _detailsGroup = null!;
        private CanvasGroup _timerGroup = null!;
        private CanvasGroup _summaryGroup = null!;
        private BaggedObjectCarousel? _carousel;
        private GameObject? _lastSelected;
        private readonly Vector3[] _hudCorners = new Vector3[4];
        private CharacterBody _body = null!;
        private DrifterBagController _bagController = null!;
        private HUD? _hud;
        private bool _cachedEnableBaggedObjectInfo;

        private System.Reflection.PropertyInfo? _cachedInputPlayerProperty;
        private System.Reflection.FieldInfo? _cachedInputPlayerField;
        private System.Reflection.MethodInfo? _cachedGetButtonMethod;
        private bool _reflectionCacheInitialized;
        private static readonly object[] _getButtonArgs = new object[] { "info" };

        // ========================================================================================
        // LIFECYCLE
        // ========================================================================================
        private void Start()
        {
            _body = GetComponent<CharacterBody>();
            _bagController = GetComponent<DrifterBagController>();
            _cachedEnableBaggedObjectInfo = PluginConfig.Instance.EnableBaggedObjectInfo.Value;
        }

        private void LateUpdate()
        {
            if (_body == null || _bagController == null) return;

            if (_cachedEnableBaggedObjectInfo != PluginConfig.Instance.EnableBaggedObjectInfo.Value)
            {
                _cachedEnableBaggedObjectInfo = PluginConfig.Instance.EnableBaggedObjectInfo.Value;
                SetUIVisible(false);
                if (!_cachedEnableBaggedObjectInfo) return;
            }

            bool isEnabled = _cachedEnableBaggedObjectInfo || HudEditorManager.IsEditorActive;
            if (!isEnabled)
            {
                SetUIVisible(false);
                return;
            }

            if (_hud != null && _hud.targetBodyObject != _body.gameObject)
            {
                if (_uiPanel != null) Destroy(_uiPanel);
                _uiPanel = null!;
                _carousel = null;
                _lastSelected = null;
                _reflectionCacheInitialized = false;
                _cachedInputPlayerProperty = null;
                _cachedInputPlayerField = null;
                _cachedGetButtonMethod = null;
                _hud = null;
            }

            if (_hud == null)
            {

                foreach (var hud in HUD.readOnlyInstanceList)
                {
                    if (hud && hud.targetBodyObject == _body.gameObject)
                    {
                        _hud = hud;
                        InitializeUI(hud.mainContainer);
                        break;
                    }
                }
            }

            if (_hud != null)
            {
                if (_uiPanel == null) InitializeUI(_hud.mainContainer);
            }

            if (_hud != null && _uiPanel != null)
            {
                bool showInfo = false;
                if (_hud.localUserViewer != null)
                {

                    if (!_reflectionCacheInitialized)
                    {
                        var viewerType = _hud.localUserViewer.GetType();
                        _cachedInputPlayerProperty = viewerType.GetProperty("inputPlayer");
                        if (_cachedInputPlayerProperty == null)
                            _cachedInputPlayerField = viewerType.GetField("inputPlayer");
                        _reflectionCacheInitialized = true;
                    }

                    object? inputPlayer = _cachedInputPlayerProperty?.GetValue(_hud.localUserViewer)
                                       ?? _cachedInputPlayerField?.GetValue(_hud.localUserViewer);

                    if (inputPlayer != null)
                    {
                        _cachedGetButtonMethod ??= inputPlayer.GetType().GetMethod("GetButton", new[] { typeof(string) });
                        if (_cachedGetButtonMethod != null)
                        {
                            showInfo = (bool)_cachedGetButtonMethod.Invoke(inputPlayer, _getButtonArgs);
                        }
                    }
                }

                if (HudEditorManager.IsEditorActive) showInfo = true;

                UpdateStatsDisplay(showInfo);

            }
        }

        // ========================================================================================
        // UI INITIALIZATION
        // ========================================================================================
        private void InitializeUI(GameObject parentContainer)
        {
            if (_uiPanel != null) return;

            _uiPanel = new GameObject("BaggedObjectInfoPanel", typeof(RectTransform));
            _uiPanel.transform.SetParent(parentContainer.transform, false);
            _bagText = CreateStatsText("BagSummary");
            _statsText = CreateStatsText("ObjectDetails");
            _timerText = CreateStatsText("BreakoutTimer");
            _summaryGroup = _bagText.gameObject.AddComponent<CanvasGroup>();
            _summaryGroup.alpha = 0f;
            _detailsGroup = _statsText.gameObject.AddComponent<CanvasGroup>();
            _timerGroup = _timerText.gameObject.AddComponent<CanvasGroup>();
            _detailsGroup.alpha = 0f;
            _timerGroup.alpha = 0f;
            SetUIVisible(false);
        }

        private HGTextMeshProUGUI CreateStatsText(string name)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(_uiPanel.transform, false);
            var text = obj.AddComponent<HGTextMeshProUGUI>();
            text.fontSize = 17f;
            text.alignment = TMPro.TextAlignmentOptions.TopLeft;
            text.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 17f;
            text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            text.richText = true;
            text.raycastTarget = false;
            text.outlineWidth = 0.15f;
            text.outlineColor = new Color32(12, 18, 22, 255);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(420f, 44f);
            return text;
        }

        private static void Place(HGTextMeshProUGUI text, Vector2 position, float scale)
        {
            var rect = (RectTransform)text.transform;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one * scale;
        }

        private void FitRow(HGTextMeshProUGUI text, float scale)
        {
            if (_hud == null || _carousel == null || !(_hud.mainContainer.transform is RectTransform hudRect)) return;
            hudRect.GetWorldCorners(_hudCorners);
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            foreach (var corner in _hudCorners)
            {
                float x = _carousel.transform.InverseTransformPoint(corner).x;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }
            minX += 8f;
            maxX -= 8f;
            var rect = (RectTransform)text.transform;
            float desired = Mathf.Clamp(text.GetPreferredValues(text.text).x + 4f, text == _timerText ? 40f : 160f, 420f);
            float width = Mathf.Min(desired, Mathf.Max(1f, (maxX - minX) / scale));
            float xPosition = Mathf.Clamp(rect.anchoredPosition.x, minX, Mathf.Max(minX, maxX - width * scale));
            rect.anchoredPosition = new Vector2(xPosition, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, 44f);
        }

        // ========================================================================================
        // STATS DISPLAY
        // ========================================================================================
        private void UpdateStatsDisplay(bool showFullStats = true)
        {
            if (_statsText == null || _uiPanel == null) return;

            if (_carousel == null && _hud != null)
                _carousel = _hud.mainContainer.GetComponentInChildren<BaggedObjectCarousel>(true);
            var selectedObject = BagPatches.GetMainSeatObject(_bagController);
            if (_carousel == null || !_carousel.isActiveAndEnabled || !PluginConfig.Instance.EnableCarouselHUD.Value ||
                selectedObject == null || !_carousel.TryGetInspectionLayout(selectedObject, out var slot, out var card, out var bounds) ||
                slot == null || card == null)
            {
                SetUIVisible(false);
                return;
            }
            if (_uiPanel.transform.parent != _carousel.transform)
            {
                _uiPanel.transform.SetParent(_carousel.transform, false);
                var root = (RectTransform)_uiPanel.transform;
                root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = Vector2.zero;
                root.localPosition = Vector3.zero;
            }
            SetUIVisible(true);
            bool changing = DrifterBossGrabPlugin._isSwappingPassengers || _carousel.IsSlotAnimating(slot);
            if (_lastSelected != selectedObject || changing)
            {
                _detailsGroup.alpha = 0f;
                _timerGroup.alpha = 0f;
                _summaryGroup.alpha = 0f;
                _lastSelected = selectedObject;
            }
            float scale = Mathf.Max(0.1f, PluginConfig.Instance.BaggedObjectInfoScale.Value) * slot.transform.localScale.x;
            var bar = card.healthBar != null ? (RectTransform)card.healthBar.transform : (RectTransform)slot.transform;
            var barBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_carousel.transform, bar);
            bool horizontal = PluginConfig.Instance.CarouselOrientation.Value == CarouselOrientation.Horizontal;
            float detailY = barBounds.min.y - 7f * scale;
            bool detailsAbove = horizontal || _carousel.IsInspectionRowObstructed(slot,
                new Rect(barBounds.min.x, detailY - 22f * scale, 420f * scale, 22f * scale));
            if (detailsAbove) detailY = bounds.max.y + 26f * scale;
            float summaryY = bounds.max.y + (detailsAbove ? 50f : 26f) * scale;
            Place(_statsText, new Vector2(barBounds.min.x, detailY), scale);
            Place(_bagText, new Vector2(barBounds.min.x, summaryY), scale);
            Place(_timerText, new Vector2(barBounds.max.x + 7f * scale, barBounds.max.y + 3f * scale), scale);
            _detailsGroup.alpha = Mathf.MoveTowards(_detailsGroup.alpha, showFullStats && !changing ? 1f : 0f, Time.unscaledDeltaTime * 10f);
            _summaryGroup.alpha = Mathf.MoveTowards(_summaryGroup.alpha, showFullStats && !changing ? 1f : 0f, Time.unscaledDeltaTime * 10f);
            bool hasTimer = BreakoutWarningOverlay.TryGetProgress(selectedObject, _bagController, out float progress, out _, out float interval) &&
                !DrifterBagController.bagDisableBreakout;
            _timerGroup.alpha = showFullStats
                ? Mathf.MoveTowards(_timerGroup.alpha, hasTimer && !changing && bar.gameObject.activeInHierarchy ? 1f : 0f, Time.unscaledDeltaTime * 10f)
                : 0f;
            if (hasTimer)
            {
                string timerColor = "#" + ColorCatalog.GetColorHexString(progress >= 0.75f ? ColorCatalog.ColorIndex.Equipment : ColorCatalog.ColorIndex.Tier1Item);
                string timerText = $"<color={timerColor}><b>{Mathf.Max(0f, interval * (1f - progress)):F1}s</b></color>";
                if (_timerText.text != timerText) _timerText.text = timerText;
            }
            FitRow(_timerText, scale);
            _statsText.color = _bagText.color = _timerText.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;

            if (!showFullStats || changing)
            {
                FitRow(_statsText, scale);
                FitRow(_bagText, scale);
                return;
            }

            var aggregateState = StateCalculator.GetAggregateState(_bagController);
            float penalty = aggregateState.movespeedPenalty * 100f;

            int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);
            int slotCapacity = Math.Max(1, BagCapacityCalculator.GetUtilityMaxStock(_bagController));
            bool finiteSlots = slotCapacity < int.MaxValue;
            float massCapacity = PluginConfig.Instance.EnableBalance.Value
                ? CapacityScalingSystem.CalculateMassCapacity(_bagController)
                : float.PositiveInfinity;
            bool finiteMass = !float.IsNaN(massCapacity) && !float.IsInfinity(massCapacity) && massCapacity < float.MaxValue;
            string capacityStr;
            if (finiteMass)
            {
                float mass = BagCapacityCalculator.GetBaggedObjectMass(_bagController);
                capacityStr = $"{mass:0.#}/{massCapacity:0.#} kg";
                if (finiteSlots) capacityStr += $"<space=0.8em>{currentCount}/{slotCapacity}";
            }
            else
            {
                capacityStr = finiteSlots ? $"{currentCount}/{slotCapacity}" : $"{currentCount} bagged";
            }

            var mainSeatObject = selectedObject;

            float damageCoef = SlamDamageCalculator.GetEffectiveCoefficient(_bagController);

            float baseDamage = _body.damage * damageCoef;

            float itemDamageMultiplier = GetItemDamageMultiplier();
            float damageWithItems = baseDamage * itemDamageMultiplier;

            float actualDamage = damageWithItems;
            if (_body.crit >= 100f)
            {
                actualDamage = damageWithItems * _body.critMultiplier;
            }

            float baggedObjectDamage = actualDamage;
            float baggedObjectArmor = 0f;
            if (mainSeatObject != null)
            {
                var baggedBody = mainSeatObject.GetComponent<CharacterBody>();
                var junkCubeController = mainSeatObject.GetComponent<JunkCubeController>();
                var soa = mainSeatObject.GetComponent<SpecialObjectAttributes>();

                if (junkCubeController != null && junkCubeController.ActivationCount > 0)
                {

                    baggedObjectDamage = 1f;
                }

                else if (baggedBody != null)
                {
                    baggedObjectArmor = baggedBody.armor;

                    float damageWithCrowbar = baseDamage * GetItemDamageMultiplier(baggedBody);

                    if (_body.crit >= 100f)
                    {
                        damageWithCrowbar *= _body.critMultiplier;
                    }

                    float armorFactor = baggedObjectArmor >= 0 ? (100f / (100f + baggedObjectArmor)) : (2f - (100f / (100f - baggedObjectArmor)));
                    baggedObjectDamage = damageWithCrowbar * armorFactor;
                }

                else if (soa != null && soa.maxDurability > 0)
                {

                    baggedObjectDamage = 1f;
                }
            }

            string labelColor = "#" + ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier1ItemDark);
            string penaltyColor = "#" + ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Equipment);
            string totalsSection = $"<color={labelColor}><size=85%>Load</size></color> <b>{capacityStr}</b><space=0.8em><color={labelColor}><size=85%>Slam</size></color> <size=110%><b>{actualDamage:F0}</b></size>";
            if (penalty > 0f) totalsSection += $"<space=0.8em><color={penaltyColor}><size=85%>Drag</size> <b>{penalty:F1}%</b></color>";
            if (_bagText.text != totalsSection) _bagText.text = totalsSection;
            if (mainSeatObject is null || mainSeatObject == null)
            {
                _statsText.text = "";
                return;
            }
            var state = StateCalculator.GetIndividualObjectState(_bagController, mainSeatObject);
            if (state == null || state.targetObject == null)
            {
                _statsText.text = "";
                return;
            }
            int junkCount = ImpactJunkPolicy.AllowsReward(mainSeatObject) ? state.junkSpawnCount * 4 : 0;
            string objectSection = $"<b>{junkCount}</b> <color={labelColor}><size=85%>junk</size><space=0.8em><size=85%>Takes</size></color> <b>{baggedObjectDamage:F0}</b> <color={labelColor}><size=85%>/ slam</size></color>";
            if (_statsText.text != objectSection) _statsText.text = objectSection;
            FitRow(_statsText, scale);
            FitRow(_bagText, scale);
        }

        private void SetUIVisible(bool visible)
        {
            if (_uiPanel != null && _uiPanel.activeSelf != visible)
            {
                _uiPanel.SetActive(visible);
                if (!visible)
                {
                    _detailsGroup.alpha = 0f;
                    _timerGroup.alpha = 0f;
                    _summaryGroup.alpha = 0f;
                }
            }
        }

        // ========================================================================================
        // ITEM CALCULATIONS
        // ========================================================================================
        private float GetItemDamageMultiplier(CharacterBody? targetBody = null)
        {
            if (_body == null || _body.inventory == null)
                return 1f;

            float itemDamageMultiplier = 1f;

            int fragileStacks = _body.inventory.GetItemCountEffective(DLC1Content.Items.FragileDamageBonus);
            if (fragileStacks > 0)
            {
                itemDamageMultiplier *= 1f + fragileStacks * 0.2f;
            }

            int nearbyDamageStacks = _body.inventory.GetItemCountEffective(RoR2Content.Items.NearbyDamageBonus);
            if (nearbyDamageStacks > 0)
            {
                itemDamageMultiplier *= 1f + nearbyDamageStacks * 0.2f;
            }

            if (targetBody != null && targetBody.healthComponent != null)
            {
                float targetHealthFraction = targetBody.healthComponent.combinedHealth / targetBody.healthComponent.fullCombinedHealth;
                if (targetHealthFraction >= 0.9f)
                {
                    int crowbarStacks = _body.inventory.GetItemCountEffective(RoR2Content.Items.Crowbar);
                    if (crowbarStacks > 0)
                    {
                        itemDamageMultiplier *= 1f + 0.75f * crowbarStacks;
                    }
                }
            }

            return itemDamageMultiplier;
        }

        private void OnDestroy()
        {
            if (_uiPanel != null)
            {
                Destroy(_uiPanel);
            }
        }
    }
}
