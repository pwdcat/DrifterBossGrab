#nullable enable
using RoR2;
using HarmonyLib;
using DrifterBossGrabMod.Core;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates;
using System.Linq;
using System.Reflection;

namespace DrifterBossGrabMod.Patches
{
    public static class TeleporterPatches
    {
        private static readonly FieldInfo? _baseSingletonField = typeof(TeleporterInteraction).BaseType?.GetField("instance", BindingFlags.Static | BindingFlags.Public);

        public static void PatchStaleReferences(TeleporterInteraction teleporter)
        {
            if (teleporter == null || teleporter.gameObject == null) return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug("[TeleporterPatches] Skipping patches: Teleporter1 is blacklisted from persistence system.");
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[TeleporterPatches] Patching stale references for teleporter: {teleporter.name}");

            // Track activation state
            var stateMachines = teleporter.GetComponents<EntityStateMachine>();
            var esm = stateMachines.FirstOrDefault(esm => esm.customName == "Body") ?? teleporter.GetComponent<EntityStateMachine>();
            if (esm != null)
            {
                Log.Info($"[TeleporterPatches.State] Current State: {esm.state?.GetType().Name ?? "null"}, ActivationState: {teleporter.activationState}, shrineBonusStacks={teleporter.shrineBonusStacks}");
            }

            // Toggle off isInFinalSequence by kicking back to ChargedState if fully charged
            if (esm != null && teleporter.isInFinalSequence)
            {
                var chargedStateType = typeof(TeleporterInteraction).GetNestedType("ChargedState", BindingFlags.NonPublic);
                if (chargedStateType != null)
                {
                    var chargedState = System.Activator.CreateInstance(chargedStateType) as EntityStates.EntityState;
                    if (chargedState != null)
                    {
                        Log.Info($"[TeleporterPatches.State] Teleporter is in FinishedState. Kicking back to ChargedState to allow re-interaction.");
                        esm.SetNextState(chargedState);
                    }
                }
            }

            // Reset SceneExitController to allow stage advancement
            var exitController = teleporter.GetComponent<RoR2.SceneExitController>();
            if (exitController != null)
            {
                var exitStateField = typeof(RoR2.SceneExitController).GetField("exitState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (exitStateField != null)
                {
                    Log.Info($"[TeleporterPatches.State] Resetting SceneExitController.exitState from Finished to Idle.");
                    exitStateField.SetValue(exitController, 0);
                }
            }

            try
            {
                // 1. Restore singleton if needed
                RestoreSingleton();

                // 2. Fix HoldoutZoneController: Ensure a clean 0% charge state
                var holdout = teleporter.GetComponent<RoR2.HoldoutZoneController>();
                if (holdout != null)
                {
                    holdout.Network_charge = 0f;

                    // Fix Radius NaN / Velocity issues
                    object? velocityValue = ReflectionCache.HoldoutZoneController.RadiusVelocity?.GetValue(holdout);
                    float velocity = (velocityValue is float f) ? f : 0f;
                    if (float.IsNaN(velocity) || float.IsInfinity(velocity))
                    {
                        ReflectionCache.HoldoutZoneController.RadiusVelocity?.SetValue(holdout, 0f);
                    }

                    // Jumpstart currentRadius so player detection works immediately
                    ReflectionCache.HoldoutZoneController.CurrentRadius?.SetValue(holdout, holdout.baseRadius);

                    // Only enable HoldoutZone if charging
                    holdout.enabled = teleporter.isCharging;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[TeleporterPatches.HoldoutZone] Reset charge/radius for {teleporter.name} (Enabled: {holdout.enabled})");
                }

                // 3. Replace OutsideInteractableLocker to kill stale coroutines
                var locker = teleporter.GetComponent<RoR2.OutsideInteractableLocker>();
                if (locker != null)
                {
                    try
                    {
                        float oldRadius = locker.radius;
                        Object.DestroyImmediate(locker);

                        var newLocker = teleporter.gameObject.AddComponent<RoR2.OutsideInteractableLocker>();
                        if (newLocker != null)
                        {
                            newLocker.radius = oldRadius;
                            newLocker.enabled = teleporter.isCharging;
                        }
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug("[TeleporterPatches] Replaced OutsideInteractableLocker with fresh instance.");
                    }
                    catch (System.Exception ex) { Log.Error($"[TeleporterPatches] Locker nuclear reset error: {ex.Message}"); }
                }

                // 4. Clean up BossDirector (CombatDirector) and harden spawning
                var director = ReflectionCache.TeleporterInteraction.BossDirector?.GetValue(teleporter) as CombatDirector;
                if (director != null && director.gameObject != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[TeleporterPatches.Director] CombatDirector found - INITIAL credits={director.monsterCredit}, enabled={director.enabled}");

                    var squad = ReflectionCache.CombatDirector.CombatSquad?.GetValue(director) as CombatSquad;
                    if (squad != null && squad.gameObject != null)
                    {
                        var members = ReflectionCache.CombatSquad.MembersList?.GetValue(squad) as System.Collections.IList;
                        var defeatedServerField = typeof(CombatSquad).GetField("defeatedServer", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (defeatedServerField != null)
                        {
                            bool wasDefeated = (bool)(defeatedServerField.GetValue(squad) ?? false);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Debug($"[TeleporterPatches.Director] Resetting CombatSquad.defeatedServer from {wasDefeated} -> False");
                            defeatedServerField.SetValue(squad, false);
                        }
                        var historyField = typeof(CombatSquad).GetField("memberHistory", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (historyField != null)
                        {
                            var history = historyField.GetValue(squad) as System.Collections.IList;
                            history?.Clear();
                        }
                        if (members != null)
                        {
                            for (int i = members.Count - 1; i >= 0; i--)
                            {
                                var member = members[i] as Object;
                                if (member == null) members.RemoveAt(i);
                            }
                        }
                    }

                    var teleLocker = teleporter.GetComponent<RoR2.OutsideInteractableLocker>();
                    if (teleLocker != null)
                    {
                        try
                        {
                            var objectMap = ReflectionCache.OutsideInteractableLocker.LockObjectMap?.GetValue(teleLocker) as System.Collections.IDictionary;
                            var interactableMap = ReflectionCache.OutsideInteractableLocker.LockInteractableMap?.GetValue(teleLocker) as System.Collections.IDictionary;
                            objectMap?.Clear();
                            interactableMap?.Clear();
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Debug($"[TeleporterPatches] Cleared stale locker maps for {teleporter.name}");
                        }
                        catch (System.Exception ex)
                        {
                            Log.Warning($"[TeleporterPatches] Failed to clear locker maps: {ex.Message}");
                        }
                    }
                    // Harden Director Parameters
                    float baseRadius = teleporter.holdoutZoneController ? teleporter.holdoutZoneController.baseRadius : 60f;
                    if (baseRadius < 10f) baseRadius = 60f; // Fallback for uninitialized zones

                    ReflectionCache.CombatDirector.SpawnRange?.SetValue(director, baseRadius * 1.5f);
                    ReflectionCache.CombatDirector.MinSpawnDistance?.SetValue(director, 0f);
                    ReflectionCache.CombatDirector.MaxSpawnDistance?.SetValue(director, baseRadius * 2f);
                    ReflectionCache.CombatDirector.ExpendEntireMonsterCredit?.SetValue(director, true);

                    // Ensure director is using the same squad as the BossGroup with verification
                    var bossGroupComp = teleporter.GetComponent<RoR2.BossGroup>();
                    bool squadLinked = false;
                    if (bossGroupComp != null && bossGroupComp.combatSquad != null)
                    {
                        var squadField = ReflectionCache.CombatDirector.CombatSquad;
                        if (squadField != null)
                        {
                            // Link director to BossGroup's squad
                            squadField.SetValue(director, bossGroupComp.combatSquad);
                            squadLinked = true;

                            // Check that BossGroup has valid squad reference
                            bool subscriptionValid = VerifyBossGroupSubscription(bossGroupComp);
                            Log.Info($"[TeleporterPatches.Director] Squad linked: director.combatSquad={bossGroupComp.combatSquad?.netId}, subscriptionValid={subscriptionValid}");
                        }
                        else
                        {
                            Log.Error($"[TeleporterPatches.Director] CRITICAL: Cannot link squad - CombatSquad field not found for {teleporter.name}");
                        }
                    }
                    else
                    {
                        Log.Error($"[TeleporterPatches.Director] CRITICAL: Cannot link squad - BossGroup {(bossGroupComp == null ? "is null" : "combatSquad is null")} for {teleporter.name}");
                    }

                    // If squad linking failed, force director to use BossGroup's squad
                    if (!squadLinked && bossGroupComp != null && bossGroupComp.combatSquad != null)
                    {
                        Log.Warning($"[TeleporterPatches] Squad linking failed, forcing fallback: director={director.name}, bossGroup={bossGroupComp.name}");

                        try
                        {
                            var squadField = ReflectionCache.CombatDirector.CombatSquad;
                            if (squadField != null)
                            {
                                squadField.SetValue(director, bossGroupComp.combatSquad);
                                squadLinked = true;
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Debug($"[TeleporterPatches] Fallback squad linking successful");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error($"[TeleporterPatches] Fallback squad linking failed: {ex.Message}");
                        }
                    }

                    // Handled by universal BossGroupPatches.DropRewards redirection

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[TeleporterPatches.Director] Hardened Results: " +
                                 $"range={ReflectionCache.CombatDirector.SpawnRange?.GetValue(director) ?? "FAIL"}, " +
                                 $"credits={ReflectionCache.CombatDirector.MonsterCredit?.GetValue(director) ?? "FAIL"}, " +
                                 $"squadLinked={squadLinked}, " +
                                 $"playerCount={Run.instance?.participatingPlayerCount ?? -1}");
                    }

                    // 5. Re-trigger Boss Spawn logic if charging
                    if (teleporter.isCharging)
                    {
                        director.enabled = true;
                        float diffCoeff = Run.instance != null ? Run.instance.compensatedDifficultyCoefficient : 1f;
                        float spawnCredits = (float)System.Math.Max(director.overrideCost, (int)(600f * Mathf.Pow(diffCoeff, 0.5f))) * (float)(1 + teleporter.shrineBonusStacks);

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[TeleporterPatches.Director] BEFORE adding credits - monsterCredit={director.monsterCredit}, shrineBonusStacks={teleporter.shrineBonusStacks}, spawnCredits={spawnCredits}");

                        // Mark as restoring to prevent vanilla credit transfer during OnDisable
                        CombatDirectorPatches.MarkTeleporterDirectorAsRestoring(director);

                        director.currentSpawnTarget = teleporter.gameObject;
                        director.monsterCredit += spawnCredits;
                        director.SetNextSpawnAsBoss();

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[TeleporterPatches.Director] AFTER adding credits - monsterCredit={director.monsterCredit}, expected={director.monsterCredit - spawnCredits + spawnCredits:F0}");
                            Log.Debug($"[TeleporterPatches.Director] Director state: enabled={director.enabled}, spawnTarget={director.currentSpawnTarget?.name ?? "null"}");
                        }

                        // Verify credits persist after a short delay
                        director.StartCoroutine(VerifyCreditsPersist(director, spawnCredits));
                    }
                }

                // 6. Reset BossGroup for fresh encounter (new drops, clean health tracking)
                var bossGroup = teleporter.GetComponent<RoR2.BossGroup>();
                if (bossGroup != null && bossGroup.gameObject != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[TeleporterPatches.BossGroup] BossGroup found - bonusRewardCount={bossGroup.bonusRewardCount}, dropTable={bossGroup.dropTable?.name ?? "null"}");
                    // Clear stale boss memories so health bar shows new bosses
                    ReflectionCache.BossGroup.BossMemoryCount?.SetValue(bossGroup, 0);

                    // Sync shrine bonuses to rewards
                    bossGroup.bonusRewardCount = teleporter.shrineBonusStacks;

                    if (NetworkServer.active && !teleporter.isCharged)
                    {
                        // 6a. Reset MonstersCleared flag to prevent instant completion
                        ReflectionCache.TeleporterInteraction.MonstersCleared?.SetValue(teleporter, false);

                        // Fresh RNG so drops aren't duplicated. 
                        // Fallback to time-based seed if bossRewardRng is missing
                        var rngField = typeof(RoR2.BossGroup).GetField("rng", BindingFlags.NonPublic | BindingFlags.Instance);
                        ulong seed = (Run.instance?.bossRewardRng != null) ? Run.instance.bossRewardRng.nextUlong : (ulong)System.DateTime.Now.Ticks;
                        rngField?.SetValue(bossGroup, new Xoroshiro128Plus(seed));
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Debug($"[TeleporterPatches.BossGroup] Set fresh RNG with seed source: {(Run.instance?.bossRewardRng != null ? "Run" : "Time")}");

                        // Ensure the list exists and has at least the base table
                        var currentTables = (System.Collections.Generic.List<PickupDropTable>?)ReflectionCache.BossGroup.BossDropTables?.GetValue(bossGroup);
                        if (currentTables == null)
                        {
                            currentTables = new System.Collections.Generic.List<PickupDropTable>();
                            ReflectionCache.BossGroup.BossDropTables?.SetValue(bossGroup, currentTables);
                        }

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[TeleporterPatches.BossGroup] PRE-PATCH: " +
                                     $"dropTable={(bossGroup.dropTable != null ? bossGroup.dropTable.name : "NULL")}, " +
                                     $"bonusCount={bossGroup.bonusRewardCount}, " +
                                     $"currentTablesCount={currentTables.Count}, " +
                                     $"squadMembers={bossGroup.combatSquad?.readOnlyMembersList.Count ?? -1}");
                        }

                        if (currentTables.Count == 0)
                        {
                            if (bossGroup.dropTable != null)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Debug($"[TeleporterPatches] Restoring reward dropTable: {bossGroup.dropTable.name}");
                                currentTables.Add(bossGroup.dropTable);
                            }
                            else
                            {
                                // Try to load the standard teleporter drop table
                                var defaultTable = Resources.Load<PickupDropTable>("DropTables/dtTier2Item"); //dtTier2Item is common for TP
                                if (defaultTable != null)
                                {
                                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        Log.Debug("[TeleporterPatches] Using FAIL-SAFE dtTier2Item drop table.");
                                    currentTables.Add(defaultTable);
                                }
                            }
                        }

                        ReflectionCache.BossGroup.BossDrops?.SetValue(bossGroup, new System.Collections.Generic.List<UniquePickup>());
                        ReflectionCache.BossGroup.BossDropTablesLocked?.SetValue(bossGroup, false);
                    }

                    // Set drop position to the teleporter itself
                    bossGroup.dropPosition = teleporter.transform;

                    // Reset HUD boss bar
                    try { ReflectionCache.BossGroup.ResetBossBar?.Invoke(bossGroup, null); }
                    catch { /* Stale internals, skip */ }
                }

