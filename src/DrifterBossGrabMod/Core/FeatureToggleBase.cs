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
        
        public void Initialize(Harmony harmony)
        {
            if (IsEnabled)
            {
                ApplyPatches(harmony);
            }
        }
        
        public virtual void Cleanup(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Cleaning up patches...");
            // Unpatch all methods
            harmony.UnpatchSelf();
        }

        public void Enable(Harmony harmony)
        {
            if (!IsEnabled) 
            {
                Log.Info($"[{FeatureName}] Enable called but IsEnabled is false. Aborting.");
                return;
            }
            Log.Info($"[{FeatureName}] Enabling feature and applying patches...");
            ApplyPatches(harmony);
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