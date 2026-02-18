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
    // Main class for Bottomless Bag patches - entry points for input handling and passenger cycling
    public static class BottomlessBagPatches
    {
        // Processes input for cycling through bag passengers
        public static void HandleInput()
        {
            CyclingInputHandler.HandleInput();
        }

        // Cycles through passengers in the bag by the specified amount
        public static void CyclePassengers(DrifterBagController bagController, int amount)
        {
            PassengerCycler.CyclePassengers(bagController, amount);
        }

        // Server-side implementation of cycling
        public static void ServerCyclePassengers(DrifterBagController bagController, int amount)
        {
            PassengerCycler.ServerCyclePassengers(bagController, amount);
        }
    }
}