                // 7. Fix teleporterPositionIndicator destroyed by scene transition
                var positionIndicator = ReflectionCache.TeleporterInteraction.PositionIndicator.GetValue(teleporter) as Component;
                // Unity overrides == operator, so evaluating to null here means the underlying native object is destroyed
                if (positionIndicator == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[TeleporterPatches] Recreating destroyed teleporterPositionIndicator for {teleporter.name}");
                    var prefab = RoR2.LegacyResourcesAPI.Load<GameObject>("Prefabs/PositionIndicators/TeleporterChargingPositionIndicator");
                    if (prefab != null)
                    {
                        var newIndicatorObj = UnityEngine.Object.Instantiate(prefab, teleporter.transform.position, Quaternion.identity);
                        var newIndicator = newIndicatorObj.GetComponent<RoR2.PositionIndicator>();
                        if (newIndicator != null)
                        {
                            newIndicator.targetTransform = teleporter.transform;
                            ReflectionCache.TeleporterInteraction.PositionIndicator.SetValue(teleporter, newIndicator);

                            var chargeIndicator = newIndicatorObj.GetComponent<RoR2.UI.ChargeIndicatorController>();
                            if (chargeIndicator != null)
                            {
                                holdout = teleporter.GetComponent<RoR2.HoldoutZoneController>();
                                chargeIndicator.holdoutZoneController = holdout;
                                chargeIndicator.isActivated = teleporter.isActivated;
                                chargeIndicator.isCharged = teleporter.isCharged;
                                chargeIndicator.isDiscovered = teleporter.isDiscovered;
                            }
                            newIndicatorObj.SetActive(false);
                        }
                    }
                }

