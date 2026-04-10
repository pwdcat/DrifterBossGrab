#nullable enable
using HarmonyLib;
using RoR2;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch(typeof(HealthComponent), "OnDeath")]
    public class HealthComponent_OnDeath
    {
        [HarmonyPostfix]
        public static void Postfix(HealthComponent __instance)
        {
            if (__instance == null) return;

            var obj = __instance.gameObject;
            if (obj == null) return;

            // Check if this is a bagged object
            var tracker = obj.GetComponent<BaggedObjectTracker>();
            if (tracker == null) return;

            var controller = tracker.controller;
            if (controller == null) return;

            // Skip during passenger swaps
            if (DrifterBossGrabPlugin.IsSwappingPassengers) return;

            // Cleanup bag tracking immediately on death
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[HealthComponent_OnDeath] Bagged object {obj.name} died, cleaning up bag tracking immediately");
            }

            BagPassengerManager.RemoveBaggedObject(controller, obj, isDestroying: true);
        }
    }
}
