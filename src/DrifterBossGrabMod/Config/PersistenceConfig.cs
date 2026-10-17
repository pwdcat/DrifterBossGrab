#nullable enable
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitPersistenceConfig(ConfigFile cfg)
        {
            Instance.EnableObjectPersistence = cfg.Bind("Persistence", "EnableObjectPersistence",
                false,
                "Keep bagged objects between stages.");
            Instance.EnableAutoGrab = cfg.Bind("Persistence", "EnableAutoGrab",
                false,
                "Automatically grab saved objects when a stage starts.");
            Instance.PersistBaggedBosses = cfg.Bind("Persistence", "PersistBaggedBosses",
                true,
                "Keep bagged bosses between stages.");
            Instance.PersistBaggedNPCs = cfg.Bind("Persistence", "PersistBaggedNPCs",
                true,
                "Keep bagged NPCs between stages.");
            Instance.PersistBaggedEnvironmentObjects = cfg.Bind("Persistence", "PersistBaggedEnvironmentObjects",
                true,
                "Keep bagged environment objects between stages.");
            Instance.PersistenceBlacklist = cfg.Bind("Persistence", "PersistenceBlacklist", "",
                "Objects to leave behind when changing stages. Comma-separated.");
            Instance.AutoGrabDelay = cfg.Bind("Persistence", "AutoGrabDelay", 1.0f, "Seconds to wait before automatically grabbing saved objects.");
        }
    }
}
