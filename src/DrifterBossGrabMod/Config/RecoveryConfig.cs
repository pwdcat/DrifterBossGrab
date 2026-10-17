#nullable enable
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitRecoveryConfig(ConfigFile cfg)
        {
            Instance.EnableRecoveryFeature = cfg.Bind("Recovery", "EnableRecoveryFeature", false, "Recover bagged objects that fall off the map.");
            Instance.EnemyRecoveryMode = cfg.Bind("Recovery", "EnemyRecoveryMode", DrifterBossGrabMod.EnemyRecoveryMode.Kill, "Choose what happens to bagged enemies that fall off the map.");
            Instance.RecoverBaggedBosses = cfg.Bind("Recovery", "RecoverBaggedBosses", false, "Recover bagged bosses that fall off the map.");
            Instance.RecoverBaggedNPCs = cfg.Bind("Recovery", "RecoverBaggedNPCs", false, "Recover bagged NPCs that fall off the map.");
            Instance.RecoverBaggedEnvironmentObjects = cfg.Bind("Recovery", "RecoverBaggedEnvironmentObjects", false, "Recover bagged environment objects that fall off the map.");
        }
    }
}
