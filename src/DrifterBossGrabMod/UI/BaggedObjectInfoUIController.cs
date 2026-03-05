using UnityEngine;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.UI
{
    public class BaggedObjectInfoUIController : MonoBehaviour
    {
        private GameObject _uiPanel = null!;
        private HGTextMeshProUGUI _statsText = null!;
        private CharacterBody _body = null!;
        private DrifterBagController _bagController = null!;
        private HUD _hud = null!;

        // Cached reflection metadata (resolved once, reused every frame)
        private System.Reflection.PropertyInfo? _cachedInputPlayerProperty;
        private System.Reflection.FieldInfo? _cachedInputPlayerField;
        private System.Reflection.MethodInfo? _cachedGetButtonMethod;
        private bool _reflectionCacheInitialized;
        private static readonly object[] _getButtonArgs = new object[] { "info" };

        private void Start()
        {
            _body = GetComponent<CharacterBody>();
            _bagController = GetComponent<DrifterBagController>();
        }

        private void Update()
        {
            if (_body == null || _bagController == null) return;
            if (!PluginConfig.Instance.EnableBaggedObjectInfo.Value)
            {
                SetUIVisible(false);
                return;
            }

            if (_hud == null)
            {
                // Find HUD for this character body
                foreach (var hud in HUD.readOnlyInstanceList)
                {
                    if (hud.targetBodyObject == _body.gameObject)
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
                    // Cache reflection metadata on first use
                    if (!_reflectionCacheInitialized)
                    {
                        var viewerType = _hud.localUserViewer.GetType();
                        _cachedInputPlayerProperty = viewerType.GetProperty("inputPlayer");
                        if (_cachedInputPlayerProperty == null)
                            _cachedInputPlayerField = viewerType.GetField("inputPlayer");
                        _reflectionCacheInitialized = true;
                    }

                    // Get the input player value (still per-frame, but metadata lookup is cached)
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

                if (showInfo)
                {
                    // Always show per-object stats (for the main seat object) + bag totals
                    UpdateStatsDisplay(showFullStats: true);
                    SetUIVisible(true);
                }
                else
                {
                    SetUIVisible(false);
                }
            }
        }

        private void InitializeUI(GameObject parentContainer)
        {
            if (_uiPanel != null) return;

            // Create a panel on the left side
            _uiPanel = new GameObject("BaggedObjectInfoPanel");
            _uiPanel.transform.SetParent(parentContainer.transform, false);

            var rectTransform = _uiPanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 0.5f);
            rectTransform.anchorMax = new Vector2(0, 0.5f);
            rectTransform.pivot = new Vector2(0, 0.5f);
            
            // Apply config layout
            ApplyConfigValues(rectTransform);

            var textObj = new GameObject("StatsText");
            textObj.transform.SetParent(_uiPanel.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(10, 10);
            textRect.offsetMax = new Vector2(-10, -10);

            _statsText = textObj.AddComponent<HGTextMeshProUGUI>();
            _statsText.fontSize = 20;
            _statsText.alignment = TMPro.TextAlignmentOptions.TopLeft;
            _statsText.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;
            _statsText.enableWordWrapping = true;
            _statsText.richText = true;
            
            SetUIVisible(false);
        }

        private void ApplyConfigValues(RectTransform rectTransform)
        {
            rectTransform.anchoredPosition = new Vector2(PluginConfig.Instance.BaggedObjectInfoX.Value, PluginConfig.Instance.BaggedObjectInfoY.Value);
            rectTransform.localScale = Vector3.one * PluginConfig.Instance.BaggedObjectInfoScale.Value;
            rectTransform.sizeDelta = new Vector2(500, 400); // Fixed size for info
        }

        private void UpdateStatsDisplay(bool showFullStats = true)
        {
            if (_statsText == null || _uiPanel == null) return;

            // Apply specific configs frequently if needed (could be optimized)
            if (_uiPanel.transform is RectTransform rect)
            {
                ApplyConfigValues(rect);
                _statsText.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;
            }

            var aggregateState = StateCalculator.GetAggregateState(_bagController);
            float totalMass = aggregateState.baggedMass;
            float penalty = aggregateState.movespeedPenalty * 100f;
            float massCapacity = DrifterBossGrabMod.Balance.CapacityScalingSystem.CalculateMassCapacity(_bagController);
            string capacityStr = massCapacity >= 100000f ? "INF" : massCapacity.ToString("F0");

            string totalsSection = $"<size=20><b>Bag Totals</b></size>\n" +
                              $"<color=#bbbbbb>Total Mass:</color> {totalMass:F0} / {capacityStr}\n" +
                              $"<color=#ff6666>Speed Penalty:</color> -{penalty:F1}%\n";

            if (!showFullStats)
            {
                // Only show bag totals
                _statsText.text = totalsSection;
                return;
            }

            // Full stats mode: show per-object stats + bag totals
            var mainSeatObject = BagPatches.GetMainSeatObject(_bagController);
            if (mainSeatObject == null)
            {
                _statsText.text = "<size=24><b>Bagged Object</b></size>\n<color=#888888>Empty</color>\n\n" + totalsSection;
                return;
            }

            var state = BaggedObjectStateStorage.LoadObjectState(_bagController, mainSeatObject);
            if (state == null)
            {
                _statsText.text = "<size=24><b>Bagged Object</b></size>\n<color=#888888>Loading stats...</color>\n\n" + totalsSection;
                return;
            }

            string name = mainSeatObject.name.Replace("(Clone)", "");
            if (state.targetBody != null && !string.IsNullOrEmpty(state.targetBody.GetDisplayName()))
            {
                name = state.targetBody.GetDisplayName();
            }

            float dmg = state.damageStat;
            float aspd = state.attackSpeedStat;
            float move = state.moveSpeedStat;
            float crit = state.critStat;
            float armor = state.armorStat;
            float regen = state.regenStat;
            float mass = state.baggedMass;

            _statsText.text = $"<size=24><b>{name}</b></size>\n" +
                              $"<color=#bbbbbb>Mass:</color> {mass:F1}\n" +
                              $"<color=#ff4444>Damage:</color> {dmg:F1}\n" +
                              $"<color=#ffaa00>Attack Speed:</color> {aspd:F2}\n" +
                              $"<color=#ffcc00>Crit Chance:</color> {crit:F1}%\n" +
                              $"<color=#44ccff>Armor:</color> {armor:F1}\n" +
                              $"<color=#44ff44>Regenerate:</color> {regen:F2} HP/s\n" +
                              $"<color=#ffffaa>Move Speed:</color> {move:F1}\n\n" +
                              totalsSection;
        }

        private void SetUIVisible(bool visible)
        {
            if (_uiPanel != null && _uiPanel.activeSelf != visible)
            {
                _uiPanel.SetActive(visible);
            }
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
