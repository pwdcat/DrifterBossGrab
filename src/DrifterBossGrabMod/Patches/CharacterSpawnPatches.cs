using HarmonyLib;
using RoR2;
namespace DrifterBossGrabMod.Patches
{
    public static class CharacterSpawnPatches
    {
        [HarmonyPatch(typeof(CharacterMaster), "OnBodyStart")]
        public class CharacterMaster_OnBodyStart
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterMaster __instance, CharacterBody body)
            {
                if (!PluginConfig.EnableObjectPersistence.Value ||
                    !PluginConfig.EnableAutoGrab.Value ||
                    body == null)
                {
                    return;
                }
                // Check if this is a Drifter player respawn
                if (body.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
                {
                    // Schedule auto-grab with delay to ensure bag controller is ready
                    PersistenceManager.ScheduleAutoGrab(__instance);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CharacterMaster_OnBodyStart] Scheduled auto-grab for Drifter respawn");
                    }
                }
                // Detect zone inversion on first player spawn
                Patches.OtherPatches.DetectZoneInversion(body.transform.position);
            }
        }
    }
}