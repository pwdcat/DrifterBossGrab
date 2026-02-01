using System;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod
{
    public static class FeatureState
    {
        public static bool IsCyclingEnabled
        {
            get
            {
                return PluginConfig.Instance.BottomlessBagEnabled.Value;
            }
        }
    }
}
