#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RiskOfOptions.Components.Options;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrifterBossGrabMod.Config.UI
{
    public class StringListController : OptionEditorController
    {
        public bool DiscoverComponents;
        public Func<IEnumerable<KeyValuePair<string, string>>>? Choices;
        public Func<string>? EmptyMessage;
        public Func<string>? CurrentTitle;
        public bool EliteEffectColumns;
        private bool IsChecklist => DiscoverComponents || Choices != null;
        private readonly Dictionary<string, string> _choiceLabels = new(StringComparer.Ordinal);
        private TMP_Text _title = null!;
        private string _originalValue = "";
        private TMP_InputField _add = null!;
        private Button _addButton = null!;
        private TMP_Text _addLabel = null!;
        private readonly Dictionary<string, GameObject> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Toggle> _toggles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Toggle> _grabToggles = new(StringComparer.Ordinal);
        private LayoutElement _layout = null!;
        private RectTransform _body = null!;
        private RectTransform _content = null!;
        private TMP_Text _count = null!;
        private TMP_Text _editLabel = null!;
        private TMP_Text _empty = null!;
        private TMP_InputField _search = null!;
        private Toggle _selectedOnly = null!;
        private ScrollRect _scroll = null!;
        private bool _expanded;
        private bool _loaded;
        private bool _built;
        private StringComparer EntryComparer => DiscoverComponents ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        protected override void Awake() { }

        protected override void Start()
        {
            _originalValue = Entry.Value;
            Build();
            _built = true;
            Entry.SettingChanged += OnSelectionChanged;
            if (DiscoverComponents) PluginConfig.Instance.ComponentChooserSortModeEntry.SettingChanged += OnSortChanged;
            RefreshSelection();
        }

        private void OnDestroy()
        {
            if (!_built) return;
            Entry.SettingChanged -= OnSelectionChanged;
            if (DiscoverComponents) PluginConfig.Instance.ComponentChooserSortModeEntry.SettingChanged -= OnSortChanged;
        }

        public override bool HasChanged() => Entry.Value != _originalValue;
        public override void Revert()
        {
            Entry.Value = _originalValue;
            optionController?.OptionChanged();
        }
        public override void CheckIfDisabled() { }
        protected override void Disable() { }
        protected override void Enable() { }

        private void Build()
        {
            _layout = gameObject.AddComponent<LayoutElement>();
            _layout.minHeight = _layout.preferredHeight = 64f;
            var header = EditorHeader();
            _title = Text(header, CurrentTitle?.Invoke() ?? Title, 24f);
            Place(_title.rectTransform, 0.02f, 0.53f, 0f, 64f);
            _count = Text(header, "", 18f);
            _count.color = new Color(0.8f, 0.85f, 0.9f);
            Place(_count.rectTransform, 0.54f, 0.79f, 0f, 64f);
            var edit = MakeButton(header, "Edit", ToggleExpanded);
            Place(edit, 0.81f, 0.98f, 14f, 36f);
            _editLabel = edit.GetComponentInChildren<TMP_Text>();
        }

        private void BuildBody()
        {
            _body = Rect("List Editor", transform);
            Place(_body, 0f, 1f, 64f, 390f);
            Surface(_body, PanelColor);

            _search = Input(_body, DiscoverComponents ? "Search or add components" : "Search entries");
            Place((RectTransform)_search.transform, 0.02f, 0.98f, 8f, 36f);
            _search.onValueChanged.AddListener(_ => FilterRows(true));
            if (IsChecklist)
            {
                _selectedOnly = CheckBox(_body, "Selected only", _ => FilterRows(true));
                Place((RectTransform)_selectedOnly.transform, 0.02f, 0.56f, 50f, 34f);
                if (DiscoverComponents)
                {
                    var add = MakeButton(_body, "Add", AddEntry);
                    Place(add, 0.58f, 0.76f, 50f, 34f);
                    _addButton = add.GetComponent<Button>();
                }
                Place(MakeButton(_body, "Refresh", () => { if (Choices != null) ReloadChoices(); else LoadComponents(); }), 0.78f, 0.98f, 50f, 34f);
            }
            else
            {
                _add = Input(_body, "Add an entry");
                Place((RectTransform)_add.transform, 0.02f, 0.78f, 50f, 34f);
                var add = MakeButton(_body, "Add", AddEntry);
                Place(add, 0.8f, 0.98f, 50f, 34f);
                _addButton = add.GetComponent<Button>();
                _add.onValueChanged.AddListener(_ => UpdateAddButton());
                _add.onSubmit.AddListener(_ => AddEntry());
            }

            if (_addButton != null) _addLabel = _addButton.GetComponentInChildren<TMP_Text>();
            if (EliteEffectColumns)
            {
                var throwLabel = Text(_body, "Throw", 18f);
                Place(throwLabel.rectTransform, 0.65f, 0.80f, 88f, 28f);
                throwLabel.alignment = TextAlignmentOptions.Center;
                var baggedLabel = Text(_body, "Bagged", 18f);
                Place(baggedLabel.rectTransform, 0.81f, 0.95f, 88f, 28f);
                baggedLabel.alignment = TextAlignmentOptions.Center;
            }
            float listTop = EliteEffectColumns ? 120f : 92f;
            float listHeight = EliteEffectColumns ? 264f : 288f;
            var viewport = Rect("Viewport", _body);
            Place(viewport, 0.02f, 0.96f, listTop, listHeight);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.scrollSensitivity = 28f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.viewport = viewport;
            _content = Rect("Components", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0f, 1f);
            _content.sizeDelta = Vector2.zero;
            var rowsLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.childControlHeight = rowsLayout.childControlWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.spacing = 2f;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
            var track = Rect("Scrollbar", _body);
            Place(track, 0.966f, 0.98f, listTop, listHeight);
            Surface(track, PanelColor);
            var handle = Rect("Handle", track);
            Stretch(handle);
            var thumb = Surface(handle, new Color(0.55f, 0.65f, 0.73f));
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = thumb;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _empty = Text(viewport, "No matching entries", 20f);
            Stretch(_empty.rectTransform);
            _empty.alignment = TextAlignmentOptions.Center;
            _body.gameObject.SetActive(false);
        }

        private void ToggleExpanded()
        {
            if (!_body) BuildBody();
            _expanded = !_expanded;
            _body.gameObject.SetActive(_expanded);
            _layout.minHeight = _layout.preferredHeight = _expanded ? 462f : 64f;
            _editLabel.text = _expanded ? "Done" : "Edit";
            if (_expanded && !_loaded)
            {
                if (Choices != null) ReloadChoices();
                else if (DiscoverComponents) LoadComponents();
                else { _loaded = true; RefreshSelection(); }
            }
        }

        public void ReloadChoices()
        {
            if (!_built || Choices == null) return;
            if (!_body) { RefreshSelection(); return; }
            foreach (var row in _rows.Values) { row.SetActive(false); Destroy(row); }
            _rows.Clear();
            _toggles.Clear();
            _grabToggles.Clear();
            _choiceLabels.Clear();
            foreach (var choice in Choices())
            {
                if (_choiceLabels.ContainsKey(choice.Key)) continue;
                _choiceLabels.Add(choice.Key, choice.Value);
                AddRow(choice.Key);
            }
            _loaded = true;
            _title.text = CurrentTitle?.Invoke() ?? Title;
            RefreshSelection();
        }

        private void LoadComponents()
        {
            try
            {
                var mode = PluginConfig.Instance.ComponentChooserSortModeEntry.Value;
                var camera = Camera.main;
                var origin = camera != null ? camera.transform.position : Vector3.zero;
                IEnumerable<Component> components;
                if (mode == ComponentChooserSortMode.ByRaycast)
                {
                    components = camera == null ? Array.Empty<Component>() : Physics.RaycastAll(new Ray(origin, camera.transform.forward), 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                        .Where(hit => hit.collider != null).SelectMany(hit => hit.collider.GetComponentsInParent<Component>());
                }
                else
                {
                    components = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                }
                var groups = components.Where(component => component != null).GroupBy(component => component.GetType().Name).ToArray();
                var names = mode == ComponentChooserSortMode.ByFrequency
                    ? groups.OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key)
                    : groups.OrderBy(group => group.Min(component => Vector3.SqrMagnitude(component.transform.position - origin))).ThenBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key);
                foreach (var row in _rows.Values)
                {
                    row.SetActive(false);
                    Destroy(row);
                }
                _rows.Clear();
                _toggles.Clear();
                _grabToggles.Clear();
                foreach (string name in names.Concat(Selected()).Distinct(StringComparer.Ordinal)) AddRow(name);
                _loaded = true;
                RefreshSelection();
                _scroll.verticalNormalizedPosition = 1f;
            }
            catch (Exception ex)
            {
                Log.Warning($"[ComponentChooser] Failed to scan components: {ex.Message}");
                _empty.text = "Could not load components. Try Refresh.";
                _empty.gameObject.SetActive(true);
            }
        }

        private List<string> Selected() => Entry.Value
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim())
            .Where(name => name.Length > 0).Distinct(EntryComparer).ToList();

        private void AddRow(string name)
        {
            GameObject row;
            if (EliteEffectColumns)
            {
                var rect = Rect(name, _content);
                Surface(rect, RowColor);
                var label = Text(rect, _choiceLabels.TryGetValue(name, out var displayName) ? displayName : name, 20f);
                Place(label.rectTransform, 0.02f, 0.64f, 0f, 34f);
                var throwToggle = CheckBox(rect, "", enabled => SetSelected(name, enabled));
                Place((RectTransform)throwToggle.transform, 0.67f, 0.83f, 0f, 34f);
                CenterCheckBox(throwToggle);
                var baggedToggle = CheckBox(rect, "", enabled => SetSelected("Grab:" + name, enabled));
                Place((RectTransform)baggedToggle.transform, 0.84f, 0.99f, 0f, 34f);
                CenterCheckBox(baggedToggle);
                baggedToggle.interactable = API.EliteThrowEffectsAPI.Entries.Any(entry => entry.Id == name && entry.SupportsGrab);
                _grabToggles.Add(name, baggedToggle);
                _toggles.Add(name, throwToggle);
                row = rect.gameObject;
            }
            else if (IsChecklist)
            {
                row = CheckBox(_content, _choiceLabels.TryGetValue(name, out var label) ? label : name, enabled => SetSelected(name, enabled)).gameObject;
                _toggles.Add(name, row.GetComponent<Toggle>());
            }
            else
            {
                var rect = Rect(name, _content);
                Surface(rect, RowColor);
                var label = Text(rect, name, 20f);
                Place(label.rectTransform, 0.02f, 0.77f, 0f, 34f);
                Place(MakeButton(rect, "Remove", () => SetSelected(name, false)), 0.79f, 0.98f, 2f, 30f);
                row = rect.gameObject;
            }
            row.AddComponent<LayoutElement>().preferredHeight = 34f;
            _rows.Add(name, row);
        }

        private void AddEntry()
        {
            if (!_addButton.interactable) return;
            var selected = Selected();
            var input = DiscoverComponents ? _search : _add;
            foreach (string name in input.text.Split(','))
            {
                string entry = name.Trim();
                if (entry.Length > 0 && !selected.Contains(entry, EntryComparer)) selected.Add(entry);
            }
            Entry.Value = string.Join(",", selected);
            input.SetTextWithoutNotify("");
            input.ForceLabelUpdate();
            FilterRows(true);
            optionController?.OptionChanged();
        }

        private static void CenterCheckBox(Toggle toggle)
        {
            var square = (RectTransform)toggle.transform.Find("Box");
            square.anchorMin = square.anchorMax = new Vector2(0.5f, 0.5f);
            square.anchoredPosition = Vector2.zero;
        }

        private void SetSelected(string name, bool enabled)
        {
            var selected = Selected();
            if (enabled && !selected.Contains(name)) selected.Add(name);
            else if (!enabled) selected.Remove(name);
            Entry.Value = string.Join(",", selected);
            optionController?.OptionChanged();
        }

        private void OnSelectionChanged(object sender, EventArgs args) => RefreshSelection();
        private void OnSortChanged(object sender, EventArgs args)
        {
            if (_loaded) LoadComponents();
        }

        private void RefreshSelection()
        {
            var selected = new HashSet<string>(Selected(), EntryComparer);
            _count.text = Choices != null ? $"{Choices().Count(choice => selected.Contains(choice.Key) || (EliteEffectColumns && selected.Contains("Grab:" + choice.Key)))} selected" :
                IsChecklist ? $"{selected.Count} selected" : $"{selected.Count} entries";
            UpdateAddButton();
            if (!_loaded) return;
            if (!IsChecklist)
            {
                foreach (string name in _rows.Keys.Where(name => !selected.Contains(name)).ToArray())
                {
                    _rows[name].SetActive(false);
                    Destroy(_rows[name]);
                    _rows.Remove(name);
                }
            }
            if (Choices == null)
                foreach (string name in selected.OrderBy(name => name, StringComparer.Ordinal))
                    if (!_rows.ContainsKey(name)) AddRow(name);
            if (IsChecklist)
                foreach (var row in _toggles) row.Value.SetIsOnWithoutNotify(selected.Contains(row.Key));
            foreach (var row in _grabToggles) row.Value.SetIsOnWithoutNotify(selected.Contains("Grab:" + row.Key));
            FilterRows(false);
        }

        private void UpdateAddButton()
        {
            if (_addButton == null) return;
            var names = (DiscoverComponents ? _search.text : _add.text).Split(',')
                .Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
            var selected = new HashSet<string>(Selected(), EntryComparer);
            bool duplicate = names.Length > 0 && names.All(selected.Contains);
            _addButton.interactable = names.Length > 0 && !duplicate;
            _addLabel.text = duplicate ? "Listed" : "Add";
        }

        private void FilterRows(bool resetScroll)
        {
            UpdateAddButton();
            int visible = 0;
            foreach (var row in _rows)
            {
                string label = _choiceLabels.TryGetValue(row.Key, out var text) ? text : row.Key;
                bool show = (!IsChecklist || !_selectedOnly.isOn || _toggles[row.Key].isOn || (_grabToggles.TryGetValue(row.Key, out var grabToggle) && grabToggle.isOn)) &&
                    (label.IndexOf(_search.text, StringComparison.OrdinalIgnoreCase) >= 0 || row.Key.IndexOf(_search.text, StringComparison.OrdinalIgnoreCase) >= 0);
                row.Value.SetActive(show);
                if (show) visible++;
            }
            _empty.text = EmptyMessage?.Invoke() ?? "No matching entries";
            _empty.enableWordWrapping = true;
            _empty.gameObject.SetActive(visible == 0);
            if (resetScroll) _scroll.verticalNormalizedPosition = 1f;
        }

    }
}
