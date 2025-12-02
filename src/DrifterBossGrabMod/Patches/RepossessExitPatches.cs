using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using EntityStates.Drifter;

namespace DrifterBossGrabMod.Patches
{
    public static class RepossessExitPatches
    {
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
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Restored Rigidbody on {targetObject.name} (bag exit)");
                        }
                    }

                    // Check if this object has GrabbedObjectState (it's being thrown as projectile)
                    var grabbedState = targetObject.GetComponent<GrabbedObjectState>();
                    if (grabbedState != null)
                    {
                        // For environment objects, restore renderers and highlights immediately on throw
                        // Colliders and other states will be restored on projectile impact
                        StateManagement.RestoreRenderers(grabbedState.originalRendererStates);
                        StateManagement.RestoreHighlights(grabbedState.originalHighlightStates);
                        grabbedState.originalRendererStates.Clear();
                        grabbedState.originalHighlightStates.Clear();

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Restored renderers and highlights immediately for thrown {targetObject.name} - other states will restore on impact");
                        }

                        // Set upright rotation for all thrown objects
                        targetObject.transform.rotation = Quaternion.identity;
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Set upright rotation for thrown {targetObject.name}");
                        }
                    }
                    else
                    {
                        // Fallback for objects without GrabbedObjectState (should not happen in normal flow)
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No GrabbedObjectState found for {targetObject.name} - skipping state restoration");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"{Constants.LogPrefix} Error in BaggedObject.OnExit restoration: {ex.Message}");
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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} RepossessExit Prefix: originalChosenTarget = {originalChosenTarget}");
                }
            }

            [HarmonyPostfix]
            public static void Postfix(RepossessExit __instance)
            {
                // Only apply grabbing logic if any grabbing type is enabled
                if (!PluginConfig.EnableBossGrabbing.Value && !PluginConfig.EnableNPCGrabbing.Value)
                    return;

                var traverse = Traverse.Create(__instance);
                var chosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} RepossessExit Postfix: chosenTarget = {chosenTarget}, originalChosenTarget = {originalChosenTarget}");
                }
                
                if (chosenTarget && chosenTarget.GetComponent<IInteractable>() != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Allowing grab for IInteractable: {chosenTarget.name}");
                    }
                    
                    var colliders = chosenTarget.GetComponentsInChildren<Collider>();
                    var grabbedState = chosenTarget.GetComponent<GrabbedObjectState>();
                    
                    // Create GrabbedObjectState early if it doesn't exist yet to store trigger states
                    if (grabbedState == null)
                    {
                        grabbedState = chosenTarget.AddComponent<GrabbedObjectState>();
                    }
                    
                    foreach (var col in colliders)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Found collider {col.name} on {chosenTarget.name}, isTrigger: {col.isTrigger}, enabled: {col.enabled}");
                        }
                        
                        bool originalTrigger = col.isTrigger;
                        
                        if (!grabbedState.originalIsTrigger.ContainsKey(col))
                        {
                            grabbedState.originalIsTrigger[col] = originalTrigger;
                        }
                        
                        if (!col.isTrigger)
                        {
                            col.isTrigger = true;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Set collider {col.name} on {chosenTarget.name} to trigger to prevent collision during grab");
                            }
                        }
                    }
                }
                
                // If chosenTarget was rejected but it's grabbable, allow it
                if (chosenTarget == null && originalChosenTarget != null)
                {
                    var component2 = originalChosenTarget.GetComponent<CharacterBody>();
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Checking body: {component2}, ungrabbable: {component2 && component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}");
                    }
                    
                    if (component2)
                    {
                        bool isBoss = component2.master && component2.master.isBoss;
                        bool isElite = component2.isElite;
                        bool isUngrabbable = component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                        bool canGrab = (PluginConfig.EnableBossGrabbing.Value && isBoss) ||
                                        (PluginConfig.EnableNPCGrabbing.Value && isUngrabbable);
                        bool isBlacklisted = PluginConfig.IsBlacklisted(component2.name);
                        
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Body {component2.name}: isBoss={isBoss}, isElite={isElite}, ungrabbable={isUngrabbable}, canGrab={canGrab}, isBlacklisted={isBlacklisted}");
                        }
                        
                        if (canGrab && !isBlacklisted)
                        {
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Allowing grab for {component2.name}");
                            }
                            
                            traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                            traverse.Field("activatedHitpause").SetValue(true);
                            
                            try
                            {
                                Util.PlaySound(Constants.RepossessSuccessSound, traverse.Field("gameObject").GetValue<GameObject>());
                                traverse.Method("PlayCrossfade", new object[] { 
                                    Constants.FullBodyOverride, 
                                    Constants.SuffocateHit, 
                                    Constants.SuffocatePlaybackRate, 
                                    traverse.Field("duration").GetValue<float>() * 2.5f, 
                                    traverse.Field("duration").GetValue<float>() 
                                });
                                
                                var animator = traverse.Method("GetModelAnimator").GetValue<object>();
                                if (animator != null) 
                                {
                                    Traverse.Create(animator).Field("speed").SetValue(0f);
                                }

                                // Set stored velocity to zero to prevent being pushed around after grabbing
                                var characterMotor = traverse.Field("characterMotor").GetValue<CharacterMotor>();
                                if (characterMotor != null)
                                {
                                    traverse.Field("storedHitPauseVelocity").SetValue(Vector3.zero);
                                }
                                
                                var hitPauseTimer = traverse.Field("hitPauseTimer").GetValue<float>() + RepossessExit.hitPauseDuration;
                                traverse.Field("hitPauseTimer").SetValue(hitPauseTimer);
                                
                                var component3 = originalChosenTarget.GetComponent<SetStateOnHurt>();
                                if (component3 != null && component3.canBeStunned)
                                {
                                    component3.SetStun(RepossessExit.hitPauseDuration);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"{Constants.LogPrefix} Error in grabbing logic: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
    }
}