#nullable enable
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitGeneralConfig(ConfigFile cfg)
        {
            Instance.SelectedPreset = cfg.Bind("General", "SelectedPreset", PresetType.Intended,
                "Apply a preset when selected.");

            Instance.LastSelectedPreset = cfg.Bind("Hidden", "LastSelectedPreset", (PresetType)(-1),
                "Last applied preset.");

            Instance.EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Allow grabbing bosses.");
            Instance.EnableSelfGrabbing = cfg.Bind("General", "EnableSelfGrabbing", false, "Look up at least 68 degrees and double-tap Repossess within 0.3 seconds to grab yourself.");
            Instance.EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Allow grabbing NPCs that cannot normally be grabbed.");
            Instance.EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Allow grabbing environment objects.");
            Instance.EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", false, "Allow grabbing locked objects.");
            Instance.ProjectileGrabbingMode = cfg.Bind("General", "ProjectileGrabbingMode", DrifterBossGrabMod.ProjectileGrabbingMode.None, "Choose which projectiles you can grab.");
            Instance.EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Write extra logs for debugging.");
            Instance.BodyBlacklist = cfg.Bind("General", "Blacklist", "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang,GhostBladesProjectile,LunarSunProjectile",
                "Bodies and projectiles to never grab. Comma-separated.");
            Instance.RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Objects to never recover from the abyss. Comma-separated.");
            Instance.GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                "Component type names that make objects grabbable. Comma-separated.");
            Instance.GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Block objects whose names contain these keywords. Comma-separated.");
            Instance.ComponentChooserSortModeEntry = cfg.Bind("Hidden", "ComponentChooserSortMode", ComponentChooserSortMode.ByFrequency,
                "How to sort components in the UI.");
            Instance.ComponentChooserDummyEntry = cfg.Bind("Hidden", "ComponentChooserDummy", ComponentChooserDummy.SelectToToggle,
                "Dummy value");
            Instance.EnableConfigSync = cfg.Bind("General", "EnableConfigSync", true,
                "Use the host's gameplay settings for all players.");
        }
    }
}
