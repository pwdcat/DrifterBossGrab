#nullable enable
using BepInEx.Configuration;
using UnityEngine;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod
{
    public partial class PluginConfig
    {
        private static void InitCharacterFlagsConfig(ConfigFile cfg)
        {
            Instance.EliteFlagMultiplier = cfg.Bind("Character Flags", "EliteFlagMultiplier", "1", "Mass multiplier for Elite entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.EliteFlagMultiplier.Value = "1";

            Instance.BossFlagMultiplier = cfg.Bind("Character Flags", "BossFlagMultiplier", "1", "Mass multiplier for Boss entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.BossFlagMultiplier.Value = "1";

            Instance.ChampionFlagMultiplier = cfg.Bind("Character Flags", "ChampionFlagMultiplier", "1", "Mass multiplier for Champion entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.ChampionFlagMultiplier.Value = "1";

            Instance.PlayerFlagMultiplier = cfg.Bind("Character Flags", "PlayerFlagMultiplier", "1", "Mass multiplier for Player entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.PlayerFlagMultiplier.Value = "1";

            Instance.MinionFlagMultiplier = cfg.Bind("Character Flags", "MinionFlagMultiplier", "1", "Mass multiplier for Minion entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.MinionFlagMultiplier.Value = "1";

            Instance.DroneFlagMultiplier = cfg.Bind("Character Flags", "DroneFlagMultiplier", "1", "Mass multiplier for Drone entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.DroneFlagMultiplier.Value = "1";

            Instance.MechanicalFlagMultiplier = cfg.Bind("Character Flags", "MechanicalFlagMultiplier", "1", "Mass multiplier for Mechanical entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.MechanicalFlagMultiplier.Value = "1";

            Instance.VoidFlagMultiplier = cfg.Bind("Character Flags", "VoidFlagMultiplier", "1", "Mass multiplier for Void entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.VoidFlagMultiplier.Value = "1";

            Instance.AllFlagMultiplier = cfg.Bind(
                new ConfigDefinition("Character Flags", "all Flag Multiplier"),
                "1",
                new ConfigDescription("Universal multiplier for all enemies. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).")
            );

            Instance.SelectedFlag = cfg.Bind("Hidden", "SelectedFlag", CharacterFlagType.All,
                "Select which flag to modify.");
            Instance.SelectedFlag.Value = CharacterFlagType.All;
            Instance.SelectedFlagMultiplier = cfg.Bind("Hidden", "FlagMultiplier", "1",
                "Mass multiplier for selected flag.");
            Instance.SelectedFlagMultiplier.Value = "1";

            Instance.SelectedBalanceSubTab = cfg.Bind("Hidden", "SelectedBalanceSubTab", BalanceSubTabType.All,
                "Select which Balance settings group to view.");
            Instance.SelectedBalanceSubTab.Value = BalanceSubTabType.All;

            WireCharacterFlagEventHandlers();
        }

        private static void WireCharacterFlagEventHandlers()
        {
            Instance.EliteFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.EliteFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid EliteFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.BossFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.BossFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid BossFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.ChampionFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.ChampionFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid ChampionFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.PlayerFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.PlayerFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid PlayerFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MinionFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.MinionFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MinionFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.DroneFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.DroneFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid DroneFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MechanicalFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.MechanicalFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MechanicalFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.VoidFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.VoidFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid VoidFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.SelectedFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.SelectedFlagMultiplier.Value);
                if (error != null)
                {
                    Log.Warning($"[PluginConfig] Invalid FlagMultiplier formula: {error}");
                    return;
                }

                var selectedFlag = Instance.SelectedFlag.Value;
                string newFormula = Instance.SelectedFlagMultiplier.Value;

                switch (selectedFlag)
                {
                    case CharacterFlagType.Elite:
                        Instance.EliteFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Boss:
                        Instance.BossFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Champion:
                        Instance.ChampionFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Player:
                        Instance.PlayerFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Minion:
                        Instance.MinionFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Drone:
                        Instance.DroneFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Mechanical:
                        Instance.MechanicalFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Void:
                        Instance.VoidFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.All:
                        Instance.AllFlagMultiplier.Value = newFormula;
                        break;
                }
            };

            Instance.SelectedFlag.SettingChanged += (sender, args) =>
            {
                var selectedFlag = Instance.SelectedFlag.Value;
                string currentFormula = "0";

                switch (selectedFlag)
                {
                    case CharacterFlagType.Elite:
                        currentFormula = Instance.EliteFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Boss:
                        currentFormula = Instance.BossFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Champion:
                        currentFormula = Instance.ChampionFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Player:
                        currentFormula = Instance.PlayerFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Minion:
                        currentFormula = Instance.MinionFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Drone:
                        currentFormula = Instance.DroneFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Mechanical:
                        currentFormula = Instance.MechanicalFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Void:
                        currentFormula = Instance.VoidFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.All:
                        currentFormula = Instance.AllFlagMultiplier.Value;
                        break;
                }

                Instance.SelectedFlagMultiplier.Value = currentFormula;
            };
        }
    }
}