                // 8. Refresh cachedLocalUser
                var mpeventSystem = (RoR2.UI.MPEventSystem)UnityEngine.EventSystems.EventSystem.current;
                var localUser = (mpeventSystem != null) ? mpeventSystem.localUser : null;
                ReflectionCache.TeleporterInteraction.CachedLocalUser?.SetValue(teleporter, localUser);

            }
            catch (System.Exception ex)
            {
                Log.Error($"[TeleporterPatches] Error during stale reference patching: {ex.Message}");
            }
        }

        // Silence errors
        private static void SilenceSingleton(TeleporterInteraction __instance)
        {
            var primary = MultiTeleporterTracker.GetPrimary();
            if (primary != null && primary != __instance)
            {
                // Clear specialized singleton
                TeleporterInteraction.instance = null!;

                // Clear base class singleton via reflection
                if (_baseSingletonField != null)
                {
                    _baseSingletonField.SetValue(null, null);
                }
            }
        }

        // Restore singleton reference back to the primary.
        private static void RestoreSingleton()
        {
            var primary = MultiTeleporterTracker.GetPrimary();
            if (primary != null)
            {
                TeleporterInteraction.instance = primary;
                if (_baseSingletonField != null)
                {
                    _baseSingletonField.SetValue(null, primary);
                }
            }
        }

        // Awake PREFIX — clear the singleton
        [HarmonyPatch(typeof(TeleporterInteraction), "Awake")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void AwakePrefix(TeleporterInteraction __instance)
        {
            SilenceSingleton(__instance);
        }

        // register primary/secondary and restore singleton.
        [HarmonyPatch(typeof(TeleporterInteraction), "Awake")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPostfix]
        private static void AwakePostfix(TeleporterInteraction __instance)
        {
            if (MultiTeleporterTracker.GetPrimary() == null)
            {
                MultiTeleporterTracker.RegisterPrimary(__instance);
            }
            else
            {
                MultiTeleporterTracker.RegisterSecondary(__instance);
                // Restoration of singleton happens below
            }
            RestoreSingleton();
        }

        // singleton guard and NRE mitigation.
        [HarmonyPatch(typeof(TeleporterInteraction), "OnEnable")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void OnEnablePrefix(TeleporterInteraction __instance)
        {
            SilenceSingleton(__instance);

            var primary = MultiTeleporterTracker.GetPrimary();
            if (primary != null && primary != __instance)
            {
                // Mark as pending init for FixedUpdate suppression
                MultiTeleporterTracker.MarkPendingInit(__instance);

                // Ensure the internal state machine field is not null.
                var currentesm = (EntityStateMachine)ReflectionCache.TeleporterInteraction.MainStateMachine.GetValue(__instance);
                if (currentesm == null)
                {
                    var esm = __instance.GetComponents<EntityStateMachine>()
                        .FirstOrDefault(x => x.customName == "Body") ?? __instance.GetComponent<EntityStateMachine>();

                    if (esm != null)
                    {
                        ReflectionCache.TeleporterInteraction.MainStateMachine.SetValue(__instance, esm);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Debug($"[TeleporterPatches] Restored mainStateMachine field for {__instance.gameObject.name} via Reflection");
                        }
                    }
                }
            }
        }

        // restore singleton.
        [HarmonyPatch(typeof(TeleporterInteraction), "OnEnable")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPostfix]
        private static void OnEnablePostfix(TeleporterInteraction __instance)
        {
            RestoreSingleton();
        }

        // Tracing why rewards might fail even if bosses spawn.
        [HarmonyPatch(typeof(TeleporterInteraction), "OnInteractionBegin")]
        [HarmonyPrefix]
        private static void OnInteractionBeginPrefix(TeleporterInteraction __instance)
        {
            var bossGroup = __instance.GetComponent<BossGroup>();
            if (bossGroup != null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                var tables = (System.Collections.Generic.List<PickupDropTable>?)ReflectionCache.BossGroup.BossDropTables?.GetValue(bossGroup);
                Log.Debug($"[TeleporterPatches] Teleporter Activated! BossGroup: tables={tables?.Count ?? -1}, dropTable={bossGroup.dropTable?.name ?? "null"}, bonusRewards={bossGroup.bonusRewardCount}, shrineBonusStacks={__instance.shrineBonusStacks}, squadExists={bossGroup.combatSquad != null}");
            }
        }

        [HarmonyPatch(typeof(BossGroup), "OnMemberDiscovered")]
        [HarmonyPrefix]
        private static void OnBossMemberDiscoveredPrefix(BossGroup __instance, CharacterMaster memberMaster)
        {
            if (__instance == null || memberMaster == null || !PluginConfig.Instance.EnableDebugLogs.Value) return;
            Log.Debug($"[TeleporterPatches] Boss Discovered: {memberMaster.name}. Total members in squad: {__instance.combatSquad.readOnlyMembersList.Count}");
        }

        [HarmonyPatch(typeof(BossGroup), "OnDefeatedServer")]
        [HarmonyPrefix]
        private static void OnBossDefeatedServerPrefix(BossGroup __instance)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[TeleporterPatches] BossGroup {__instance.name} defeated! bonusRewardCount={__instance.bonusRewardCount}, dropTable={__instance.dropTable?.name ?? "null"}. Logic handled by safety patches.");
        }

        // allow rewards but block portal for secondaries.
        [HarmonyPatch(typeof(TeleporterInteraction.ChargedState), "OnEnter")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        private static bool ChargedStateOnEnterPrefix(EntityStates.BaseState __instance)
        {
            var teleporter = __instance.GetComponent<TeleporterInteraction>();
            if (teleporter != null && MultiTeleporterTracker.IsSecondary(teleporter))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($"[TeleporterPatches] Secondary teleporter {teleporter.gameObject.name} charged — allowing rewards, blocking portal.");
                }
            }
            return true;
        }

        // Suppress pinging while the teleporter is inside a bag.
        [HarmonyPatch(typeof(TeleporterInteraction), "PingTeleporter")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        private static bool PingTeleporterPrefix(TeleporterInteraction __instance)
        {
            return !PersistenceManager.IsTeleporterCurrentlyBagged(__instance.gameObject);
        }

        // prevent NRE if bossGroup is null.
        [HarmonyPatch(typeof(TeleporterInteraction), "UpdateMonstersClear")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        private static bool UpdateMonstersClearPrefix(TeleporterInteraction __instance)
        {
            var bossGroup = (BossGroup)ReflectionCache.TeleporterInteraction.BossGroup.GetValue(__instance);
            return bossGroup != null;
        }

        // prevent NRE if destroyed.
        [HarmonyPatch(typeof(BossGroup), "OnMemberDiscovered")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        private static bool OnMemberDiscoveredPrefix(BossGroup __instance)
        {
            // If the BossGroup is null or being destroyed, ignore the member discovery
            return __instance != null && __instance.gameObject != null;
        }

        // prevent crash in UpdateMonstersClear.
        [HarmonyPatch(typeof(TeleporterInteraction.ChargingState), "FixedUpdate")]
        [HarmonyPatch(MethodType.Normal)]
        [HarmonyPrefix]
        private static bool ChargingStateFixedUpdatePrefix(EntityStates.BaseState __instance)
        {
            var teleporter = __instance.GetComponent<TeleporterInteraction>();
            if (teleporter != null)
            {
                var bossGroup = (BossGroup)ReflectionCache.TeleporterInteraction.BossGroup.GetValue(teleporter);
                if (bossGroup == null) return false; // Block FixedUpdate to prevent UpdateMonstersClear NRE
            }
            return true;
        }

        // Verify that BossGroup has valid combatSquad and subscription
        private static bool VerifyBossGroupSubscription(RoR2.BossGroup bossGroup)
        {
            // Verify BossGroup has a valid combatSquad reference
            if (bossGroup.combatSquad == null)
            {
                Log.Error($"[TeleporterPatches] BossGroup {bossGroup.name} has null combatSquad!");
                return false;
            }

            // Verify that squad exists and is not destroyed
            if (bossGroup.combatSquad.gameObject == null)
            {
                Log.Error($"[TeleporterPatches] BossGroup {bossGroup.name} has destroyed combatSquad!");
                return false;
            }

            // Verify that squad is not in a destroyed state
            if (!bossGroup.combatSquad.isActiveAndEnabled)
            {
                Log.Warning($"[TeleporterPatches] BossGroup {bossGroup.name} combatSquad is not active!");
                return false;
            }

            return true;
        }

        // Verify that CombatDirector credits persist after restoration
        private static System.Collections.IEnumerator VerifyCreditsPersist(CombatDirector director, float expectedCredits)
        {
            // Check immediately
            float creditsImmediately = director.monsterCredit;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[TeleporterPatches.Director] Credit check IMMEDIATELY after add - credits={creditsImmediately}, expected≥{expectedCredits:F0}");

            if (creditsImmediately < expectedCredits * 0.9f)
            {
                Log.Error($"[TeleporterPatches.Director] CRITICAL: Credits already lost IMMEDIATELY after adding! Expected ≥{expectedCredits * 0.9f:F0}, got {creditsImmediately:F0}");
            }

            // Wait for next frame to see if vanilla code resets credits
            yield return new WaitForEndOfFrame();

            float creditsNextFrame = director.monsterCredit;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[TeleporterPatches.Director] Credit check next frame - credits={creditsNextFrame}");

            if (creditsNextFrame != creditsImmediately)
            {
                Log.Error($"[TeleporterPatches.Director] CRITICAL: Credits changed between frames! {creditsImmediately:F0} → {creditsNextFrame:F0}");
            }

            // Wait 0.5 seconds to see if credits persist
            yield return new WaitForSeconds(0.5f);

            float creditsAfterDelay = director.monsterCredit;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[TeleporterPatches.Director] Credit check after 0.5s - credits={creditsAfterDelay}");

            if (creditsAfterDelay < expectedCredits * 0.9f)
            {
                Log.Error($"[TeleporterPatches.Director] CRITICAL: Credits were lost! Expected ≥{expectedCredits * 0.9f:F0}, got {creditsAfterDelay:F0}");
                // Clear restoration flag even on failure to prevent blocking other restorations
                CombatDirectorPatches.ClearTeleporterDirectorRestoring(director);
            }
            else
            {
                // Credits persisted successfully, clear the restoration flag
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Debug($"[TeleporterPatches.Director] Credits verified and persisting ({creditsAfterDelay:F0}), clearing restoration flag");
                CombatDirectorPatches.ClearTeleporterDirectorRestoring(director);
            }
        }

        // Track when combatSquadInstanceId is synced to catch squad assignment issues
        [HarmonyPatch(typeof(CharacterMaster), "OnSyncCombatSquadInstanceId")]
        [HarmonyPrefix]
        private static void OnSyncCombatSquadInstanceIdPrefix(CharacterMaster __instance, NetworkInstanceId ___combatSquadInstanceId)
        {
            if (___combatSquadInstanceId == NetworkInstanceId.Invalid)
            {
                // Check if this is a boss spawned by a teleporter
                if (__instance.isBoss)
                {
                    Log.Warning($"[TeleporterDiagnostics] Boss {__instance.name} is being assigned Invalid squad ID! This will prevent tracking.");
                }
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[TeleporterDiagnostics] {__instance.name} assigned to squad {___combatSquadInstanceId}");
            }
        }

        // Track when monsters are added to squads
        [HarmonyPatch(typeof(CombatSquad), nameof(CombatSquad.MemberDiscovered))]
        [HarmonyPrefix]
        private static void MemberDiscoveredPrefix(CombatSquad __instance, CharacterMaster memberMaster)
        {
            // Check if this squad belongs to a teleporter's BossGroup
            var bossGroup = __instance.GetComponent<RoR2.BossGroup>();
            if (bossGroup != null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[TeleporterDiagnostics] Squad of {bossGroup.name} discovered member: {memberMaster.name}");
            }
        }

        // Check that spawned bosses are properly added to squad
        [HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.Spawn))]
        [HarmonyPostfix]
        private static void SpawnPostfix(CombatDirector __instance, bool __result, SpawnCard spawnCard)
        {
            if (!__result) return;

            // Check all spawns from teleporter directors
            try
            {
                var directorSquad = ReflectionCache.CombatDirector.CombatSquad?.GetValue(__instance) as CombatSquad;

                // Only log for teleporter directors (they're the ones with BossGroup components)
                if (__instance.gameObject.GetComponent<RoR2.BossGroup>() != null)
                {
                    if (directorSquad == null)
                    {
                        Log.Error($"[TeleporterDiagnostics] Spawn by {__instance.name} but director.combatSquad is NULL! Spawned monster will not be tracked!");
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[TeleporterDiagnostics] Spawn by {__instance.name} using squad {directorSquad.netId}, squadMembers={directorSquad.memberCount}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[TeleporterDiagnostics] Spawn verification failed: {ex.Message}");
            }
        }

        // Ensure BossGroup re-subscribes to CombatSquad after persistence
        [HarmonyPatch(typeof(CombatSquad), "Awake")]
        [HarmonyPostfix]
        private static void CombatSquadAwakePostfix(CombatSquad __instance)
        {
            // Check if this CombatSquad belongs to a BossGroup
            var bossGroup = __instance.GetComponent<RoR2.BossGroup>();
            if (bossGroup != null && PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Debug($"[TeleporterDiagnostics] CombatSquad awakened with BossGroup {bossGroup.name}, netId={__instance.netId}");

                // Verify BossGroup subscription is active by checking component state
                if (!bossGroup.isActiveAndEnabled)
                {
                    Log.Warning($"[TeleporterDiagnostics] BossGroup {bossGroup.name} is not active when CombatSquad awakened!");
                }
            }
        }
    }
}
