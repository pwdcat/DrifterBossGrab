#nullable enable
using UnityEngine;
using RoR2;
using RoR2.UI;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod.UI
{
    public class HudEditorManager : MonoBehaviour
    {
        public static bool IsEditorActive { get; private set; } = false;
        private static HudEditorManager? _instance;

        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            PluginConfig.Instance.IsHudEditorEnabled.SettingChanged += OnConfigToggleChanged;
        }

        private void OnConfigToggleChanged(object sender, System.EventArgs e)
        {
            SetEditorActive(PluginConfig.Instance.IsHudEditorEnabled.Value);
        }

        private void Update()
        {
            if (IsEditorActive && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                SetEditorActive(false);
            }
        }

        public static void ToggleEditor()
        {
            PluginConfig.Instance.IsHudEditorEnabled.Value = !PluginConfig.Instance.IsHudEditorEnabled.Value;
        }

        public static void SetEditorActive(bool active)
        {
            if (IsEditorActive == active) return;
            IsEditorActive = active;

            if (PluginConfig.Instance.IsHudEditorEnabled.Value != active)
            {
                PluginConfig.Instance.IsHudEditorEnabled.Value = active;
            }

            if (active)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;

                Chat.AddMessage("<color=#EFD27F>[HUD Editor] Enabled.</color> Drag elements to move, Scroll to resize. Press ESC to exit.");

                var pauseMenu = UnityEngine.Object.FindAnyObjectByType<PauseScreenController>();
                if (pauseMenu != null)
                {
                    var canvasGroup = pauseMenu.GetComponent<CanvasGroup>();
                    if (canvasGroup == null) canvasGroup = pauseMenu.gameObject.AddComponent<CanvasGroup>();

                    canvasGroup.alpha = 0f;
                    canvasGroup.blocksRaycasts = false;
                    canvasGroup.interactable = false;

                    var canvas = pauseMenu.GetComponentInChildren<Canvas>();
                    if (canvas != null) canvas.enabled = false;
                }
            }
            else
            {

                var pauseMenu = UnityEngine.Object.FindAnyObjectByType<PauseScreenController>();
                if (pauseMenu != null)
                {
                    var canvasGroup = pauseMenu.GetComponent<CanvasGroup>();
                    if (canvasGroup != null)
                    {
                        canvasGroup.alpha = 1f;
                        canvasGroup.blocksRaycasts = true;
                        canvasGroup.interactable = true;
                    }

                    var canvas = pauseMenu.GetComponentInChildren<Canvas>();
                    if (canvas != null) canvas.enabled = true;
                }

                Chat.AddMessage("<color=#EFD27F>[HUD Editor] Disabled. Changes saved.</color>");
            }
        }

        private void OnDestroy()
        {
            if (PluginConfig.Instance != null && PluginConfig.Instance.IsHudEditorEnabled != null)
            {
                PluginConfig.Instance.IsHudEditorEnabled.SettingChanged -= OnConfigToggleChanged;
            }
        }
    }
}
