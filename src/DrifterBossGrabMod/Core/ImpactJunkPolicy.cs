#nullable enable
using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Core
{
    public static class ImpactJunkPolicy
    {
        private static readonly HashSet<Type> _excludedComponents = new()
        {
            typeof(TrialGongInteraction),
            typeof(MimicController),
            typeof(WakeDonationController)
        };

        public static void Exclude<T>() where T : Component => _excludedComponents.Add(typeof(T));

        public static bool AllowsReward(GameObject? passenger)
        {
            if (passenger == null) return true;
            foreach (var component in _excludedComponents)
                if (passenger.GetComponent(component) != null) return false;
            return true;
        }
    }
}
