#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
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

        private void Update()
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
                SetUIVisible(false);
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

                if (showInfo)
                {

                    UpdateStatsDisplay(showFullStats: true);
                    SetUIVisible(true);
                }
                else
                {
                    SetUIVisible(false);
                }
            }
        }

        // ========================================================================================
        // UI INITIALIZATION
        // ========================================================================================
        private void InitializeUI(GameObject parentContainer)
        {
            if (_uiPanel != null) return;

            _uiPanel = new GameObject("BaggedObjectInfoPanel");
            _uiPanel.transform.SetParent(parentContainer.transform, false);

            var rectTransform = _uiPanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 0.5f);
            rectTransform.anchorMax = new Vector2(0, 0.5f);
            rectTransform.pivot = new Vector2(0, 0.5f);
            var layoutGroup = _uiPanel.AddComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;

            var fitter = _uiPanel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ApplyConfigValues(rectTransform);

            var textObj = new GameObject("StatsText");
            textObj.transform.SetParent(_uiPanel.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();

            _statsText = textObj.AddComponent<HGTextMeshProUGUI>();
            _statsText.fontSize = 20;
            _statsText.alignment = TMPro.TextAlignmentOptions.TopLeft;
            _statsText.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;
            _statsText.enableWordWrapping = true;
            _statsText.richText = true;

            var draggable = _uiPanel.AddComponent<HudDraggable>();
            draggable.ElementType = HudElementType.StatsPanel;
            draggable.XConfig = PluginConfig.Instance.BaggedObjectInfoX;
            draggable.YConfig = PluginConfig.Instance.BaggedObjectInfoY;
            draggable.ScaleConfig = PluginConfig.Instance.BaggedObjectInfoScale;

            SetUIVisible(false);
        }

        private void ApplyConfigValues(RectTransform rectTransform)
        {
            rectTransform.anchoredPosition = new Vector2(PluginConfig.Instance.BaggedObjectInfoX.Value, PluginConfig.Instance.BaggedObjectInfoY.Value);
            rectTransform.localScale = Vector3.one * PluginConfig.Instance.BaggedObjectInfoScale.Value;
        }

        // ========================================================================================
        // STATS DISPLAY
        // ========================================================================================
        private void UpdateStatsDisplay(bool showFullStats = true)
        {
            if (_statsText == null || _uiPanel == null) return;

            if (DrifterBossGrabPlugin._isSwappingPassengers) return;

            if (_uiPanel.transform is RectTransform rect)
            {
                ApplyConfigValues(rect);
                _statsText.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;
            }

            var aggregateState = StateCalculator.GetAggregateState(_bagController);
            float totalMass = aggregateState.baggedMass;
            float penalty = aggregateState.movespeedPenalty * 100f;

            float massCapacity = CapacityScalingSystem.CalculateMassCapacity(_bagController);

            bool useSlotBasedDisplay = CapacityScalingSystem.IsMassCapacityUnlimited(_bagController);

            bool isBottomlessBag = PluginConfig.Instance.BottomlessBagEnabled.Value &&
                PluginConfig.Instance.IsAddedCapacityInfinite;

            string capacityStr;
            if (useSlotBasedDisplay)
            {
                int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);
                int slotCapacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);

                if (isBottomlessBag)
                {
                    capacityStr = $"{currentCount}/∞";
                }
                else
                {
                    int displayCapacity = Math.Max(1, slotCapacity);
                    capacityStr = $"{currentCount}/{displayCapacity}";
                }
            }
            else
            {
                capacityStr = massCapacity.ToString("F0");
            }

            var mainSeatObject = BagPatches.GetMainSeatObject(_bagController);

            float massFraction = massCapacity > 0 ? (totalMass / massCapacity) : 0f;
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

            string totalsSection = $"<size=20><b>Bag Totals</b></size>\n";

            if (useSlotBasedDisplay)
            {

                totalsSection += $"<color=#D1D1D1>Capacity:</color> {capacityStr}\n";
            }
            else
            {

                totalsSection += $"<color=#D1D1D1>Total Mass:</color> {totalMass:F0} / {capacityStr}\n";
            }

            totalsSection += $"<color=#FF4D4D>Speed Penalty:</color> {penalty:F1}%\n" +
                              $"<color=#EFD27F>Damage Coef:</color> {damageCoef:F2} ({actualDamage:F0})\n" +
                              $"<color=#FF4D4D>To Bagged Obj:</color> {baggedObjectDamage:F0}\n";

            if (!showFullStats)
            {

                _statsText.text = totalsSection;
                return;
            }

            if (mainSeatObject == null)
            {
                _statsText.text = "<size=24><b>Bagged Object</b></size>\n<color=#888888>Empty</color>\n\n" + totalsSection;
                return;
            }

            var state = StateCalculator.GetIndividualObjectState(_bagController, mainSeatObject);
            if (state == null || state.targetObject == null)
            {
                _statsText.text = "<size=24><b>Bagged Object</b></size>\n<color=#888888>Loading stats...</color>\n\n" + totalsSection;
                return;
            }

            string name = mainSeatObject.name.Replace("(Clone)", "");
            if (state.targetBody != null && !string.IsNullOrEmpty(state.targetBody.GetDisplayName()))
            {
                name = state.targetBody.GetDisplayName();
            }

            float mass = state.baggedMass;
            int junkCount = state.junkSpawnCount;

            string breakoutStr = "N/A";
            float breakoutTime = state.breakoutTime;
            float elapsedBreakoutTime = state.elapsedBreakoutTime;
            float breakoutAttempts = state.breakoutAttempts;

            if (!AdditionalSeatBreakoutTimer.CanBreakout(mainSeatObject))
            {
                breakoutTime = 0f;
            }

            if (breakoutTime > 0)
            {

                if (elapsedBreakoutTime >= breakoutTime)
                {
                    elapsedBreakoutTime = elapsedBreakoutTime % breakoutTime;
                }

                float remaining = breakoutTime - elapsedBreakoutTime;
                breakoutStr = $"{remaining:F1} / {breakoutTime:F1}s";
                if (breakoutAttempts > 0)
                {
                    breakoutStr += $" ({breakoutAttempts:F0})";
                }
            }

            _statsText.text = $"<size=24><b>{name}</b></size>\n" +
                              $"<color=#D1D1D1>Mass:</color> {mass:F1}\n" +
                              $"<color=#B87BFF>Junk on Drop:</color> {junkCount} cubes\n" +
                              $"<color=#FF8C00>Breakout:</color> {breakoutStr}\n" +
                              $"<color=#EFD27F>AtkSpd:</color> {state.attackSpeedStat:F2}\n" +
                              $"<color=#EFD27F>Dmg:</color> {state.damageStat:F2}\n" +
                              $"<color=#EFD27F>Crit:</color> {state.critStat:F2}%\n" +
                              $"<color=#4DBFFF>MvSpd:</color> {state.moveSpeedStat:F2}\n" +
                              $"<color=#FFFF00>Armor:</color> {state.armorStat:F2}\n" +
                              $"<color=#7BFC3A>Regen:</color> {state.regenStat:F2}\n\n" +
                              totalsSection;
        }

        private void SetUIVisible(bool visible)
        {
            if (_uiPanel != null && _uiPanel.activeSelf != visible)
            {
                _uiPanel.SetActive(visible);
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
