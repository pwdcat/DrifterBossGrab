using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    public static class CycleNetworkHandler
    {
        private const short MSG_CYCLE_REQUEST = 205;

        public static void Init()
        {
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler(MSG_CYCLE_REQUEST, OnServerReceiveCycleRequest);
            }
        }

        public static void SendCycleRequest(DrifterBagController bagController, bool scrollUp)
        {
            var ni = bagController.GetComponent<NetworkIdentity>();
            if (!ni) return;

            var msg = new CyclePassengersMessage
            {
                bagControllerNetId = ni.netId,
                scrollUp = scrollUp
            };

            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleNetworkHandler] Sent cycle request: netId={ni.netId.Value}, scrollUp={scrollUp}");
            NetworkManager.singleton.client.Send(MSG_CYCLE_REQUEST, msg);
        }

        private static void OnServerReceiveCycleRequest(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<CyclePassengersMessage>();
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleNetworkHandler] Server received cycle request: netId={msg.bagControllerNetId.Value}, scrollUp={msg.scrollUp}");

            var controllerObj = NetworkServer.FindLocalObject(msg.bagControllerNetId);
            if (controllerObj)
            {
                var bagController = controllerObj.GetComponent<DrifterBagController>();
                if (bagController)
                {
                    BottomlessBagPatches.ServerCyclePassengers(bagController, msg.scrollUp);
                }
            }
        }
    }
}
