#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DrifterBossGrabMod.Balance;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrifterBossGrabMod.Config.UI
{
    public class FormulaEditorController : OptionEditorController
    {
        public FormulaEditorKind Kind;
        private readonly Dictionary<string, float> _samples = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Name, string Label)> _variables = new();
        private readonly Dictionary<string, TMP_InputField> _sampleInputs = new();
        private readonly Dictionary<string, float> _defaultSamples = new();
        private TMP_InputField _draft = null!;
        private TMP_Text _summary = null!;
        private TMP_Text _result = null!;
        private TMP_Text _editLabel = null!;
        private Button _apply = null!;
        private Button _useCurrent = null!;
        private TMP_Text _currentContext = null!;
        private RectTransform _body = null!;
        private LayoutElement _layout = null!;
        private string _originalValue = "";
        private float _bodyHeight;
        private bool _built;
        private bool _expanded;
        private bool _valid;
        private int _selectionStart;
        private int _selectionEnd;

        protected override void Awake() { }

        protected override void Start()
        {
            _originalValue = Entry.Value;
            Build();
            _built = true;
            Entry.SettingChanged += OnEntryChanged;
            if (Kind == FormulaEditorKind.Flag) PluginConfig.Instance.SelectedFlag.SettingChanged += OnFlagChanged;
            RefreshFromConfig();
        }

        private void OnDestroy()
        {
            if (_built) Entry.SettingChanged -= OnEntryChanged;
            if (_built && Kind == FormulaEditorKind.Flag) PluginConfig.Instance.SelectedFlag.SettingChanged -= OnFlagChanged;
        }

        private void Update()
        {
            if (_built && _expanded) RefreshCurrentButton();
            if (_built && _draft != null && _draft.isFocused)
            {
                _selectionStart = _draft.selectionStringAnchorPosition;
                _selectionEnd = _draft.selectionStringFocusPosition;
            }
        }

        public override bool HasChanged() => Entry.Value != _originalValue;
        public override void Revert()
        {
            Entry.Value = _originalValue;
            RefreshFromConfig();
            optionController?.OptionChanged();
        }
        public override void CheckIfDisabled() { }
        protected override void Disable() { }
        protected override void Enable() { }

        private void AddSamples()
        {
            void Add(string name, string label, float value)
            {
                _samples[name] = value;
                _defaultSamples[name] = value;
                _variables.Add((name, label));
            }
            if (Kind == FormulaEditorKind.Slam)
            {
                Add("BASE_COEF", "Base", SlamDamageCalculator.DefaultBaseDamageCoef);
                Add("MASS_SCALING", "Scaling", SlamDamageCalculator.DefaultMassScaling);
                Add("BM", "Bagged mass", 350f);
                Add("MC", "Mass capacity", 700f);
            }
            else if (Kind == FormulaEditorKind.Penalty)
            {
                Add("T", "Total mass", 350f);
                Add("M", "Mass capacity", 700f);
                Add("C", "Total slots", 3f);
                Add("H", "Max HP", 1000f);
                Add("L", "Level", 5f);
                Add("MC", "Passenger cap", 700f);
                Add("S", "Stage", 3f);
            }
            else
            {
                bool breakout = Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.MaxSmacks;
                Add("H", breakout ? "Drifter max HP" : "Max HP", 1000f);
                Add("L", breakout ? "Drifter level" : "Level", 5f);
                Add("C", breakout ? "Drifter stocks" : "Utility stocks", 1f);
                Add("S", "Stage", 3f);
                if (Kind != FormulaEditorKind.Slots) Add("MC", "Passenger cap", 700f);
                if (Kind == FormulaEditorKind.Flag)
                {
                    Add("B", "Base mass", 100f);
                    Add("BH", "Base max HP", 1000f);
                }
            }
            if (Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.GrabRange || Kind == FormulaEditorKind.MaxSmacks || Kind == FormulaEditorKind.Slam)
            {
                Add("N", "Passenger count", 2f);
                if (Kind != FormulaEditorKind.Slam) Add("T", "Total bag mass", 350f);
            }
            if (Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.MaxSmacks)
            {
                Add("PM", "Passenger mass", 100f);
                Add("PH", "Passenger max HP", 1000f);
                Add("PL", "Passenger level", 5f);

                if (Kind == FormulaEditorKind.Breakout) Add("BT", "Seconds", 10f);
            }
            if (Kind != FormulaEditorKind.GrabRange)
                foreach (string flag in FlagFormulaVariables.Names)
                    Add(flag, CultureInfo.InvariantCulture.TextInfo.ToTitleCase(flag.ToLowerInvariant()), 0f);
            if (Kind == FormulaEditorKind.Capacity || Kind == FormulaEditorKind.Slam)
            {
                Add("BH", "Base max HP", 1000f);
                Add("B", "Base mass", 0f);
            }
            if (Kind == FormulaEditorKind.Slam)
            {
                Add("H", "Max health", 1000f);
                Add("L", "Level", 5f);
                Add("C", "Utility stocks", 1f);
                Add("S", "Stage", 3f);
            }
        }

        private void Build()
        {
            _layout = gameObject.AddComponent<LayoutElement>();
            _layout.minHeight = _layout.preferredHeight = 64f;
            var header = EditorHeader();
            var title = Text(header, Title, 22f);
            Place(title.rectTransform, 0.02f, 0.48f, 0f, 64f);
            _summary = Text(header, "", 18f);
            Place(_summary.rectTransform, 0.5f, 0.78f, 0f, 64f);
            var edit = MakeButton(header, "Edit", ToggleExpanded);
            Place(edit, 0.8f, 0.98f, 8f, 48f);
            _editLabel = edit.GetComponentInChildren<TMP_Text>();
        }

        private void BuildBody()
        {
            AddSamples();
            _body = Rect("Formula Editor", transform);
            Surface(_body, PanelColor);
            var hint = Text(_body, "Edit a draft, then Apply. Variable buttons insert at the cursor.", 18f);
            Place(hint.rectTransform, 0.02f, 0.98f, 8f, 24f);
            _draft = Input(_body, "Enter a formula");
            _draft.lineType = TMP_InputField.LineType.MultiLineNewline;
            _draft.onFocusSelectAll = false;
            _draft.textComponent.enableWordWrapping = true;
            _draft.textComponent.overflowMode = TextOverflowModes.Masking;
            Place((RectTransform)_draft.transform, 0.02f, 0.98f, 38f, 68f);
            _draft.onValueChanged.AddListener(_ => UpdatePreview());
            var sampleTitle = Text(_body, Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.MaxSmacks
                ? "Preview only; passenger flags are 0/1" : "Preview inputs; flags are 0/1", 18f);
            Place(sampleTitle.rectTransform, 0.02f, 0.76f, 114f, 28f);
            var reset = MakeButton(_body, "Reset", ResetSamples);
            reset.GetComponentInChildren<TMP_Text>().fontSize = 16f;
            Place(reset, 0.8f, 0.98f, 114f, 28f);
            var current = MakeButton(_body, "Use current values", UseCurrentValues);
            current.GetComponentInChildren<TMP_Text>().fontSize = 18f;
            Place(current, 0.02f, 0.49f, 150f, 28f);
            _useCurrent = current.GetComponent<Button>();
            _currentContext = Text(_body, "", 16f);
            Place(_currentContext.rectTransform, 0.51f, 0.98f, 150f, 28f);
            RefreshCurrentButton();
            for (int i = 0; i < _variables.Count; i++)
            {
                var variable = _variables[i];
                var button = MakeButton(_body, $"{variable.Label} ({variable.Name})", () => Insert(variable.Name));
                var label = button.GetComponentInChildren<TMP_Text>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = 16f;
                label.enableWordWrapping = false;
                float left = 0.02f + (i % 2) * 0.49f;
                float top = 186f + (i / 2) * 36f;
                Place(button, left, left + 0.32f, top, 32f);
                var input = Input(_body, variable.Name);
                input.textComponent.fontSize = 18f;
                Place((RectTransform)input.transform, left + 0.33f, left + 0.47f, top, 32f);
                input.SetTextWithoutNotify(_samples[variable.Name].ToString("R", CultureInfo.InvariantCulture));
                _sampleInputs.Add(variable.Name, input);
                input.onValueChanged.AddListener(_ => UpdatePreview());
            }
            float resultTop = 194f + ((_variables.Count + 1) / 2) * 36f;
            _result = Text(_body, "", 20f);
            Place(_result.rectTransform, 0.02f, 0.98f, resultTop, 58f);
            _result.enableWordWrapping = true;
            _result.overflowMode = TextOverflowModes.Truncate;
            var apply = MakeButton(_body, "Apply", Apply);
            Place(apply, 0.6f, 0.78f, resultTop + 64f, 36f);
            _apply = apply.GetComponent<Button>();
            Place(MakeButton(_body, "Cancel", Cancel), 0.8f, 0.98f, resultTop + 64f, 36f);
            _bodyHeight = resultTop + 108f;
            Place(_body, 0f, 1f, 64f, _bodyHeight);
            _body.gameObject.SetActive(false);
        }

        private void Insert(string variable)
        {
            int start = Mathf.Clamp(Math.Min(_selectionStart, _selectionEnd), 0, _draft.text.Length);
            int end = Mathf.Clamp(Math.Max(_selectionStart, _selectionEnd), start, _draft.text.Length);
            _draft.text = _draft.text.Substring(0, start) + variable + _draft.text.Substring(end);
            _selectionStart = _selectionEnd = start + variable.Length;
            _draft.ActivateInputField();
            _draft.selectionStringAnchorPosition = _draft.selectionStringFocusPosition = _selectionEnd;
        }

        private bool TryGetCurrentContext(out CharacterBody? body, out DrifterBagController? bag, out string context)
        {
            body = null;
            bag = null;
            context = "Available during a run";
            if (!Run.instance) return false;
            var localBody = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (localBody == null) return false;
            bag = localBody.GetComponent<DrifterBagController>();
            if ((Kind == FormulaEditorKind.Slam || Kind == FormulaEditorKind.Penalty
                || Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.MaxSmacks) && bag == null)
            {
                context = "Requires Drifter's bag";
                return false;
            }
            if (Kind == FormulaEditorKind.Flag)
            {
                body = bag?.vehicleSeat?.NetworkpassengerBodyObject?.GetComponent<CharacterBody>();
                if (body == null)
                {
                    context = "Select a bagged enemy";
                    return false;
                }
            }
            else body = localBody;
            context = "From: " + Language.GetString(body.baseNameToken);
            if (Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.MaxSmacks)
            {
                var passenger = BagPatches.GetMainSeatObject(bag!);
                if (passenger == null)
                {
                    context = "Select a bagged object";
                    return false;
                }
                var passengerBody = passenger.GetComponent<CharacterBody>();
                context += " + " + (passengerBody != null ? Language.GetString(passengerBody.baseNameToken) : passenger.name);
            }
            return true;
        }

        private void RefreshCurrentButton()
        {
            _useCurrent.interactable = TryGetCurrentContext(out _, out _, out string context);
            _currentContext.text = context;
        }

        private void UseCurrentValues()
        {
            if (!TryGetCurrentContext(out var body, out var bag, out _) || body == null) return;
            var values = FormulaRegistry.GetVariables(body);
            if (Kind == FormulaEditorKind.Slots || Kind == FormulaEditorKind.Penalty)
            {
                values["H"] = body.maxHealth;
                values["L"] = body.level;
                values["C"] = body.skillLocator != null && body.skillLocator.utility != null
                    ? body.skillLocator.utility.maxStock : 1f;
                values["S"] = Run.instance ? Run.instance.stageClearCount + 1 : 1;
                if (Kind == FormulaEditorKind.Penalty) values["MC"] = PluginConfig.Instance.ParsedMassCap;
            }
            if (Kind == FormulaEditorKind.Slots && (body.skillLocator == null || body.skillLocator.utility == null))
            {
                values["H"] = 0f;
                values["L"] = 0f;
                values["C"] = 0f;
            }
            if (Kind == FormulaEditorKind.Slam && bag != null)
            {
                values["BASE_COEF"] = SlamDamageCalculator.DefaultBaseDamageCoef;
                values["MASS_SCALING"] = SlamDamageCalculator.DefaultMassScaling;
                values["BM"] = bag.baggedMass;
                values["MC"] = CapacityScalingSystem.CalculateMassCapacity(bag);
            }
            if (Kind == FormulaEditorKind.Penalty && bag != null)
            {
                values["T"] = bag.baggedMass;
                values["M"] = CapacityScalingSystem.CalculateMassCapacity(bag);
                values["C"] = CapacityScalingSystem.GetTotalCapacity(bag);
            }
            if (Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.GrabRange || Kind == FormulaEditorKind.MaxSmacks || Kind == FormulaEditorKind.Slam)
            {
                var passenger = Kind != FormulaEditorKind.GrabRange && bag != null ? BagPatches.GetMainSeatObject(bag) : null;
                float baseTime = 0f;
                if (Kind == FormulaEditorKind.Breakout && bag != null && passenger != null)
                {
                    var settings = new EntityStates.Drifter.Bag.BaggedObject();
                    baseTime = Mathf.Max(settings.baseBreakoutTime - 0.005f * bag.CalculateBaggedObjectMass(passenger), 1f);
                    var passengerBody = passenger.GetComponent<CharacterBody>();
                    if (passengerBody != null && passengerBody.isElite) baseTime *= settings.eliteBreakoutTimeCoefficient;
                }
                foreach (var variable in BagFormulaContext.Create(body, bag, passenger, baseTime))
                    values[variable.Key] = variable.Value;
            }
            if (Kind == FormulaEditorKind.Flag)
                values["B"] = RepossessPatches.GetBaseObjectMass(body.gameObject);
            foreach (var sample in _sampleInputs)
            {
                if (!values.ContainsKey(sample.Key))
                {
                    _currentContext.text = "Unavailable: " + sample.Key;
                    return;
                }
            }
            foreach (var sample in _sampleInputs)
            {
                float value = values[sample.Key];
                string text = value >= float.MaxValue ? "INF" : value <= float.MinValue ? "-INF"
                    : value.ToString("R", CultureInfo.InvariantCulture);
                sample.Value.SetTextWithoutNotify(text);
            }
            UpdatePreview();
        }

        private void ResetSamples()
        {
            foreach (var sample in _defaultSamples)
                _sampleInputs[sample.Key].SetTextWithoutNotify(sample.Value.ToString("R", CultureInfo.InvariantCulture));
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            foreach (var sample in _sampleInputs)
            {
                string text = sample.Value.text.Trim();
                float sampleValue;
                bool valid;
                if (string.Equals(text, "INF", StringComparison.OrdinalIgnoreCase))
                {
                    sampleValue = float.MaxValue;
                    valid = true;
                }
                else if (string.Equals(text, "-INF", StringComparison.OrdinalIgnoreCase))
                {
                    sampleValue = float.MinValue;
                    valid = true;
                }
                else
                    valid = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out sampleValue)
                        && !float.IsNaN(sampleValue) && !float.IsInfinity(sampleValue);
                if (!valid)
                {
                    _valid = false;
                    _apply.interactable = false;
                    _result.color = new Color(1f, 0.7f, 0.65f);
                    _result.text = $"Enter a number or INF for {sample.Key}.";
                    return;
                }
                _samples[sample.Key] = sampleValue;
            }
            _valid = FormulaParser.TryPreview(_draft.text, _samples, out float value, out string? error);
            if (_valid && (Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.GrabRange)
                && (value <= 0f || value >= float.MaxValue))
            {
                _valid = false;
                error = "Enter a positive finite value.";
            }
            _apply.interactable = _valid && _draft.text.Trim() != Entry.Value;
            _result.color = _valid ? new Color(0.8f, 0.91f, 0.8f) : new Color(1f, 0.7f, 0.65f);
            if (!_valid) { _result.text = error ?? "Invalid formula."; return; }
            string result = value >= float.MaxValue ? "INF" : value <= float.MinValue ? "-INF" : value.ToString("0.###", CultureInfo.InvariantCulture);
            if (Kind == FormulaEditorKind.Slots && value < int.MaxValue && value > int.MinValue)
                result = Math.Floor(value).ToString(CultureInfo.InvariantCulture) + " slots (before weight limits)";
            if (Kind == FormulaEditorKind.Capacity && value <= 0f) result = "Unlimited mass capacity";
            if (Kind == FormulaEditorKind.Penalty && value < float.MaxValue && value > float.MinValue)
                result = (value * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "% speed penalty (before limits)";
            if (Kind == FormulaEditorKind.Slam || Kind == FormulaEditorKind.Flag || Kind == FormulaEditorKind.Breakout || Kind == FormulaEditorKind.GrabRange) result += "x";
            if (Kind == FormulaEditorKind.MaxSmacks) result = value >= float.MaxValue ? "Unlimited hits" : Math.Floor(value).ToString(CultureInfo.InvariantCulture) + " hits";
            if (Kind == FormulaEditorKind.Breakout) result += " interval multiplier (" + (_samples["BT"] * value).ToString("0.###", CultureInfo.InvariantCulture) + " seconds)";
            _result.text = "Sample result: " + result;
        }

        private void Apply()
        {
            UpdatePreview();
            if (!_valid) return;
            Entry.Value = _draft.text.Trim();
            optionController?.OptionChanged();
            Close();
        }

        private void Cancel() { RefreshFromConfig(); Close(); }
        private void ToggleExpanded()
        {
            if (_expanded) { Cancel(); return; }
            if (!_body) BuildBody();
            RefreshFromConfig();
            _expanded = true;
            _body.gameObject.SetActive(true);
            _layout.minHeight = _layout.preferredHeight = 64f + _bodyHeight + 8f;
            _editLabel.text = "Close";
        }
        private void Close()
        {
            _expanded = false;
            _body.gameObject.SetActive(false);
            _layout.minHeight = _layout.preferredHeight = 64f;
            _editLabel.text = "Edit";
        }
        private void OnEntryChanged(object sender, EventArgs args) => RefreshFromConfig();
        private void OnFlagChanged(object sender, EventArgs args)
        {
            _originalValue = Entry.Value;
            RefreshFromConfig();
        }
        private void RefreshFromConfig()
        {
            string value = Entry.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                float? fallback = Kind switch
                {
                    FormulaEditorKind.Breakout => 1.0f,
                    FormulaEditorKind.GrabRange => 1.0f,
                    FormulaEditorKind.MaxSmacks => 3f,
                    _ => null
                };
                if (fallback.HasValue) value = fallback.Value.ToString("R", CultureInfo.InvariantCulture);
            }
            _summary.text = value;
            if (!_body) return;
            _draft.SetTextWithoutNotify(value);
            _selectionStart = _selectionEnd = _draft.text.Length;
            UpdatePreview();
        }
    }
}
