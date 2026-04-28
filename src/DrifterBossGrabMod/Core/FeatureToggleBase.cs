#nullable enable
using System;
using HarmonyLib;

namespace DrifterBossGrabMod
{
    public abstract class FeatureToggleBase
    {
        public abstract string FeatureName { get; }
        public abstract bool IsEnabled { get; }
        public Harmony Harmony { get; }

        protected abstract void ApplyPatches(Harmony harmony);

        private bool _isPatchesApplied = false;

        protected FeatureToggleBase()
        {
            Harmony = new Harmony(Constants.PluginGuid + "." + FeatureName.ToLowerInvariant());
        }

        public virtual void Initialize()
        {
            if (IsEnabled)
            {
                ApplyPatches(Harmony);
                _isPatchesApplied = true;
            }
        }

        public virtual void Cleanup()
        {
            Harmony.UnpatchSelf();
            _isPatchesApplied = false;
        }

        public void Enable()
        {
            if (!IsEnabled || _isPatchesApplied)
            {
                return;
            }

            ApplyPatches(Harmony);
            _isPatchesApplied = true;
        }

        public void Disable()
        {
            Cleanup();
        }

        public void Toggle(bool enable)
        {
            if (enable)
            {
                Enable();
            }
            else
            {
                Disable();
            }
        }
    }
}
