#nullable enable
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitRecoveryConfig(ConfigFile cfg)
        {
            Instance.EnableRecoveryFeature = cfg.Bind("Recovery", "EnableRecoveryFeature", true, "Return bagged items that fall off the map.");
            Instance.EnemyRecoveryMode = cfg.Bind("Recovery", "EnemyRecoveryMode", DrifterBossGrabMod.EnemyRecoveryMode.Recover, "Behavior for bagged enemies falling off the map.");
            Instance.RecoverBaggedBosses = cfg.Bind("Recovery", "RecoverBaggedBosses", true, "Recover bagged bosses from the abyss.");
            Instance.RecoverBaggedNPCs = cfg.Bind("Recovery", "RecoverBaggedNPCs", true, "Recover bagged NPCs from the abyss.");
            Instance.RecoverBaggedEnvironmentObjects = cfg.Bind("Recovery", "RecoverBaggedEnvironmentObjects", true, "Recover bagged environment objects from the abyss.");
        }
    }
}
