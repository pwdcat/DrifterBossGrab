#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{

    public static class BottomlessBagPatches
    {

        public static void HandleInput()
        {
            CyclingInputHandler.HandleInput();
        }

        public static void CyclePassengers(DrifterBagController bagController, int amount)
        {
            PassengerCycler.CyclePassengers(bagController, amount);
        }

        public static void ServerCyclePassengers(DrifterBagController bagController, int amount)
        {
            PassengerCycler.ServerCyclePassengers(bagController, amount);
        }
    }
}
