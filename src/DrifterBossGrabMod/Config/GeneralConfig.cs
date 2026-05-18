#nullable enable
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitGeneralConfig(ConfigFile cfg)
        {
            Instance.SelectedPreset = cfg.Bind("General", "SelectedPreset", PresetType.Intended,
                "Preset to load. Changes are auto-applied.");

            Instance.LastSelectedPreset = cfg.Bind("Hidden", "LastSelectedPreset", PresetType.Intended,
                "Internal tracker of the last applied preset.");

            Instance.EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Allow grabbing bosses.");
            Instance.EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Allow grabbing normally-ungrabbable NPCs.");
            Instance.EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Allow grabbing environment objects.");
            Instance.EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", false, "Allow grabbing locked objects.");
            Instance.ProjectileGrabbingMode = cfg.Bind("General", "ProjectileGrabbingMode", DrifterBossGrabMod.ProjectileGrabbingMode.None, "Projectile grab mode.");
            Instance.EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Log grab mechanics for debugging.");
            Instance.BodyBlacklist = cfg.Bind("General", "Blacklist", "HeaterPodBodyNoRespawn,ThrownObjectProjectile,ThrownObjectProjectileNoStun,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                "Bodies and projectiles to never grab. Comma-separated.");
            Instance.RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Objects to never recover from the abyss. Comma-separated.");
            Instance.GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                "Component type names that make objects grabbable. Comma-separated.");
            Instance.GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Keywords that prevent grabbing if found in name. Comma-separated.");
            Instance.ComponentChooserSortModeEntry = cfg.Bind("Hidden", "ComponentChooserSortMode", ComponentChooserSortMode.ByFrequency,
                "How to sort components in the UI.");
            Instance.ComponentChooserDummyEntry = cfg.Bind("Hidden", "ComponentChooserDummy", ComponentChooserDummy.SelectToToggle,
                "Dummy setting for UI.");
            Instance.EnableConfigSync = cfg.Bind("General", "EnableConfigSync", true,
                "Sync configuration from host to clients.");
            Instance.SearchRadiusMultiplier = cfg.Bind("Balance", "SearchRadiusMultiplier", 1.0f, "Multiplier for grab reach distance.");
        }
    }
}
