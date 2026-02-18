using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
    public static class ConfigChangeNotifier
    {
        private static readonly List<IConfigObserver> observers = new();
        private static readonly EventHandler configChangedHandler = OnConfigChanged;

        public static void AddObserver(IConfigObserver observer)
        {
            observers.Add(observer);
        }

        public static void RemoveObserver(IConfigObserver observer)
        {
            observers.Remove(observer);
        }

        private static void OnConfigChanged(object sender, EventArgs e)
        {
            var config = (ConfigEntryBase)sender;
            NotifyObservers(config.Definition.Key, config.BoxedValue);
        }

        private static void NotifyObservers(string key, object value)
        {
            foreach (var observer in observers)
            {
                observer.OnConfigChanged(key, value);
            }
        }

        public static void Init()
        {
            // Subscribe to all config entries
            PluginConfig.Instance.SearchRangeMultiplier.SettingChanged += configChangedHandler;
            PluginConfig.Instance.BreakoutTimeMultiplier.SettingChanged += configChangedHandler;
            PluginConfig.Instance.ForwardVelocityMultiplier.SettingChanged += configChangedHandler;
            PluginConfig.Instance.UpwardVelocityMultiplier.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableBossGrabbing.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableNPCGrabbing.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableEnvironmentGrabbing.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableLockedObjectGrabbing.SettingChanged += configChangedHandler;
            PluginConfig.Instance.ProjectileGrabbingMode.SettingChanged += configChangedHandler;
            PluginConfig.Instance.MaxSmacks.SettingChanged += configChangedHandler;
            PluginConfig.Instance.MassMultiplier.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableDebugLogs.SettingChanged += configChangedHandler;
            PluginConfig.Instance.BodyBlacklist.SettingChanged += configChangedHandler;
            PluginConfig.Instance.RecoveryObjectBlacklist.SettingChanged += configChangedHandler;
            PluginConfig.Instance.GrabbableComponentTypes.SettingChanged += configChangedHandler;
            PluginConfig.Instance.GrabbableKeywordBlacklist.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableComponentAnalysisLogs.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableAutoGrab.SettingChanged += configChangedHandler;
            PluginConfig.Instance.PersistBaggedBosses.SettingChanged += configChangedHandler;
            PluginConfig.Instance.PersistBaggedNPCs.SettingChanged += configChangedHandler;
            PluginConfig.Instance.PersistBaggedEnvironmentObjects.SettingChanged += configChangedHandler;
            PluginConfig.Instance.PersistenceBlacklist.SettingChanged += configChangedHandler;
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged += configChangedHandler;
            PluginConfig.Instance.BottomlessBagBaseCapacity.SettingChanged += configChangedHandler;
            PluginConfig.Instance.EnableMouseWheelScrolling.SettingChanged += configChangedHandler;
            PluginConfig.Instance.ScrollUpKeybind.SettingChanged += configChangedHandler;
            PluginConfig.Instance.ScrollDownKeybind.SettingChanged += configChangedHandler;
            PluginConfig.Instance.CarouselSpacing.SettingChanged += configChangedHandler;
            PluginConfig.Instance.CarouselSideScale.SettingChanged += configChangedHandler;
            PluginConfig.Instance.CarouselSideOpacity.SettingChanged += configChangedHandler;
        }

        public static void Cleanup()
        {
            // Unsubscribe from all config entries
            PluginConfig.Instance.SearchRangeMultiplier.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.BreakoutTimeMultiplier.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.ForwardVelocityMultiplier.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.UpwardVelocityMultiplier.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableBossGrabbing.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableNPCGrabbing.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableEnvironmentGrabbing.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableLockedObjectGrabbing.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.ProjectileGrabbingMode.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.MaxSmacks.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.MassMultiplier.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableDebugLogs.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.BodyBlacklist.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.RecoveryObjectBlacklist.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.GrabbableComponentTypes.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.GrabbableKeywordBlacklist.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableComponentAnalysisLogs.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableAutoGrab.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.PersistBaggedBosses.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.PersistBaggedNPCs.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.PersistBaggedEnvironmentObjects.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.PersistenceBlacklist.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.BottomlessBagBaseCapacity.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.EnableMouseWheelScrolling.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.ScrollUpKeybind.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.ScrollDownKeybind.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.CarouselSpacing.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.CarouselSideScale.SettingChanged -= configChangedHandler;
            PluginConfig.Instance.CarouselSideOpacity.SettingChanged -= configChangedHandler;
        }
    }
}
