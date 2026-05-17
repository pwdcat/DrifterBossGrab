#nullable enable
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Config;
using EntityStates.Drifter.Bag;

namespace DrifterBossGrabMod.Networking
{
    public static class AuthorityGuard
    {
        public static bool CanModifyBag(DrifterBagController? controller)
        {
            if (controller == null) return false;
            return NetworkServer.active || controller.hasAuthority;
        }

        public static bool ShouldAutoPromote(DrifterBagController? controller)
        {
            if (controller == null) return false;

            var nc = controller.GetComponent<BottomlessBagNetworkController>();
            if (nc != null && NetworkServer.active && !controller.hasAuthority)
            {
                return nc.autoPromoteMainSeat;
            }

            return PluginConfig.Instance.AutoPromoteMainSeat.Value &&
                   (NetworkServer.active || controller.hasAuthority);
        }

        public static bool IsServerWithPassenger(DrifterBagController? controller, GameObject obj)
        {
            return NetworkServer.active
                   && controller != null
                   && controller.vehicleSeat != null
                   && controller.vehicleSeat.NetworkpassengerBodyObject == obj;
        }

        public static bool ShouldSendPersistence(DrifterBagController? controller)
        {
            return NetworkServer.active && controller != null;
        }
    }
}
