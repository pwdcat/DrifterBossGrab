#nullable enable
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Patches
{
    [HarmonyPatch]
    public static class TeleporterSafetyPatches
    {
        private static readonly ConditionalWeakTable<TeleporterInteraction, object> _restored = new();

        public static void MarkRestored(TeleporterInteraction teleporter)
        {
            _restored.GetValue(teleporter, _ => new object());
        }

        internal static bool IsRestored(Component component)
        {
            var teleporter = component.GetComponentInParent<TeleporterInteraction>();
            return teleporter != null && _restored.TryGetValue(teleporter, out _);
        }

        private static Exception? PreserveUnrelatedException(OutsideInteractableLocker locker, Exception? exception)
        {
            if (exception == null || !IsRestored(locker)) return exception;
            if (!(exception is NullReferenceException) && !(exception is MissingReferenceException)) return exception;
            var origin = exception.TargetSite?.DeclaringType;
            if (origin != typeof(OutsideInteractableLocker) && origin?.DeclaringType != typeof(OutsideInteractableLocker))
                return exception;
            return null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(OutsideInteractableLocker), "FixedUpdate")]
        public static Exception? LockerFixedUpdateFinalizer(OutsideInteractableLocker __instance, Exception? __exception)
            => PreserveUnrelatedException(__instance, __exception);

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(OutsideInteractableLocker), "OnDisable")]
        public static Exception? LockerOnDisableFinalizer(OutsideInteractableLocker __instance, Exception? __exception)
            => PreserveUnrelatedException(__instance, __exception);

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(OutsideInteractableLocker), "UnlockAll")]
        public static Exception? LockerUnlockAllFinalizer(OutsideInteractableLocker __instance, Exception? __exception)
            => PreserveUnrelatedException(__instance, __exception);
    }
}
