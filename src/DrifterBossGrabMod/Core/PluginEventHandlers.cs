#nullable enable
using System;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    public partial class DrifterBossGrabPlugin
    {
        private EventHandler? debugLogsHandler;
        private EventHandler? blacklistHandler;
        private EventHandler? recoveryBlacklistHandler;
        private EventHandler? grabbableComponentTypesHandler;
        private EventHandler? grabbableKeywordBlacklistHandler;
        private EventHandler? bossGrabbingHandler;
        private EventHandler? npcGrabbingHandler;
        private EventHandler? environmentGrabbingHandler;
        private EventHandler? lockedObjectGrabbingHandler;
        private EventHandler? projectileGrabbingModeHandler;
        private EventHandler? persistenceHandler;
        private EventHandler? autoGrabHandler;
        private EventHandler? bottomlessBagToggleHandler;
        private EventHandler? persistenceToggleHandler;
        private EventHandler? balanceToggleHandler;

        private void OnConfigSettingChangedEvent(object sender, SettingChangedEventArgs args)
        {
            if (NetworkServer.active)
            {
                Networking.ConfigSyncHandler.BroadcastConfigToClients();
            }
        }

        private void OnClientPreferenceSettingChanged(object sender, EventArgs args)
        {
            if (!NetworkClient.active) return;
            
            foreach (var playerController in PlayerCharacterMasterController.instances)
            {
                if (playerController.hasAuthority && playerController.master && playerController.master.GetBody())
                {
                    var controller = playerController.master.GetBody().GetComponent<DrifterBagController>();
                    if (controller)
                    {
                        var netCtrl = controller.GetComponent<Networking.BottomlessBagNetworkController>();
                        if (netCtrl && netCtrl.hasAuthority)
                        {
                            var ni = netCtrl.GetComponent<NetworkIdentity>();
                            if (ni != null)
                            {
                                Networking.CycleNetworkHandler.SendClientPreferences(
                                    ni,
                                    PluginConfig.Instance.AutoPromoteMainSeat.Value,
                                    PluginConfig.Instance.PrioritizeMainSeat.Value
                                );
                            }
                        }
                    }
                }
            }
        }

        private void SetupFeatureToggleHandlers()
        {
            bottomlessBagToggleHandler = (sender, args) =>
            {
                bool isEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value;
                if (isEnabled != _wasBottomlessBagEnabled)
                {
                    _bottomlessBagFeature?.Toggle(_bottomlessBagHarmony!, isEnabled);
                    _wasBottomlessBagEnabled = isEnabled;
                }
            };
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged += bottomlessBagToggleHandler;

            persistenceToggleHandler = (sender, args) =>
            {
                bool isEnabled = PluginConfig.Instance.EnableObjectPersistence.Value;
                if (isEnabled != _wasPersistenceEnabled)
                {
                    _persistenceFeature?.Toggle(_persistenceHarmony!, isEnabled);
                    _wasPersistenceEnabled = isEnabled;
                }
            };
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged += persistenceToggleHandler;

            balanceToggleHandler = (sender, args) =>
            {
                bool isEnabled = PluginConfig.Instance.EnableBalance.Value;
                if (isEnabled != _wasBalanceEnabled)
                {
                    _balanceFeature?.Toggle(_balanceHarmony!, isEnabled);
                    _wasBalanceEnabled = isEnabled;
                }
            };
            PluginConfig.Instance.EnableBalance.SettingChanged += balanceToggleHandler;
        }

        private void SetupClientPreferenceHandlers()
        {
            PluginConfig.Instance.AutoPromoteMainSeat.SettingChanged += OnClientPreferenceSettingChanged;
            PluginConfig.Instance.PrioritizeMainSeat.SettingChanged += OnClientPreferenceSettingChanged;
        }

        private void RemoveConfigurationEventHandlers()
        {
            Config.SettingChanged -= OnConfigSettingChangedEvent;
            PluginConfig.RemoveEventHandlers(
                debugLogsHandler ?? ((sender, args) => { }),
                blacklistHandler ?? ((sender, args) => { }),
                recoveryBlacklistHandler ?? ((sender, args) => { }),
                grabbableComponentTypesHandler ?? ((sender, args) => { }),
                grabbableKeywordBlacklistHandler ?? ((sender, args) => { }),
                bossGrabbingHandler ?? ((sender, args) => { }),
                npcGrabbingHandler ?? ((sender, args) => { }),
                environmentGrabbingHandler ?? ((sender, args) => { }),
                lockedObjectGrabbingHandler ?? ((sender, args) => { }),
                projectileGrabbingModeHandler ?? ((sender, args) => { })
            );
        }

        private void RemovePersistenceEventHandlers()
        {
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged -= persistenceHandler;
            PluginConfig.Instance.EnableAutoGrab.SettingChanged -= autoGrabHandler;
        }

        private void RemoveFeatureToggleHandlers()
        {
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged -= bottomlessBagToggleHandler;
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged -= persistenceToggleHandler;
            PluginConfig.Instance.EnableBalance.SettingChanged -= balanceToggleHandler;
        }

        private void RemoveClientPreferenceHandlers()
        {
            PluginConfig.Instance.AutoPromoteMainSeat.SettingChanged -= OnClientPreferenceSettingChanged;
            PluginConfig.Instance.PrioritizeMainSeat.SettingChanged -= OnClientPreferenceSettingChanged;
        }

        private void OnAutoSwitchSettingChanged(object sender, EventArgs args)
        {
            PresetManager.OnSettingModified();
            PresetManager.RefreshPresetDropdownUI();
        }

        private void OnPresetOnlySettingChanged(object sender, EventArgs args)
        {
            PresetManager.OnSettingModified();
        }

        private void RegisterAutoSwitchHandlers(params object[] configEntries)
        {
            foreach (var config in configEntries)
            {
                var eventInfo = config.GetType().GetEvent("SettingChanged");
                if (eventInfo != null)
                {
                    var methodInfo = typeof(DrifterBossGrabPlugin).GetMethod(nameof(OnAutoSwitchSettingChanged), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, methodInfo);
                    eventInfo.AddEventHandler(config, handler);
                }
            }
        }

        private void RegisterPresetOnlyHandlers(params object[] configEntries)
        {
            foreach (var config in configEntries)
            {
                var eventInfo = config.GetType().GetEvent("SettingChanged");
                if (eventInfo != null)
                {
                    var methodInfo = typeof(DrifterBossGrabPlugin).GetMethod(nameof(OnPresetOnlySettingChanged), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, methodInfo);
                    eventInfo.AddEventHandler(config, handler);
                }
            }
        }
    }
}
