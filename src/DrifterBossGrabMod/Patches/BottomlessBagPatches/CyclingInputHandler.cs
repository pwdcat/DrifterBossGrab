using System;
using UnityEngine;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    // Handles input processing for cycling through bag passengers - manages mouse wheel scrolling with thresholding and keybind handling
    public static class CyclingInputHandler
    {
        private static float _lastCycleTime = 0f;
        private static float _scrollAccumulator = 0f;
        private const float SCROLL_THRESHOLD = 0.1f; // Cumulative delta required to trigger one scroll event

        // Processes input for cycling through bag passengers - handles mouse wheel scrolling and keybind inputs
        public static void HandleInput()
        {
            // Only process cycling input when BottomlessBag feature is enabled
            if (!FeatureState.IsCyclingEnabled)
            {
                return;
            }
            {
                // Check if local player is in Repossess or AimRepossess state - prevent cycling while grabbing or aiming
                var localUser = LocalUserManager.GetFirstLocalUser();
                if (localUser != null && localUser.cachedBody != null)
                {
                    bool isBlockingState = false;
                    var stateMachines = localUser.cachedBody.GetComponents<EntityStateMachine>();
                    foreach (var stateMachine in stateMachines)
                    {
                        if (stateMachine != null)
                        {
                            if (stateMachine.state is EntityStates.Drifter.Repossess || stateMachine.state is EntityStates.Drifter.RepossessExit)
                            {
                                isBlockingState = true;
                            }
                        }
                    }

                    if (isBlockingState) return;
                }
                int cycleAmount = 0;

                // 1. Handle Mouse Wheel with Thresholding
                if (PluginConfig.Instance.EnableMouseWheelScrolling.Value)
                {
                    float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
                    if (scrollDelta != 0f)
                    {
                        if (_scrollAccumulator != 0f && Mathf.Sign(scrollDelta) != Mathf.Sign(_scrollAccumulator))
                        {
                            _scrollAccumulator = 0f;
                        }
                        _scrollAccumulator += scrollDelta;
                    }
                    else
                    {
                        _scrollAccumulator = Mathf.MoveTowards(_scrollAccumulator, 0f, Time.deltaTime * 0.5f);
                    }

                    if (Mathf.Abs(_scrollAccumulator) >= SCROLL_THRESHOLD && Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
                    {
                        // Trigger only one cycle per cooldown period to prevent skipping items
                        bool isMovingForward = _scrollAccumulator > 0f;
                        bool up;
                        if (isMovingForward)
                        {
                            if (PluginConfig.Instance.InverseMouseWheelScrolling.Value) up = true; // scrollUp (inverted from new default)
                            else up = false; // scrollDown (new default)
                        }
                        else
                        {
                            if (PluginConfig.Instance.InverseMouseWheelScrolling.Value) up = false; // scrollDown (inverted from new default)
                            else up = true; // scrollUp (new default)
                        }

                        cycleAmount = up ? 1 : -1;
                        _scrollAccumulator -= Mathf.Sign(_scrollAccumulator) * SCROLL_THRESHOLD;
                        _lastCycleTime = Time.time;
                    }
                }

                // 2. Handle Keybinds (Always overrides/adds to threshold if pressed)
                if (Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
                {
                    if (PluginConfig.Instance.ScrollUpKeybind.Value.MainKey != KeyCode.None && Input.GetKeyDown(PluginConfig.Instance.ScrollUpKeybind.Value.MainKey))
                    {
                        bool modifiersPressed = true;
                        foreach (var modifier in PluginConfig.Instance.ScrollUpKeybind.Value.Modifiers)
                        {
                            if (!Input.GetKey(modifier)) { modifiersPressed = false; break; }
                        }
                        if (modifiersPressed) { cycleAmount++; _lastCycleTime = Time.time; }
                    }
                    if (PluginConfig.Instance.ScrollDownKeybind.Value.MainKey != KeyCode.None && Input.GetKeyDown(PluginConfig.Instance.ScrollDownKeybind.Value.MainKey))
                    {
                        bool modifiersPressed = true;
                        foreach (var modifier in PluginConfig.Instance.ScrollDownKeybind.Value.Modifiers)
                        {
                            if (!Input.GetKey(modifier)) { modifiersPressed = false; break; }
                        }
                        if (modifiersPressed) { cycleAmount--; _lastCycleTime = Time.time; }
                    }
                }

                // 3. Execute Cycle
                if (cycleAmount != 0)
                {

                    CyclePassengers(cycleAmount);
                }
            }
        }

        // Private method to cycle passengers by amount on all authoritative bag controllers
        private static void CyclePassengers(int amount)
        {
            if (amount == 0) return;
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);

            foreach (var bagController in bagControllers)
            {

                if (!bagController.isAuthority)
                {
                    continue;
                }

                PassengerCycler.CyclePassengers(bagController, amount);
                break;
            }
        }
    }
}
