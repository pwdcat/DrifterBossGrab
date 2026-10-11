#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using RoR2.UI;

namespace DrifterBossGrabMod.UI
{
    public class HudEditorManager : MonoBehaviour
    {
        public static bool IsEditorActive { get; private set; }
        public static HudDraggable? Selected { get; private set; }
        private static HudEditorManager? _instance;
        private HudEditorScreen? _screen;
        private MPEventSystem? _eventSystem;

        private void Awake()
        {
            if (_instance != null) { Destroy(this); return; }
            _instance = this;
            PluginConfig.Instance.IsHudEditorEnabled.SettingChanged += OnConfigToggleChanged;
        }

        private void OnConfigToggleChanged(object? sender, System.EventArgs e)
            => SetEditorActive(PluginConfig.Instance.IsHudEditorEnabled.Value);

        private void Update()
        {
            if (!IsEditorActive) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) SetEditorActive(false);
        }

        public static void Select(HudDraggable draggable) => Selected = draggable;
        public static void ToggleEditor() => SetEditorActive(!IsEditorActive);

        public static void SetEditorActive(bool active)
        {
            if (_instance == null || IsEditorActive == active) return;
            IsEditorActive = active;
            if (PluginConfig.Instance.IsHudEditorEnabled.Value != active)
                PluginConfig.Instance.IsHudEditorEnabled.Value = active;
            if (active) _instance.Open();
            else _instance.Close();
        }

        private void Open()
        {
            Selected = null;
            _eventSystem = EventSystem.current as MPEventSystem ?? RoR2.MPEventSystemManager.primaryEventSystem;
            if (_eventSystem != null) _eventSystem.cursorOpenerCount++;
            var screen = new GameObject("Drifter HUD Editor", typeof(RectTransform));
            _screen = screen.AddComponent<HudEditorScreen>();
        }

        private void Close()
        {
            if (_screen != null) Destroy(_screen.gameObject);
            _screen = null;
            HudContextMenu.Close();
            if (_eventSystem != null) _eventSystem.cursorOpenerCount = Mathf.Max(0, _eventSystem.cursorOpenerCount - 1);
            _eventSystem = null;
            Selected = null;
            DrifterBossGrabPlugin.Instance?.Config.Save();
        }

        private void OnDestroy()
        {
            PluginConfig.Instance.IsHudEditorEnabled.SettingChanged -= OnConfigToggleChanged;
            if (_instance != this) return;
            if (IsEditorActive) Close();
            IsEditorActive = false;
            _instance = null;
        }
    }
}
