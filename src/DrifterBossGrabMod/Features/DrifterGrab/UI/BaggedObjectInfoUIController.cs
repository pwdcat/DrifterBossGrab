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
        private CharacterBody _body = null!;
        private DrifterBagController _bagController = null!;
        private HUD? _hud;
        private bool _cachedEnableBaggedObjectInfo;
        // Cached state objects to avoid per-frame allocations
        private BaggedObjectStateData _cachedAggregateState = new BaggedObjectStateData();
        private BaggedObjectStateData _cachedIndividualState = new BaggedObjectStateData();

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

            // Cache check for enabled state (dirty flag pattern)
            if (_cachedEnableBaggedObjectInfo != PluginConfig.Instance.EnableBaggedObjectInfo.Value)
            {
                _cachedEnableBaggedObjectInfo = PluginConfig.Instance.EnableBaggedObjectInfo.Value;
                SetUIVisible(false);
                if (!_cachedEnableBaggedObjectInfo) return;
            }

            if (!_cachedEnableBaggedObjectInfo)
            {
                SetUIVisible(false);
                return;
            }

            // Re-verify HUD connection
            if (_hud != null && _hud.targetBodyObject != _body.gameObject)
            {
                SetUIVisible(false);
                _hud = null;
            }

            if (_hud == null)
            {
                // Find HUD for this character body
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
                    var player = _hud.localUserViewer.inputPlayer;
                    if (player != null)
                    {
                        showInfo = player.GetButton("info");
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

        // ========================================================================================
        // UI INITIALIZATION
        // ========================================================================================

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

        // ========================================================================================
        // STATS DISPLAY
        // ========================================================================================

        private void UpdateStatsDisplay(bool showFullStats = true)
        {
            if (_statsText == null || _uiPanel == null) return;

            // Apply specific configs frequently if needed (could be optimized)
            if (_uiPanel.transform is RectTransform rect)
            {
                ApplyConfigValues(rect);
                _statsText.color = PluginConfig.Instance.BaggedObjectInfoColor.Value;
            }

            var aggregateState = StateCalculator.GetAggregateState(_bagController, _cachedAggregateState);
            float totalMass = aggregateState.baggedMass;
            float penalty = aggregateState.movespeedPenalty * 100f;

            // Calculate mass capacity for damage calculations
            float massCapacity = CapacityScalingSystem.CalculateMassCapacity(_bagController);

            // Calculate display mass capacity
            float displayMassCapacity = DrifterBossGrabMod.Balance.CapacityScalingSystem.CalculateMassCapacity(_bagController);

            // Check if we should use slot-based display
            bool useSlotBasedDisplay = !PluginConfig.Instance.EnableBalance.Value ||
                PluginConfig.Instance.MassCapacityFormula.Value.Trim() == "0";

            // Check if bottomless bag is enabled with INF capacity
            bool isBottomlessBag = PluginConfig.Instance.BottomlessBagEnabled.Value &&
                PluginConfig.Instance.IsSlotScalingFormulaInfinite;

            string capacityStr;
            if (useSlotBasedDisplay)
            {
                // Use slot-based display format
                int currentCount = BagCapacityCalculator.GetCurrentBaggedCount(_bagController);
                int slotCapacity = BagCapacityCalculator.GetUtilityMaxStock(_bagController);

                // For bottomless bag with INF, show as "current/∞"
                if (isBottomlessBag)
                {
                    capacityStr = $"{currentCount}/∞";
                }
                else
                {
                    // For normal slot-based display, clamp slot capacity to at least 1
                    int displayCapacity = Math.Max(1, slotCapacity);
                    capacityStr = $"{currentCount}/{displayCapacity}";
                }
            }
            else
            {
                // Use mass-based display format
                capacityStr = displayMassCapacity >= 100000f ? "INF" : displayMassCapacity.ToString("F0");
            }

            // Get the main seat object for per-object stats display
            var mainSeatObject = API.DrifterBagAPI.GetMainPassenger(_bagController);

            // Calculate damage coefficient using the same formula as actual slam damage
            float massFraction = massCapacity > 0 ? (totalMass / massCapacity) : 0f;
            float damageCoef = SlamDamageCalculator.GetEffectiveCoefficient(_bagController);

            float baseDamage = _body.damage * damageCoef;

            // Apply item damage modifiers
            float itemDamageMultiplier = GetItemDamageMultiplier();
            float damageWithItems = baseDamage * itemDamageMultiplier;

            // Calculate actual damage considering crit chance
            // Show crit damage if crit chance is 100% or higher
            float actualDamage = damageWithItems;
            if (_body.crit >= 100f)
            {
                actualDamage = damageWithItems * _body.critMultiplier;
            }

            // Calculate damage to bagged object
            float baggedObjectDamage = actualDamage;
            float baggedObjectArmor = 0f;
            if (mainSeatObject != null)
            {
                var baggedBody = mainSeatObject.GetComponent<CharacterBody>();
                var junkCubeController = mainSeatObject.GetComponent<JunkCubeController>();
                var soa = mainSeatObject.GetComponent<SpecialObjectAttributes>();

                // Priority 1: JunkCubeController (decrements ActivationCount by 1 per hit)
                if (junkCubeController != null && junkCubeController.ActivationCount > 0)
                {
                    // Show decrement amount (1 per hit)
                    baggedObjectDamage = 1f;
                }
                // Priority 2: CharacterBody with armor
                else if (baggedBody != null)
                {
                    baggedObjectArmor = baggedBody.armor;

                    // Recalculate damage with Crowbar since we know the target's health
                    float damageWithCrowbar = baseDamage * GetItemDamageMultiplier(baggedBody);

                    // Apply crit if applicable
                    if (_body.crit >= 100f)
                    {
                        damageWithCrowbar *= _body.critMultiplier;
                    }

                    // Calculate armor reduction
                    float armorFactor = baggedObjectArmor >= 0 ? (100f / (100f + baggedObjectArmor)) : (2f - (100f / (100f - baggedObjectArmor)));
                    baggedObjectDamage = damageWithCrowbar * armorFactor;
                }
                // Priority 3: SpecialObjectAttributes with durability (environmental objects like chests)
                else if (soa != null && soa.maxDurability > 0)
                {
                    // Show decrement amount (1 per hit for SOA objects)
                    baggedObjectDamage = 1f;
                }
            }

            string totalsSection = $"<size=20><b>Bag Totals</b></size>\n";

            // Show different label based on display mode
            if (useSlotBasedDisplay)
            {
                // Slot-based display: show "Capacity: X/Y"
                totalsSection += $"<color=#D1D1D1>Capacity:</color> {capacityStr}\n";
            }
            else
            {
                // Mass-based display: show "Total Mass: X / Y"
                totalsSection += $"<color=#D1D1D1>Total Mass:</color> {totalMass:F0} / {capacityStr}\n";
            }

            totalsSection += $"<color=#FF4D4D>Speed Penalty:</color> {penalty:F1}%\n" +
                              $"<color=#EFD27F>Damage Coef:</color> {damageCoef:F2} ({actualDamage:F0})\n" +
                              $"<color=#FF4D4D>To Bagged Obj:</color> {baggedObjectDamage:F0}\n";

            if (!showFullStats)
            {
                // Only show bag totals
                _statsText.text = totalsSection;
                return;
            }

            // Full stats mode: show per-object stats + bag totals
            if (mainSeatObject == null)
            {
                _statsText.text = "<size=24><b>Bagged Object</b></size>\n<color=#888888>Empty</color>\n\n" + totalsSection;
                return;
            }

            // Use StateCalculator to get live state (reads from current BaggedObject state machine)
            var state = StateCalculator.GetIndividualObjectState(_bagController, mainSeatObject, _cachedIndividualState);
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

            // Format breakout timer - state.elapsedBreakoutTime is now live from StateCalculator
            string breakoutStr = "N/A";
            float breakoutTime = state.breakoutTime;
            float elapsedBreakoutTime = state.elapsedBreakoutTime;
            float breakoutAttempts = state.breakoutAttempts;

            // Check if object can actually breakout - objects like junk cubes cannot breakout
            if (!AdditionalSeatBreakoutTimer.CanBreakout(mainSeatObject))
            {
                breakoutTime = 0f;
            }

            if (breakoutTime > 0)
            {
                // Reset elapsed time for UI display when it exceeds breakout time (after breakout attempt)
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

            // Delicate Watch (FragileDamageBonus) - +20% per stack
            int fragileStacks = _body.inventory.GetItemCountEffective(DLC1Content.Items.FragileDamageBonus);
            if (fragileStacks > 0)
            {
                itemDamageMultiplier *= 1f + fragileStacks * 0.2f;
            }

            // Nearby Damage Bonus - +20% per stack when within 13m
            int nearbyDamageStacks = _body.inventory.GetItemCountEffective(RoR2Content.Items.NearbyDamageBonus);
            if (nearbyDamageStacks > 0)
            {
                itemDamageMultiplier *= 1f + nearbyDamageStacks * 0.2f;
            }

            // Crowbar - +75% per stack when target >= 90% health
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
