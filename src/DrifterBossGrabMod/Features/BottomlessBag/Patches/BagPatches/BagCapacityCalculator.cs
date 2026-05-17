#nullable enable
using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Balance;

namespace DrifterBossGrabMod.Patches
{

    public static class BagCapacityCalculator
    {

        private static readonly Dictionary<string, float> _capacityVarsBuffer = new Dictionary<string, float>();
        private static readonly HashSet<int> _countedInstanceIdsBuffer = new HashSet<int>();
        public static int GetUtilityMaxStock(DrifterBagController? drifterBagController, GameObject? incomingObject = null)
        {
            if (drifterBagController == null || !PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                return Constants.Limits.SingleCapacity;
            }

            var vars = _capacityVarsBuffer;
            vars.Clear();

            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                vars["H"] = body.maxHealth;
                vars["L"] = body.level;
                vars["C"] = body.skillLocator.utility.maxStock;
                vars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
            }
            else
            {
                vars["H"] = 0;
                vars["L"] = 0;
                vars["C"] = 0;
                vars["S"] = RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1;
            }

            int slotCapacity = Balance.FormulaParser.EvaluateInt(
                PluginConfig.Instance.SlotScalingFormula.Value, vars);

            if (PluginConfig.Instance.OverencumbranceMax.Value > 0 && PluginConfig.Instance.EnableBalance.Value)
            {

                int usedCapacity = GetCurrentBaggedCount(drifterBagController);
                float totalMass = CalculateTotalBagMass(drifterBagController, incomingObject);

                float maxMassCapacity = CapacityScalingSystem.CalculateMaxMassCapacity(drifterBagController);

                if (totalMass >= maxMassCapacity)
                {

                    slotCapacity = Math.Max(1, usedCapacity);
                }
            }

            return slotCapacity;
        }

        public static float CalculateTotalBagMass(DrifterBagController drifterBagController, GameObject? incomingObject = null)
        {
            if (drifterBagController == null) return 0f;

            float totalMass = drifterBagController.baggedMass;

            GameObject? predictiveIncomingObject = incomingObject;
            if (predictiveIncomingObject == null)
            {
                predictiveIncomingObject = BagPatches.GetState(drifterBagController).IncomingObject;
            }

            if (predictiveIncomingObject != null && !ProjectileRecoveryPatches.IsInProjectileState(predictiveIncomingObject))
            {
                totalMass += drifterBagController.CalculateBaggedObjectMass(predictiveIncomingObject);
            }

            return totalMass;
        }

        public static int GetCurrentBaggedCount(DrifterBagController? controller)
        {
            if (controller == null) return 0;

            var netController = controller.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController != null)
            {
                return netController.GetTotalObjectCount();
            }

            var list = BagPatches.GetState(controller).BaggedObjects;
            if (list == null)
            {
                return 0;
            }

            int objectsInBag = 0;
            var countedInstanceIds = _countedInstanceIdsBuffer;
            countedInstanceIds.Clear();

            foreach (var obj in list)
            {
                if (obj != null && !ProjectileRecoveryPatches.IsInProjectileState(obj))
                {
                    int instanceId = obj.GetInstanceID();
                    if (!countedInstanceIds.Contains(instanceId))
                    {
                        countedInstanceIds.Add(instanceId);
                        objectsInBag++;
                    }
                }
            }

            return objectsInBag;
        }

        public static bool HasRoomForGrab(DrifterBagController controller)
        {
            if (controller == null) return false;

            int effectiveCapacity = GetUtilityMaxStock(controller, null);
            int currentCount = GetCurrentBaggedCount(controller);
            bool hasRoom = currentCount < effectiveCapacity;

            if (!hasRoom)
            {
                API.DrifterBagAPI.InvokeOnBagFull(controller);
            }
            return hasRoom;
        }

        public static float GetBaggedObjectMass(DrifterBagController controller)
        {
            if (controller == null) return 0f;
            return controller.baggedMass;
        }
    }
}
