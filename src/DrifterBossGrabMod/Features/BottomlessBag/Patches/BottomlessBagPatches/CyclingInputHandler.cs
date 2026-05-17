#nullable enable
using System;
using UnityEngine;
using RoR2;
using RoR2.UI;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{

    public static class CyclingInputHandler
    {
        private static float _lastCycleTime = 0f;
        private static float _scrollAccumulator = 0f;
        private const float SCROLL_THRESHOLD = 0.1f;
        private static DrifterBagController? _cachedLocalController;

        public static void HandleInput()
        {

            if (!PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                return;
            }

            if (!CanProcessInput())
            {
                _scrollAccumulator = 0f;
                return;
            }
            int cycleAmount = 0;

            if (PluginConfig.Instance.EnableMouseWheelScrolling.Value)
            {
                float scrollDelta = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
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

                    bool isMovingForward = _scrollAccumulator > 0f;
                    bool up;
                    if (isMovingForward)
                    {
                        if (PluginConfig.Instance.InverseMouseWheelScrolling.Value) up = true;
                        else up = false;
                    }
                    else
                    {
                        if (PluginConfig.Instance.InverseMouseWheelScrolling.Value) up = false;
                        else up = true;
                    }

                    cycleAmount = up ? 1 : -1;
                    _scrollAccumulator -= Mathf.Sign(_scrollAccumulator) * SCROLL_THRESHOLD;
                    _lastCycleTime = Time.time;
                }
            }

            if (Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
            {
                var inputLocalUser = LocalUserManager.GetFirstLocalUser();
                if (inputLocalUser?.inputPlayer != null)
                {
                    if (inputLocalUser.inputPlayer.GetButtonDown(DrifterBossGrabMod.Input.RewiredActions.ScrollBagUp.ActionId))
                    {
                        cycleAmount--;
                        _lastCycleTime = Time.time;
                    }
                    if (inputLocalUser.inputPlayer.GetButtonDown(DrifterBossGrabMod.Input.RewiredActions.ScrollBagDown.ActionId))
                    {
                        cycleAmount++;
                        _lastCycleTime = Time.time;
                    }
                }
            }

            if (cycleAmount != 0)
            {
                CyclePassengers(cycleAmount);
            }
        }

        private static bool CanProcessInput()
        {
            if (PauseScreenController.instancesList.Count > 0) return false;

            var localUser = LocalUserManager.GetFirstLocalUser();
            if (localUser != null && localUser.eventSystem && localUser.eventSystem.isCursorVisible) return false;

            if (Run.instance == null) return false;

            return true;
        }

        private static DrifterBagController? FindLocalPlayerBagController()
        {
            var localUser = LocalUserManager.GetFirstLocalUser();
            if (localUser?.cachedBody == null) return null;

            var body = localUser.cachedBody;

            var controller = body.GetComponent<DrifterBagController>();
            if (controller != null) return controller;

            controller = body.GetComponentInChildren<DrifterBagController>();
            if (controller != null) return controller;

            return null;
        }

        private static void CyclePassengers(int amount)
        {
            if (amount == 0) return;

            if (_cachedLocalController != null && _cachedLocalController.isAuthority)
            {
                PassengerCycler.CyclePassengers(_cachedLocalController, amount);
                return;
            }

            var controller = FindLocalPlayerBagController();
            if (controller != null && controller.isAuthority)
            {
                _cachedLocalController = controller;
                PassengerCycler.CyclePassengers(controller, amount);
            }
        }
    }
}
