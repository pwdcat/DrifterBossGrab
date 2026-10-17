#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Config;
using DrifterBossGrabMod.Balance;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.API
{
    public enum EncumbranceLevel
    {
        None,
        Light,
        Heavy,
        Over
    }

    // ========================================================================================
    // DRIFTER BAG API
    // ========================================================================================
    public static class DrifterBagAPI
    {
        private static readonly List<GameObject> _queryBuffer = new List<GameObject>();
        private static readonly List<string> _detailsBuffer = new List<string>();
        private static readonly Dictionary<string, int> _countsBuffer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static List<GameObject> GetBaggedObjects(DrifterBagController controller)
        {
            if (controller == null) return new List<GameObject>();
            return new List<GameObject>(BagPatches.GetState(controller).BaggedObjects ?? new List<GameObject>());
        }

        public static IReadOnlyList<GameObject> GetBaggedObjectsReadOnly(DrifterBagController controller)
        {
            if (controller == null) return Array.Empty<GameObject>();
            var list = BagPatches.GetState(controller).BaggedObjects;
            return list ?? new List<GameObject>(Array.Empty<GameObject>());
        }

        public static void ForEachBaggedObject(DrifterBagController controller, Action<GameObject> action)
        {
            if (controller == null || action == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null) action(obj);
            }
        }

        public static void GetBaggedObjectsInto(DrifterBagController controller, List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null) buffer.Add(obj);
            }
        }

        public static int GetBagCount(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetCurrentBaggedCount(controller);
        }

        public static int GetBagCapacity(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetUtilityMaxStock(controller);
        }
        public static bool HasRoom(DrifterBagController controller)
        {
            return BagCapacityCalculator.HasRoomForGrab(controller);
        }

        public static float GetTotalMass(DrifterBagController controller)
        {
            return BagCapacityCalculator.GetBaggedObjectMass(controller);
        }

        public static float GetObjectMass(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return 0f;
            return controller.CalculateBaggedObjectMass(obj);
        }

        public static string GetObjectName(GameObject obj)
        {
            if (obj == null) return "Unknown";
            var body = obj.GetComponent<CharacterBody>();
            if (body != null) return body.GetDisplayName();
            return obj.name;
        }

        public static Texture? GetObjectIcon(GameObject obj)
        {
            if (obj == null) return null;
            var body = obj.GetComponent<CharacterBody>();
            if (body != null && body.portraitIcon != null) return body.portraitIcon;

            var attributes = obj.GetComponent<SpecialObjectAttributes>();
            if (attributes != null && attributes.portraitIcon != null) return attributes.portraitIcon;

            return null;
        }

        public static bool IsObjectInBag(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            var list = BagPatches.GetState(controller).BaggedObjects;
            return list != null && list.Contains(obj);
        }

        public static GameObject? GetMainPassenger(DrifterBagController controller)
        {
            return BagPatches.GetMainSeatObject(controller);
        }
        public static bool IsBlacklisted(string objectName)
        {
            return PluginConfig.IsBlacklisted(objectName);
        }

        public static bool SetMainPassenger(DrifterBagController controller, GameObject objRef)
        {
            if (controller == null || objRef == null) return false;

            var list = GetBaggedObjects(controller);
            if (!list.Contains(objRef)) return false;

            if (GetMainPassenger(controller) == objRef) return true;

            DelayedAutoPromote.Schedule(controller, objRef, 0f);
            return true;
        }

        public static bool AddBaggedObject(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
            controller.AssignPassenger(obj);
            if (BagPatches.GetMainSeatObject(controller) == obj)
            {
                var targetBody = controller.GetComponentInParent<CharacterBody>();
                if (targetBody != null)
                {
                    var bagStateMachine = EntityStateMachine.FindByCustomName(targetBody.gameObject, "Bag");
                    if (bagStateMachine != null)
                    {
                        Log.Debug($"[DrifterBagAPI] Setting BaggedObject state on {targetBody.name} for {obj.name}");
                        var baggedObjectState = new BaggedObject();
                        baggedObjectState.targetObject = obj;
                        bagStateMachine.SetNextState(baggedObjectState);
                    }
                }
            }

            return true;
        }

        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false)
        {
            if (controller == null || obj == null) return;
            BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying);
        }

        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;
            BagPassengerManager.ForceRecalculateMass(controller);
        }

        public static void ClearBag(DrifterBagController controller, bool isDestroying = false)
        {
            if (controller == null) return;
            var list = GetBaggedObjects(controller);
            foreach (var obj in list)
            {
                RemoveBaggedObject(controller, obj, isDestroying);
            }
            InvokeOnBagCleared(controller, isDestroying);
        }

        public static void ScheduleAutoGrab(DrifterBagController controller, GameObject obj, float delay = 0.5f)
        {
            if (controller == null || obj == null) return;
            var coroutineRunner = new GameObject("AutoGrabRunner_" + obj.GetInstanceID());
            var runner = coroutineRunner.AddComponent<AutoGrabCoroutineRunner>();
            runner.StartCoroutine(DelayedAutoGrabCoroutine(controller, obj, delay));
        }

        private static IEnumerator DelayedAutoGrabCoroutine(DrifterBagController controller, GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null && obj.activeInHierarchy)
            {
                AddBaggedObject(controller, obj);
            }
        }

        private class AutoGrabCoroutineRunner : MonoBehaviour
        {
            public IEnumerator? runningCoroutine;

            public new void StartCoroutine(IEnumerator coroutine)
            {
                runningCoroutine = coroutine;
                base.StartCoroutine(coroutine);
            }

            private void OnDestroy()
            {
                if (runningCoroutine != null)
                {
                    StopCoroutine(runningCoroutine);
                }
            }
        }

        // ========================================================================================
        // ENCUMBRANCE & STATUS
        // ========================================================================================
        public static float GetMassRatio(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            float totalMass = GetTotalMass(controller);
            float capacity = GetMaxMassCapacity(controller);
            if (capacity == float.MaxValue || capacity <= 0) return 0f;
            return totalMass / capacity;
        }

        public static float GetMassCapacity(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            return Balance.CapacityScalingSystem.CalculateMassCapacity(controller);
        }

        public static float GetMaxMassCapacity(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            return Balance.CapacityScalingSystem.CalculateMaxMassCapacity(controller);
        }

        public static EncumbranceLevel GetEncumbranceLevel(DrifterBagController controller)
        {
            float ratio = GetMassRatio(controller);
            if (ratio < 0.5f) return EncumbranceLevel.None;
            if (ratio < 0.75f) return EncumbranceLevel.Light;
            if (ratio < 1.0f) return EncumbranceLevel.Heavy;
            return EncumbranceLevel.Over;
        }

        public static bool IsOverencumbered(DrifterBagController controller)
        {
            return GetMassRatio(controller) > 1.0f;
        }
        public static float GetMoveSpeedPenalty(DrifterBagController controller)
        {
            if (controller == null) return 1.0f;
            return Core.StateCalculator.CalculateMovespeedPenalty(controller, GetTotalMass(controller));
        }

        public static float GetDamageMultiplier(DrifterBagController controller)
        {
            if (controller == null) return 1.0f;
            return Core.SlamDamageCalculator.GetEffectiveCoefficient(controller);
        }

        // ========================================================================================
        // FORMULA VARIABLES
        // ========================================================================================
        public static void RegisterFormulaVariable(string name, float value, string? description = null)
        {
            Balance.FormulaRegistry.RegisterVariable(name, value, description);
        }

        public static void RegisterFormulaVariable(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null)
        {
            Balance.FormulaRegistry.RegisterVariable(name, provider, description, fallbackValue);
        }

        public static bool RegisterFormulaVariableSafe(string name, float value, string? description = null, bool overwrite = false)
        {
            return Balance.FormulaRegistry.RegisterVariableSafe(name, value, description, overwrite);
        }

        public static bool RegisterFormulaVariableSafe(string name, Func<CharacterBody?, float> provider, string? description = null, float? fallbackValue = null, bool overwrite = false)
        {
            return Balance.FormulaRegistry.RegisterVariableSafe(name, provider, description, fallbackValue, overwrite);
        }

        public static IEnumerable<string> GetFormulaVariableNames()
        {
            return Balance.FormulaRegistry.GetRegisteredVariableNames();
        }

        public static bool UnregisterFormulaVariable(string name)
        {
            return Balance.FormulaRegistry.UnregisterVariable(name);
        }

        public static bool IsFormulaVariableRegistered(string name)
        {
            return Balance.FormulaRegistry.IsVariableRegistered(name);
        }

        public static VariableInfo? GetFormulaVariableInfo(string name)
        {
            return Balance.FormulaRegistry.GetVariableInfo(name);
        }

        // ========================================================================================
        // FILTERED QUERIES
        // ========================================================================================
        public static List<GameObject> GetBaggedObjectsByComponent<T>(DrifterBagController controller) where T : Component
        {
            var result = new List<GameObject>();
            TryGetBaggedObjectsByComponent<T>(controller, result);
            return result;
        }

        public static void TryGetBaggedObjectsByComponent<T>(DrifterBagController controller, List<GameObject> buffer) where T : Component
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null) return;
            foreach (var obj in list)
            {
                if (obj != null && obj.GetComponent<T>() != null)
                {
                    buffer.Add(obj);
                }
            }
        }

        public static List<GameObject> GetBaggedCharacterBodies(DrifterBagController controller)
        {
            return GetBaggedObjectsByComponent<CharacterBody>(controller);
        }

        public static void TryGetBaggedCharacterBodies(DrifterBagController controller, List<GameObject> buffer)
        {
            TryGetBaggedObjectsByComponent<CharacterBody>(controller, buffer);
        }

        // ========================================================================================
        // OPERATIONS
        // ========================================================================================
        public static bool TryGrab(DrifterBagController controller, GameObject obj)
        {
            if (controller == null || obj == null) return false;
            if (!HasRoom(controller)) return false;
            return AddBaggedObject(controller, obj);
        }

        public static bool TryReleaseMainPassenger(DrifterBagController controller)
        {
            if (controller == null) return false;
            var mainPassenger = GetMainPassenger(controller);
            if (mainPassenger == null) return false;
            RemoveBaggedObject(controller, mainPassenger, false);
            return true;
        }

        public static int ReleaseObjectsByType<T>(DrifterBagController controller) where T : Component
        {
            if (controller == null) return 0;
            var objects = GetBaggedObjectsByComponent<T>(controller);
            int count = 0;
            foreach (var obj in objects)
            {
                RemoveBaggedObject(controller, obj, false);
                count++;
            }
            return count;
        }

        // ========================================================================================
        // SUMMARY HELPERS
        // ========================================================================================
        public static string GetFormattedBagSummary(DrifterBagController controller)
        {
            if (controller == null) return "Bag: N/A";

            int count = GetBagCount(controller);
            int capacity = GetBagCapacity(controller);
            float totalMass = GetTotalMass(controller);
            float massCap = GetMassCapacity(controller);
            float ratio = GetMassRatio(controller);

            string countStr = capacity == int.MaxValue ? $"{count}/∞" : $"{count}/{capacity}";
            string massCapStr = massCap == float.MaxValue ? "∞" : massCap.ToString("F0");

            return $"Bag: {countStr} | Mass: {totalMass:F0}/{massCapStr} ({ratio:P0})";
        }

        public static List<string> GetBaggedObjectDetails(DrifterBagController controller)
        {
            _detailsBuffer.Clear();
            if (controller == null) return new List<string>(_detailsBuffer);
            int index = 1;
            ForEachBaggedObject(controller, obj =>
            {
                string name = GetObjectName(obj);
                float mass = GetObjectMass(controller, obj);
                _detailsBuffer.Add($"{index}. {name} ({mass:F1}kg)");
                index++;
            });
            return new List<string>(_detailsBuffer);
        }

        public static void TryGetBaggedObjectDetails(DrifterBagController controller, List<string> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (controller == null) return;
            int index = 1;
            ForEachBaggedObject(controller, obj =>
            {
                string name = GetObjectName(obj);
                float mass = GetObjectMass(controller, obj);
                buffer.Add($"{index}. {name} ({mass:F1}kg)");
                index++;
            });
        }

        // ========================================================================================
        // EVENTS
        // ========================================================================================
        public static event Action<DrifterBagController, GameObject, int>? OnObjectGrabbed;

        public static event Action<DrifterBagController, GameObject, bool>? OnObjectReleased;

        public static event Action<DrifterBagController>? OnBagFull;

        public static event Action<DrifterBagController, float>? OnOverencumbered;

        public static event Action<DrifterBagController, bool>? OnBagCleared;

        public static event Action<DrifterBagController, GameObject?, GameObject?>? OnMainPassengerChanged;

        public static event Action<DrifterBagController, float, float>? OnMassRecalculated;

        // ========================================================================================
        // EVENT INVOKERS
        // ========================================================================================
        internal static void InvokeOnObjectGrabbed(DrifterBagController controller, GameObject obj, int slotIndex)
        {
            OnObjectGrabbed?.Invoke(controller, obj, slotIndex);
        }

        internal static void InvokeOnObjectReleased(DrifterBagController controller, GameObject obj, bool wasDestroyed)
        {
            OnObjectReleased?.Invoke(controller, obj, wasDestroyed);
        }

        internal static void InvokeOnBagFull(DrifterBagController controller)
        {
            OnBagFull?.Invoke(controller);
        }

        internal static void InvokeOnOverencumbered(DrifterBagController controller, float massRatio)
        {
            OnOverencumbered?.Invoke(controller, massRatio);
        }

        internal static void InvokeOnBagCleared(DrifterBagController controller, bool wasDestroyed)
        {
            OnBagCleared?.Invoke(controller, wasDestroyed);
        }

        internal static void InvokeOnMainPassengerChanged(DrifterBagController controller, GameObject? previousObj, GameObject? newObj)
        {
            OnMainPassengerChanged?.Invoke(controller, previousObj, newObj);
        }

        internal static void InvokeOnMassRecalculated(DrifterBagController controller, float newTotalMass, float previousTotalMass)
        {
            OnMassRecalculated?.Invoke(controller, newTotalMass, previousTotalMass);
        }

        // ========================================================================================
        // SERIALIZATION API
        // ========================================================================================
        public static void RegisterSerializerPlugin(ProperSave.Serializers.IObjectSerializerPlugin plugin)
        {
            ProperSave.ProperSaveIntegration.RegisterPlugin(plugin);
        }

        public static List<ProperSave.Serializers.IObjectSerializerPlugin> GetSerializerPlugins()
        {
            return ProperSave.ProperSaveIntegration.GetSerializerPlugins();
        }
    }

    public static class EliteThrowEffectsAPI
    {
        public sealed class Entry
        {
            public string Id { get; }
            public string DisplayName { get; }
            public ConfigEntry<bool>? Enabled { get; internal set; }
            public ConfigEntry<bool>? GrabEnabled { get; internal set; }
            public bool SupportsGrab => GrabEffect != null || GrabHitEffect != null || NativeEquipmentName == "EliteFireEquipment";
            internal readonly Action<CharacterBody, CharacterBody>? GrabEffect;
            internal readonly Action<CharacterBody, CharacterBody>? GrabHitEffect;
            internal readonly float GrabHitCooldown;
            internal readonly string? NativeEquipmentName;
            internal readonly Func<EliteDef, bool>? Matches;
            internal readonly DamageTypeCombo Flags;
            internal readonly bool DefaultEnabled;

            internal Entry(string id, string name, string? equipment, Func<EliteDef, bool>? matches,
                DamageTypeCombo flags, bool enabled, Action<CharacterBody, CharacterBody>? grabEffect = null,
                Action<CharacterBody, CharacterBody>? grabHitEffect = null, float grabHitCooldown = 0f)
            {
                Id = id; DisplayName = name; NativeEquipmentName = equipment;
                Matches = matches; Flags = flags; DefaultEnabled = enabled; GrabEffect = grabEffect;
                GrabHitEffect = grabHitEffect; GrabHitCooldown = grabHitCooldown;
            }
        }

        private static readonly List<Entry> entries = new()
        {
            new Entry("Blazing", "Blazing - Burn", "EliteFireEquipment", null, default, false),
            new Entry("Glacial", "Glacial - Freeze", "EliteIceEquipment", null, default, false, (passenger, grabber) => grabber.AddTimedBuff(RoR2Content.Buffs.Slow80, 1.5f)),
            new Entry("Overloading", "Overloading - Electrical Shock", "EliteLightningEquipment", null, default, false, SpawnLightningStake),
            new Entry("Perfected", "Perfected - Cripple", "EliteLunarEquipment", null, default, false, null,
                (passenger, grabber) => grabber.AddTimedBuff(RoR2Content.Buffs.Cripple, 3f), 6f)
        };
        private static ConfigFile? config;
        private static ConditionalWeakTable<CharacterBody, Dictionary<string, float>> grabHitCooldowns = new();
        internal static event Action<Entry>? EntryBound;
        public static IReadOnlyList<Entry> Entries => entries.AsReadOnly();

        public static Entry Register(string id, string displayName, Func<EliteDef, bool> matches,
            DamageTypeCombo flags, bool defaultEnabled = false)
            => Register(id, displayName, matches, flags, defaultEnabled, null);

        public static Entry Register(string id, string displayName, Func<EliteDef, bool> matches,
            DamageTypeCombo flags, bool defaultEnabled, Action<CharacterBody, CharacterBody>? grabEffect)
            => Register(id, displayName, matches, flags, defaultEnabled, grabEffect, null, 0f);

        public static Entry Register(string id, string displayName, Func<EliteDef, bool> matches,
            DamageTypeCombo flags, bool defaultEnabled, Action<CharacterBody, CharacterBody>? grabEffect,
            Action<CharacterBody, CharacterBody>? grabHitEffect, float grabHitCooldown)
        {
            if (float.IsNaN(grabHitCooldown) || float.IsInfinity(grabHitCooldown) || grabHitCooldown < 0f)
                throw new ArgumentOutOfRangeException(nameof(grabHitCooldown));
            if (string.IsNullOrWhiteSpace(id) || id.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-'))
                throw new ArgumentException("Use a stable ID containing letters, digits, underscores or hyphens", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A display name is required", nameof(displayName));
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            if (entries.Any(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("An elite throw mapping with this ID already exists", nameof(id));
            var entry = new Entry(id, displayName, null, matches, flags, defaultEnabled, grabEffect, grabHitEffect, grabHitCooldown);
            entries.Add(entry);
            if (config != null) Bind(entry);
            return entry;
        }

        internal static void Initialize(ConfigFile cfg)
        {
            config = cfg;
            foreach (var entry in entries) Bind(entry);
        }

        private static void Bind(Entry entry)
        {
            entry.Enabled = config!.Bind("Balance", "EliteThrowEffect_" + entry.Id, entry.DefaultEnabled,
                "Apply " + entry.DisplayName + " when throwing this elite.");
            entry.Enabled.SettingChanged += (_, __) => PluginConfig.RefreshEliteThrowSelection();
            if (entry.SupportsGrab)
            {
                entry.GrabEnabled = config.Bind("Balance", "EliteGrabEffect_" + entry.Id, entry.Id == "Blazing",
                    entry.Id switch
                    {
                        "Blazing" => "Keep Blazing fire trails active while bagged.",
                        "Glacial" => "Grabbing a Glacial enemy slows you for 1.5 seconds.",
                        "Overloading" => "Grabbing an Overloading enemy spawns a lightning stake with half the enemy's current damage.",
                        "Perfected" => "Taking a direct damaging hit while carrying a Perfected enemy cripples you for 3 seconds. Cooldown: 6 seconds.",
                        _ => "Apply " + entry.Id + "'s effect to you while bagged."
                    });
                entry.GrabEnabled.SettingChanged += (_, __) => PluginConfig.RefreshEliteThrowSelection();
            }
            PluginConfig.RefreshEliteThrowSelection();
            EntryBound?.Invoke(entry);
        }

        internal static string EnabledIds => string.Join(",", entries.Where(e => e.Enabled?.Value == true).Select(e => e.Id));
        internal static void ApplyEnabledIds(string ids)
        {
            var enabled = new HashSet<string>(ids.Split(','), StringComparer.Ordinal);
            foreach (var entry in entries)
                if (entry.Enabled != null) entry.Enabled.Value = enabled.Contains(entry.Id);
        }

        internal static string EnabledGrabIds => string.Join(",", entries.Where(e => e.GrabEnabled?.Value == true).Select(e => e.Id));
        internal static bool IsGrabEffectEnabled(string id) => entries.Any(entry => entry.Id == id && entry.GrabEnabled?.Value == true);
        internal static string SelectionIds => string.Join(",", entries.SelectMany(e =>
            (e.Enabled?.Value == true ? new[] { e.Id } : Array.Empty<string>()).Concat(
                e.GrabEnabled?.Value == true ? new[] { "Grab:" + e.Id } : Array.Empty<string>())));

        internal static void ApplyGrabEnabledIds(string ids)
        {
            var enabled = new HashSet<string>(ids.Split(','), StringComparer.Ordinal);
            foreach (var entry in entries)
                if (entry.GrabEnabled != null) entry.GrabEnabled.Value = enabled.Contains(entry.Id);
        }

        internal static void ApplySelectionIds(string ids)
        {
            var selected = ids.Split(',');
            ApplyEnabledIds(string.Join(",", selected.Where(id => !id.StartsWith("Grab:", StringComparison.Ordinal))));
            ApplyGrabEnabledIds(string.Join(",", selected.Where(id => id.StartsWith("Grab:", StringComparison.Ordinal)).Select(id => id.Substring(5))));
        }

        internal static void ApplyGrabEffects(CharacterBody passenger, CharacterBody grabber)
        {
            var elites = EliteIdentity.Enumerate(passenger).ToArray();
            foreach (var entry in entries)
            {
                if (entry.GrabEnabled?.Value != true || entry.GrabEffect == null) continue;
                try
                {
                    if (elites.Any(elite => entry.NativeEquipmentName != null
                        ? elite.eliteEquipmentDef && elite.eliteEquipmentDef.name == entry.NativeEquipmentName
                        : entry.Matches!(elite)))
                        entry.GrabEffect(passenger, grabber);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EliteGrabEffects] Mapping {entry.Id} failed: {ex.Message}");
                }
            }
        }

        internal static void ResetGrabHitCooldowns() => grabHitCooldowns = new();

        internal static void ApplyGrabHitEffects(DrifterBagController bag, CharacterBody grabber)
        {
            foreach (var entry in entries)
            {
                if (entry.GrabEnabled?.Value != true || entry.GrabHitEffect == null) continue;
                var cooldowns = grabHitCooldowns.GetOrCreateValue(grabber);
                if (cooldowns.TryGetValue(entry.Id, out var nextTime) && Time.fixedTime < nextTime) continue;
                CharacterBody? passenger = null;
                var main = bag.vehicleSeat ? bag.vehicleSeat.NetworkpassengerBodyObject : null;
                try
                {
                    if (main) passenger = FindMatchingPassenger(main, bag, grabber, entry);
                    if (!passenger)
                        foreach (var obj in DrifterBagAPI.GetBaggedObjectsReadOnly(bag))
                        {
                            passenger = FindMatchingPassenger(obj, bag, grabber, entry);
                            if (passenger) break;
                        }
                    if (!passenger) continue;
                    cooldowns[entry.Id] = Time.fixedTime + entry.GrabHitCooldown;
                    entry.GrabHitEffect(passenger!, grabber);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EliteGrabEffects] Hit mapping {entry.Id} failed: {ex.Message}");
                }
            }
        }

        private static CharacterBody? FindMatchingPassenger(GameObject? obj, DrifterBagController bag, CharacterBody grabber, Entry entry)
        {
            var body = obj ? obj!.GetComponent<CharacterBody>() : null;
            if (body is null || !body || body.isPlayerControlled || !body.healthComponent || !body.healthComponent.alive ||
                !body.teamComponent || !grabber.teamComponent || body.teamComponent.teamIndex == grabber.teamComponent.teamIndex) return null;
            var seat = body.currentVehicle;
            if (!seat || seat.NetworkpassengerBodyObject != obj ||
                (seat != bag.vehicleSeat && seat.GetComponentInParent<DrifterBagController>() != bag)) return null;
            return EliteIdentity.Enumerate(body).Any(elite => entry.NativeEquipmentName != null
                ? elite.eliteEquipmentDef && elite.eliteEquipmentDef.name == entry.NativeEquipmentName
                : entry.Matches!(elite)) ? body : null;
        }

        private static void SpawnLightningStake(CharacterBody passenger, CharacterBody grabber)
        {
            if (!UnityEngine.Networking.NetworkServer.active) return;
            var prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/LightningStake");
            if (!prefab || !ProjectileManager.instance) return;
            var position = grabber.footPosition;
            if (Physics.Raycast(position + Vector3.up, Vector3.down, out var groundHit, 16f,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                position = groundHit.point + groundHit.normal * 0.1f;
            var stake = ProjectileManager.instance.FireProjectileImmediateServer(new FireProjectileInfo
            {
                projectilePrefab = prefab,
                position = position,
                rotation = Quaternion.identity,
                owner = passenger.gameObject,
                damage = passenger.damage * 0.5f,
                force = 0f,
                crit = false,
                damageColorIndex = DamageColorIndex.Item
            });
            if (!stake) return;
            var explosion = stake.GetComponent<ProjectileImpactExplosion>();
            if (explosion) explosion.blastRadius = 16f;
            var sticking = stake.GetComponent<ProjectileStickOnImpact>();
            if (sticking)
            {
                sticking.ignoreCharacters = true;
                sticking.ignoreImpact = true;
                sticking.enabled = false;
            }
            var rigidbody = stake.GetComponent<Rigidbody>();
            if (rigidbody)
            {
                rigidbody.detectCollisions = false;
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
            }
        }

        internal static DamageTypeCombo Resolve(CharacterBody body, ThrownObjectProjectileController prefab)
        {
            var result = DamageTypeCombo.Generic;
            foreach (var elite in EliteIdentity.Enumerate(body))
                foreach (var entry in entries)
                {
                    if (entry.Enabled?.Value != true) continue;
                    if (entry.NativeEquipmentName != null)
                    {
                        if (prefab.eliteOverrides == null) continue;
                        foreach (var mapping in prefab.eliteOverrides)
                            if (mapping.equipmentDef && mapping.equipmentDef == elite.eliteEquipmentDef &&
                                mapping.equipmentDef.name == entry.NativeEquipmentName)
                                result = MergeFlags(result, mapping.damageTypeCombo);
                    }
                    else
                    {
                        try
                        {
                            if (entry.Matches!(elite)) result = MergeFlags(result, entry.Flags);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[EliteThrowEffects] Mapping {entry.Id} failed: {ex.Message}");
                        }
                    }
                }
            return result;
        }

        internal static DamageTypeCombo MergeFlags(DamageTypeCombo original, DamageTypeCombo additions)
        {
            original.damageType |= additions.damageType;
            original.damageTypeExtended |= additions.damageTypeExtended;
            return original;
        }

        internal static DamageTypeCombo AddedFlags(DamageTypeCombo original, DamageTypeCombo additions)
        {
            var result = DamageTypeCombo.Generic;
            result.damageType = additions.damageType & ~original.damageType;
            result.damageTypeExtended = additions.damageTypeExtended & ~original.damageTypeExtended;
            return result;
        }

        internal static DamageTypeCombo RemoveFlags(DamageTypeCombo original, DamageTypeCombo removed)
        {
            original.damageType &= ~removed.damageType;
            original.damageTypeExtended &= ~removed.damageTypeExtended;
            return original;
        }
    }
}

namespace DrifterBossGrabMod
{
    internal static class EliteIdentity
    {
        internal static IEnumerable<EliteDef> Enumerate(CharacterBody? body)
        {
            if (!body || BuffCatalog.eliteBuffIndices == null) yield break;
            foreach (var index in BuffCatalog.eliteBuffIndices)
            {
                if (!body!.HasBuff(index)) continue;
                var elite = BuffCatalog.GetBuffDef(index)?.eliteDef;
                if (elite) yield return elite!;
            }
        }
    }
}
