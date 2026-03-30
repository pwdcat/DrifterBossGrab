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
        // ThrownObjectProjectileController members
        public static class ThrownObjectProjectileController
        {
            public static readonly FieldInfo ProjectileController = AccessTools.Field(typeof(RoR2.Projectile.ThrownObjectProjectileController), "projectileController");
            public static readonly FieldInfo VehicleSeat = AccessTools.Field(typeof(RoR2.Projectile.ThrownObjectProjectileController), "vehicleSeat");
            public static readonly MethodInfo CalculatePassengerFinalPosition = AccessTools.Method(typeof(RoR2.Projectile.ThrownObjectProjectileController), "CalculatePassengerFinalPosition");
        }

        // BaggedObject members - Use fully qualified type name
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

        // DrifterBagController members
        public static class DrifterBagController
        {
            public static readonly FieldInfo BaggedMass = AccessTools.Field(typeof(RoR2.DrifterBagController), "baggedMass");
            public static readonly MethodInfo OnSyncBaggedObject = AccessTools.Method(typeof(RoR2.DrifterBagController), "OnSyncBaggedObject", new Type[] { typeof(GameObject) });
            public static readonly FieldInfo JunkController = AccessTools.Field(typeof(RoR2.DrifterBagController), "junkController");
        }
        
        // ProjectileStickOnImpact members
        public static class ProjectileStickOnImpact
        {
            public static readonly FieldInfo RunStickEvent = AccessTools.Field(typeof(RoR2.Projectile.ProjectileStickOnImpact), "runStickEvent");
            public static readonly FieldInfo AlreadyRanStickEvent = AccessTools.Field(typeof(RoR2.Projectile.ProjectileStickOnImpact), "alreadyRanStickEvent");
        }
        
        // SpecialObjectAttributes members
        public static class SpecialObjectAttributes
        {
            public static readonly FieldInfo CollisionToDisable = typeof(SpecialObjectAttributes).GetField("collisionToDisable", BindingFlags.Public | BindingFlags.Instance);
            public static readonly FieldInfo TargetObject = typeof(SpecialObjectAttributes).GetField("targetObject", BindingFlags.Public | BindingFlags.Instance);
            public static readonly FieldInfo CollidersToDisable = typeof(SpecialObjectAttributes).GetField("collidersToDisable", BindingFlags.NonPublic | BindingFlags.Instance);
            public static readonly FieldInfo BehavioursToDisable = typeof(SpecialObjectAttributes).GetField("behavioursToDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        
        // HackingMainState members
        public static class HackingMainState
        {
            public static readonly FieldInfo SphereSearch = typeof(HackingMainState).GetField("sphereSearch", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        
        // EntityState members
        public static class EntityState
        {
            public static readonly PropertyInfo FixedAge = AccessTools.Property(typeof(EntityState), "fixedAge");
        }
        
        // GenericSkill members
        public static class GenericSkill
        {
            public static readonly FieldInfo SkillOverrides = AccessTools.Field(typeof(GenericSkill), "skillOverrides");
        }
        
        // BaggedObject (duplicate entries for different patches - these are the same as above but I'll keep them for now)
        public static class Misc
        {
            public static readonly MethodInfo OnUIOverlayInstanceRemove = AccessTools.Method(typeof(BaggedObject), "OnUIOverlayInstanceRemove");
        }

        // Additional BaggedObject fields not listed above but used in the codebase
        public static class BaggedObjectAdditional
        {
            private static readonly Type BaggedObjectType = System.Type.GetType("EntityStates.Drifter.Bag.BaggedObject, RoR2") ?? typeof(EntityStates.Drifter.Bag.BaggedObject);

            public static readonly FieldInfo MovespeedPenalty = AccessTools.Field(BaggedObjectType, "movespeedPenalty");
            public static readonly FieldInfo? AttackSpeedStat = null;
            public static readonly FieldInfo? DamageStat = null;
            public static readonly FieldInfo? CritStat = null;
            public static readonly FieldInfo? MoveSpeedStat = null;
        }

        // RepossessExit members
        public static class RepossessExit
        {
            public static readonly FieldInfo ChosenTarget = AccessTools.Field(typeof(EntityStates.Drifter.RepossessExit), "chosenTarget");
            public static readonly FieldInfo ActivatedHitpause = AccessTools.Field(typeof(EntityStates.Drifter.RepossessExit), "activatedHitpause");
        }
    }
}
