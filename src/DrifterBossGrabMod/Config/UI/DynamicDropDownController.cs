using UnityEngine;
using RiskOfOptions.Components.Options;
using RiskOfOptions.Options;
using RoR2.UI;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine.EventSystems;

namespace DrifterBossGrabMod.Config.UI
{
    public class DropdownPointerClickListener : MonoBehaviour, IPointerDownHandler
    {
        public Action OnClick = null!;
        public void OnPointerDown(PointerEventData eventData)
        {
            OnClick?.Invoke();
        }
    }

    public class DynamicDropDownController : ModSetting
    {
        public RooDropdown dropdown = null!;

        private static List<string>? _cachedComponentNames;
        private static string[]? _dropdownChoices;

        protected override void Awake()
        {
            dropdown = GetComponentInChildren<RooDropdown>(true);
            nameLabel = GetComponentInChildren<LanguageTextMeshController>(true);

            if (dropdown != null)
            {
                var clickListener = dropdown.gameObject.AddComponent<DropdownPointerClickListener>();
                clickListener.OnClick = OnDropdownClicked;
            }
        }

        private void OnEnable()
        {
            if (nameLabel != null)
            {
                nameLabel.token = nameToken;
            }

            if (dropdown != null)
            {
                if (_dropdownChoices == null)
                {
                    dropdown.choices = new string[] { "-- Click to Load & Select --" };
                }
                else
                {
                    dropdown.choices = _dropdownChoices;
                }
                dropdown.OnValueChanged.AddListener(OnChoiceChanged);
                dropdown.SetChoice(0);
            }
        }

        private void OnDisable()
        {
            if (dropdown != null)
            {
                dropdown.OnValueChanged.RemoveListener(OnChoiceChanged);
            }
        }

        public override bool HasChanged() => false;
        public override void Revert() { }
        public override void CheckIfDisabled() { }
        protected override void Disable() { }
        protected override void Enable() { }

        private void OnDropdownClicked()
        {
            LoadComponents();
            if (dropdown != null)
            {
                dropdown.choices = _dropdownChoices ?? Array.Empty<string>();
                // Trigger refresh if needed by re-setting the choice
                dropdown.SetChoice(0);
            }
        }

        private void LoadComponents()
        {
            Log.Info("[DynamicDropDownController] Scanning active scene for component types...");
            _cachedComponentNames = new List<string> { "-- Select to Toggle --" };
            try
            {
                var allComponents = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);

                var sortMode = PluginConfig.Instance.ComponentChooserSortModeEntry.Value;

                if (sortMode == ComponentChooserSortMode.ByProximity)
                {
                    Vector3 referencePos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

                    var componentTypes = allComponents
                        .Where(c => c != null && c.gameObject != null)
                        .Select(c => new
                        {
                            Component = c,
                            Distance = Vector3.Distance(c.transform.position, referencePos)
                        })
                        .GroupBy(x => x.Component.GetType().Name)
                        .Select(g => new
                        {
                            Name = g.Key,
                            MinDistance = g.Min(x => x.Distance)
                        })
                        .OrderBy(x => x.MinDistance)
                        .Take(500)
                        .Select(x => $"{x.Name} ({Mathf.RoundToInt(x.MinDistance)}m)")
                        .ToList();

                    _cachedComponentNames.AddRange(componentTypes);
                }
                else if (sortMode == ComponentChooserSortMode.ByRaycast)
                {
                    Camera? cam = Camera.main;
                    if (cam != null)
                    {
                        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
                        var hits = Physics.RaycastAll(ray, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

                        var componentTypes = hits
                            .Where(h => h.collider != null)
                            .SelectMany(h => h.collider.GetComponentsInParent<Component>().Select(c => new
                            {
                                Component = c,
                                Distance = h.distance,
                                GameObjectName = c.gameObject.name
                            }))
                            .Where(x => x.Component != null && x.Component.GetType().Name != "Transform" && x.Component.GetType().Name != "MeshRenderer" && x.Component.GetType().Name != "MeshFilter")
                            .GroupBy(x => new { Name = x.Component.GetType().Name, GameObjectName = x.GameObjectName })
                            .Select(g => new
                            {
                                Name = g.Key.Name,
                                GameObjectName = g.Key.GameObjectName,
                                MinDistance = g.Min(x => x.Distance)
                            })
                            .OrderBy(x => x.MinDistance)
                            .Take(500)
                            .Select(x => $"{x.Name} ({x.GameObjectName} - {Mathf.RoundToInt(x.MinDistance)}m)")
                            .ToList();

                        _cachedComponentNames.AddRange(componentTypes);
                    }
                }
                else
                {
                    var componentTypes = allComponents
                        .Where(c => c != null)
                        .GroupBy(c => c.GetType().Name)
                        .Select(g => new { Name = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(500) // Limit to top 500 to prevent absurdly massive lists
                        .Select(x => $"{x.Name} ({x.Count})")
                        .ToList();

                    _cachedComponentNames.AddRange(componentTypes);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DynamicDropDownController] Failed to scan scene components: {ex}");
            }

            _dropdownChoices = _cachedComponentNames.ToArray();
            Log.Info($"[DynamicDropDownController] Loaded {_dropdownChoices!.Length} component types from the scene.");
        }

        private void OnChoiceChanged(int newValue)
        {
            if (_cachedComponentNames == null) return;
            if (newValue == 0) return; // The "-- Select to Toggle --" option

            string selectedComponent = _cachedComponentNames[newValue];

            // Strip the " (count)" format to get just the class name
            int bracketIndex = selectedComponent.IndexOf(" (");
            if (bracketIndex > 0)
            {
                selectedComponent = selectedComponent.Substring(0, bracketIndex);
            }

            // Toggle in config
            string currentVal = PluginConfig.Instance.GrabbableComponentTypes.Value;
            var components = currentVal.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .ToList();

            if (components.Contains(selectedComponent))
            {
                components.Remove(selectedComponent);
                Log.Info($"[DynamicDropDownController] Removed {selectedComponent} from GrabbableComponentTypes.");
            }
            else
            {
                components.Add(selectedComponent);
                Log.Info($"[DynamicDropDownController] Added {selectedComponent} to GrabbableComponentTypes.");
            }

            PluginConfig.Instance.GrabbableComponentTypes.Value = string.Join(",", components);

            // Revert dropdown back to index 0 visually
            if (dropdown != null)
            {
                dropdown.SetChoice(0);
            }

            // Re-render RiskOfOptions ModSettingsInputField components to ensure GrabbableComponentTypes visual update
            PresetManager.RefreshPresetDropdownUI();
        }
    }
}
