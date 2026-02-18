using HarmonyLib;

namespace DrifterBossGrabMod.Interfaces
{
    public interface IFeatureToggle
    {
        string FeatureName { get; }
        bool IsEnabled { get; }

        void Initialize(Harmony harmony);
        void Cleanup(Harmony harmony);
    }
}
