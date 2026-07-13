using System;
using System.Reflection;
using RoR2;
using RoR2.Projectile;
using HarmonyLib;
using UnityEngine;
using EntityStates;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod
{
    public static class ReflectionCache
    {

        public static class ThrownObjectProjectileController
        {
            public static readonly FieldInfo ProjectileController = AccessTools.Field(typeof(RoR2.Projectile.ThrownObjectProjectileController), "projectileController");
            public static readonly MethodInfo CalculatePassengerFinalPosition = AccessTools.Method(typeof(RoR2.Projectile.ThrownObjectProjectileController), "CalculatePassengerFinalPosition");
        }

        public static class BaggedObject
        {
            private static readonly Type BaggedObjectType = System.Type.GetType("EntityStates.Drifter.Bag.BaggedObject, RoR2") ?? typeof(EntityStates.Drifter.Bag.BaggedObject);

            public static readonly FieldInfo TargetObject = AccessTools.Field(BaggedObjectType, "targetObject");
            public static readonly FieldInfo TargetBody = AccessTools.Field(BaggedObjectType, "targetBody");
            public static readonly FieldInfo IsBody = AccessTools.Field(BaggedObjectType, "isBody");
            public static readonly MethodInfo HoldsDeadBody = AccessTools.Method(BaggedObjectType, "HoldsDeadBody");
            public static readonly FieldInfo VehiclePassengerAttributes = AccessTools.Field(BaggedObjectType, "vehiclePassengerAttributes");
            public static readonly FieldInfo BaggedMass = AccessTools.Field(BaggedObjectType, "baggedMass");
            public static readonly FieldInfo UIOverlayController = AccessTools.Field(BaggedObjectType, "uiOverlayController");
            public static readonly FieldInfo OverriddenUtility = AccessTools.Field(BaggedObjectType, "overriddenUtility");
            public static readonly FieldInfo OverriddenPrimary = AccessTools.Field(BaggedObjectType, "overriddenPrimary");
            public static readonly FieldInfo UtilityOverride = AccessTools.Field(BaggedObjectType, "utilityOverride");
            public static readonly FieldInfo PrimaryOverride = AccessTools.Field(BaggedObjectType, "primaryOverride");
            public static readonly FieldInfo BreakoutTime = AccessTools.Field(BaggedObjectType, "breakoutTime");
            public static readonly FieldInfo BreakoutAttempts = AccessTools.Field(BaggedObjectType, "breakoutAttempts");
            public static readonly MethodInfo TryOverrideUtility = AccessTools.Method(BaggedObjectType, "TryOverrideUtility");
            public static readonly MethodInfo TryOverridePrimary = AccessTools.Method(BaggedObjectType, "TryOverridePrimary");
            public static readonly FieldInfo BagScale01 = AccessTools.Field(BaggedObjectType, "bagScale01");
            public static readonly MethodInfo SetScale = AccessTools.Method(BaggedObjectType, "SetScale", new Type[] { typeof(float) });
            public static readonly FieldInfo DrifterBagController = AccessTools.Field(BaggedObjectType, "drifterBagController");
            public static readonly FieldInfo WalkSpeedModifier = AccessTools.Field(BaggedObjectType, "walkSpeedModifier");
        }

        public static class DrifterBagController
        {
            public static readonly FieldInfo BaggedMass = AccessTools.Field(typeof(RoR2.DrifterBagController), "baggedMass");
            public static readonly MethodInfo OnSyncBaggedObject = AccessTools.Method(typeof(RoR2.DrifterBagController), "OnSyncBaggedObject", new Type[] { typeof(GameObject) });
            public static readonly FieldInfo JunkController = AccessTools.Field(typeof(RoR2.DrifterBagController), "junkController");
            public static readonly FieldInfo Smacks = AccessTools.Field(typeof(RoR2.DrifterBagController), "smacks");
        }

        public static class SpecialObjectAttributes
        {
            public static readonly FieldInfo CollidersToDisable = typeof(RoR2.SpecialObjectAttributes).GetField("collidersList", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static class HackingMainState
        {
            public static readonly FieldInfo SphereSearch = typeof(EntityStates.CaptainSupplyDrop.HackingMainState).GetField("sphereSearch", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static class EntityState
        {
            public static readonly PropertyInfo FixedAge = AccessTools.Property(typeof(EntityStates.EntityState), "fixedAge");
        }

        public static class GenericSkill
        {
            public static readonly FieldInfo SkillOverrides = AccessTools.Field(typeof(RoR2.GenericSkill), "skillOverrides");
            public static readonly FieldInfo OnSkillChangedBackingField = AccessTools.Field(typeof(RoR2.GenericSkill), "onSkillChanged");
        }

        public static class Misc
        {
            public static readonly MethodInfo OnUIOverlayInstanceRemove = AccessTools.Method(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnUIOverlayInstanceRemove");
        }

        public static class EmptyBag
        {
            public static readonly FieldInfo ProjectileBaseSpeed = AccessTools.Field(typeof(EntityStates.AimThrowableBase), "projectileBaseSpeed");
        }

        public static class BaggedObjectAdditional
        {
            private static readonly Type BaggedObjectType = System.Type.GetType("EntityStates.Drifter.Bag.BaggedObject, RoR2") ?? typeof(EntityStates.Drifter.Bag.BaggedObject);

            public static readonly FieldInfo MovespeedPenalty = AccessTools.Field(BaggedObjectType, "movespeedPenalty");
            public static readonly FieldInfo? AttackSpeedStat = null;
            public static readonly FieldInfo? DamageStat = null;
            public static readonly FieldInfo? CritStat = null;
            public static readonly FieldInfo? MoveSpeedStat = null;
        }

        public static class RepossessExit
        {
            public static readonly FieldInfo ChosenTarget = AccessTools.Field(typeof(EntityStates.Drifter.RepossessExit), "chosenTarget");
            public static readonly FieldInfo ActivatedHitpause = AccessTools.Field(typeof(EntityStates.Drifter.RepossessExit), "activatedHitpause");
        }

        public static class JunkCubeController
        {
            private static FieldInfo? _maxActivationCount;

            public static FieldInfo? MaxActivationCount
            {
                get
                {
                    if (_maxActivationCount == null)
                    {
                        var junkCubeType = Type.GetType("RoR2.JunkCubeController, RoR2");
                        if (junkCubeType != null)
                        {
                            _maxActivationCount = AccessTools.Field(junkCubeType, "_maxActivationCount");
                        }
                    }
                    return _maxActivationCount;
                }
            }
        }

        public static class NetworkIdentity
        {
            public static readonly FieldInfo AssetId = AccessTools.Field(typeof(UnityEngine.Networking.NetworkIdentity), "m_AssetId");
        }

        public static class Rewired
        {
            public static class ActionElementMap
            {
                private static MethodInfo? _applyToControllerMapMethod;

                public static MethodInfo? GetApplyToControllerMapMethod()
                {
                    if (_applyToControllerMapMethod == null)
                    {
                        var actionElementMapType = Type.GetType("Rewired.Data.Mapping.ActionElementMap, Rewired_Core");
                        if (actionElementMapType != null)
                        {
                            foreach (var method in actionElementMapType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                if (method.ReturnType == typeof(void))
                                {
                                    var parameters = method.GetParameters();
                                    if (parameters.Length == 1)
                                    {
                                        var controllerMapType = Type.GetType("Rewired.ControllerMap, Rewired_Core");
                                        if (controllerMapType != null && controllerMapType.IsAssignableFrom(parameters[0].ParameterType))
                                        {
                                            _applyToControllerMapMethod = method;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return _applyToControllerMapMethod;
                }
            }
        }

        public static class TeleporterInteraction
        {
            public static readonly FieldInfo BossGroup = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "bossGroup");
            public static readonly FieldInfo MainStateMachine = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "mainStateMachine");
            public static readonly FieldInfo BossDirector = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "bossDirector");
            public static readonly FieldInfo MonstersCleared = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "monstersCleared");
            public static readonly FieldInfo PositionIndicator = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "teleporterPositionIndicator");
            public static readonly FieldInfo BossShrineCounter = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "_bossShrineCounter");
            public static readonly FieldInfo CachedLocalUser = AccessTools.Field(typeof(RoR2.TeleporterInteraction), "cachedLocalUser");
        }

        public static class CombatDirector
        {
            public static readonly FieldInfo CombatSquad = AccessTools.Field(typeof(RoR2.CombatDirector), "combatSquad");
            public static readonly FieldInfo MonsterCredit = AccessTools.Field(typeof(RoR2.CombatDirector), "monsterCredit");
            public static readonly FieldInfo MinSpawnRange = AccessTools.Field(typeof(RoR2.CombatDirector), "minSpawnRange");
            public static readonly FieldInfo MaxSpawnDistance = AccessTools.Field(typeof(RoR2.CombatDirector), "maxSpawnDistance");
            public static readonly FieldInfo BossOverrideSpawnSingleBoss = AccessTools.Field(typeof(RoR2.CombatDirector), "_bossOverrideSpawnSingleBoss");
        }

        public static class OutsideInteractableLocker
        {
            public static readonly FieldInfo LockObjectMap = AccessTools.Field(typeof(RoR2.OutsideInteractableLocker), "lockObjectMap");
            public static readonly FieldInfo LockInteractableMap = AccessTools.Field(typeof(RoR2.OutsideInteractableLocker), "lockInteractableMap");
        }

        public static class CombatSquad
        {
            public static readonly FieldInfo MembersList = AccessTools.Field(typeof(RoR2.CombatSquad), "membersList");
        }

        public static class HoldoutZoneController
        {
            public static readonly PropertyInfo CurrentRadius = AccessTools.Property(typeof(RoR2.HoldoutZoneController), "currentRadius");
            public static readonly FieldInfo RadiusVelocity = AccessTools.Field(typeof(RoR2.HoldoutZoneController), "radiusVelocity");
        }

        public static class BossGroup
        {
            public static readonly FieldInfo BossMemoryCount = AccessTools.Field(typeof(RoR2.BossGroup), "bossMemoryCount");
            public static readonly MethodInfo ResetBossBar = AccessTools.Method(typeof(RoR2.BossGroup), "ResetBossBar");
            public static readonly FieldInfo BossDropTables = AccessTools.Field(typeof(RoR2.BossGroup), "bossDropTables");
            public static readonly FieldInfo BossDrops = AccessTools.Field(typeof(RoR2.BossGroup), "bossDrops");
            public static readonly FieldInfo BossDropTablesLocked = AccessTools.Field(typeof(RoR2.BossGroup), "bossDropTablesLocked");
            public static readonly FieldInfo rng = AccessTools.Field(typeof(RoR2.BossGroup), "rng");
        }
    }
}