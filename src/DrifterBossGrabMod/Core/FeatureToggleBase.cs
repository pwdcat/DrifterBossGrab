using System;
using HarmonyLib;
using DrifterBossGrabMod.Interfaces;

namespace DrifterBossGrabMod
{
    public abstract class FeatureToggleBase : IFeatureToggle
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
            Log.Info($"[{FeatureName}] Cleaning up patches...");
            // Unpatch all methods from this Harmony instance
            // This is safe because each feature should have its own Harmony instance
            harmony.UnpatchSelf();
            _isPatchesApplied = false;
            Log.Info($"[{FeatureName}] Cleanup complete.");
        }

        public void Enable(Harmony harmony)
        {
            if (!IsEnabled)
            {
                Log.Info($"[{FeatureName}] Enable called but IsEnabled is false. Aborting.");
                return;
            }

            if (_isPatchesApplied)
            {
                Log.Info($"[{FeatureName}] Patches already applied, skipping.");
                return;
            }

            Log.Info($"[{FeatureName}] Enabling feature and applying patches...");
            ApplyPatches(harmony);
            _isPatchesApplied = true;
        }

        public void Disable(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Disabling feature...");
            Cleanup(harmony);
        }

        public void Toggle(Harmony harmony, bool enable)
        {
            Log.Info($"[{FeatureName}] Toggle called with enable={enable}");
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
