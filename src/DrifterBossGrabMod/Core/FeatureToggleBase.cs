#nullable enable
using System;
using HarmonyLib;

namespace DrifterBossGrabMod
{
    public abstract class FeatureToggleBase
    {
        public abstract string FeatureName { get; }
        public abstract bool IsEnabled { get; }

        protected abstract void ApplyPatches(Harmony harmony);

        private bool _isPatchesApplied = false;

        public void Initialize(Harmony harmony)
        {
            if (IsEnabled)
            {
                ApplyPatches(harmony);
                _isPatchesApplied = true;
            }
        }

        public virtual void Cleanup(Harmony harmony)
        {
            harmony.UnpatchSelf();
            _isPatchesApplied = false;
        }

        public void Enable(Harmony harmony)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (_isPatchesApplied)
            {
                return;
            }

            ApplyPatches(harmony);
            _isPatchesApplied = true;
        }

        public void Disable(Harmony harmony)
        {
            Cleanup(harmony);
        }

        public void Toggle(Harmony harmony, bool enable)
        {
            if (enable)
            {
                Enable(harmony);
            }
            else
            {
                Disable(harmony);
            }
        }
    }
}
