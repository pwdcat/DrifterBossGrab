using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drifter;
namespace DrifterBossGrabMod.Patches
{
    public static class RepossessExitPatches
    {
        private static void DumpGrabbingComponents(GameObject obj, string context)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value || obj == null) return;
            Log.Info($" === DUMPING COMPONENTS FOR {obj.name} ({context}) ===");
            // EntityStateMachine
            var esm = obj.GetComponent<EntityStateMachine>();
            if (esm != null)
            {
                Log.Info($" EntityStateMachine:");
                Log.Info($"   customName: '{esm.customName}'");
                Log.Info($"   initialStateType: {esm.initialStateType}");
                Log.Info($"   mainStateType: {esm.mainStateType}");
                Log.Info($"   networkIndex: {esm.networkIndex}");
                Log.Info($"   state: {esm.state}");
                Log.Info($"   AllowStartWithoutNetworker: {esm.AllowStartWithoutNetworker}");
                // Get all fields via reflection
                var esmFields = typeof(EntityStateMachine).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in esmFields)
                {
                    try
                    {
                        var value = field.GetValue(esm);
                        Log.Info($"   {field.Name}: {value}");
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"   {field.Name}: <error getting value: {ex.Message}>");
                    }
                }
            }
            else
            {
                Log.Info($" EntityStateMachine: NOT FOUND");
            }
            // NetworkStateMachine
            var nsm = obj.GetComponent<NetworkStateMachine>();
            if (nsm != null)
            {
                Log.Info($" NetworkStateMachine:");
                // Get all fields via reflection
                var nsmFields = typeof(NetworkStateMachine).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in nsmFields)
                {
                    try
                    {
                        var value = field.GetValue(nsm);
                        if (field.FieldType.IsArray && value != null)
                        {
                            // Handle arrays
                            var array = (System.Array)value;
                            Log.Info($"   {field.Name}: Array[{array.Length}]");
                            for (int i = 0; i < array.Length; i++)
                            {
                                Log.Info($"     [{i}]: {array.GetValue(i)}");
                            }
                        }
                        else
                        {
                            Log.Info($"   {field.Name}: {value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"   {field.Name}: <error getting value: {ex.Message}>");
                    }
                }
            }
            else
            {
                Log.Info($" NetworkStateMachine: NOT FOUND");
            }
            // NetworkIdentity
            var ni = obj.GetComponent<NetworkIdentity>();
            if (ni != null)
            {
                Log.Info($" NetworkIdentity:");
                Log.Info($"   netId: {ni.netId}");
                Log.Info($"   isServer: {ni.isServer}");
                Log.Info($"   isClient: {ni.isClient}");
                Log.Info($"   hasAuthority: {ni.hasAuthority}");
                Log.Info($"   isLocalPlayer: {ni.isLocalPlayer}");
                Log.Info($"   serverOnly: {ni.serverOnly}");
                Log.Info($"   localPlayerAuthority: {ni.localPlayerAuthority}");
                // Get all fields via reflection
                var niFields = typeof(NetworkIdentity).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in niFields)
                {
                    try
                    {
                        var value = field.GetValue(ni);
                        Log.Info($"   {field.Name}: {value}");
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"   {field.Name}: <error getting value: {ex.Message}>");
                    }
                }
            }
            else
            {
                Log.Info($" NetworkIdentity: NOT FOUND");
            }
            Log.Info($" === END COMPONENT DUMP ===");
        }
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                try
                {
                    var traverse = Traverse.Create(__instance);
                    GameObject targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                    if (targetObject == null) return;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        var targetName = targetObject.name;
                        Log.Info($" BaggedObject.OnEnter: targetObject = {targetName} ({targetObject})");
                        DumpGrabbingComponents(targetObject, "BaggedObject.OnEnter");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($" Error in BaggedObject.OnEnter debug logging: {ex.Message}");
                }
            }
        }
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnExit")]
        public class BaggedObject_OnExit_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                try
                {
                    var traverse = Traverse.Create(__instance);
                    GameObject targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                    if (targetObject == null) return;
                    // Re-enable Rigidbody for released objects
                    var rb = targetObject.GetComponent<Rigidbody>();
                    if (rb)
                    {
                        rb.isKinematic = false;
                        rb.detectCollisions = true;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Restored Rigidbody on {targetObject.name} (bag exit)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($" Error in BaggedObject.OnExit restoration: {ex.Message}");
                }
            }
        }
        [HarmonyPatch(typeof(RepossessExit), "OnEnter")]
        public class RepossessExit_OnEnter_Patch
        {
            private static GameObject? originalChosenTarget;
            [HarmonyPrefix]
            public static void Prefix(RepossessExit __instance)
            {
                var traverse = Traverse.Create(__instance);
                originalChosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" RepossessExit Prefix: originalChosenTarget = {originalChosenTarget}");
                }
            }
            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance)
            {
                // Only apply grabbing logic if any grabbing type is enabled
                if (!PluginConfig.Instance.EnableBossGrabbing.Value && !PluginConfig.Instance.EnableNPCGrabbing.Value)
                    return;
                var traverse = Traverse.Create(__instance);
                var chosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" RepossessExit Postfix: chosenTarget = {chosenTarget}, originalChosenTarget = {originalChosenTarget}");
                }
                // If chosenTarget was rejected but it's grabbable, allow it
                if (chosenTarget == null && originalChosenTarget != null && PluginConfig.IsGrabbable(originalChosenTarget))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Allowing grab for grabbable object: {originalChosenTarget.name}");
                        DumpGrabbingComponents(originalChosenTarget, "grabbable object override");
                    }
                    traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                    traverse.Field("activatedHitpause").SetValue(true);
                }
                else if (chosenTarget == null && originalChosenTarget != null)
                {
                    var component2 = originalChosenTarget.GetComponent<CharacterBody>();
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Checking body: {component2}, ungrabbable: {component2 && component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}");
                    }
                    if (component2)
                    {
                        bool isBoss = component2.master && component2.master.isBoss;
                        bool isElite = component2.isElite;
                        bool isUngrabbable = component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                        bool canGrab = (PluginConfig.Instance.EnableBossGrabbing.Value && isBoss) ||
                                        (PluginConfig.Instance.EnableNPCGrabbing.Value && isUngrabbable);
                        bool isBlacklisted = PluginConfig.IsBlacklisted(component2.name);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Body {component2.name}: isBoss={isBoss}, isElite={isElite}, ungrabbable={isUngrabbable}, canGrab={canGrab}, isBlacklisted={isBlacklisted}");
                        }
                        if (canGrab && !isBlacklisted)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                var bodyName = component2.name;
                                Log.Info($" Allowing grab for {bodyName}");
                                DumpGrabbingComponents(originalChosenTarget, "boss/NPC grab");
                            }
                            traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                            traverse.Field("activatedHitpause").SetValue(true);
                        }
                    }
                }
            }
        }
    }
}
