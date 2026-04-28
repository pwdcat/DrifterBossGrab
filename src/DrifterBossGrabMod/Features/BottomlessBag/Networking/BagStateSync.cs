#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using HarmonyLib;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod.Networking
{
    public static class BagStateSync
    {
        public static GameObject? AdditionalSeatPrefab { get; private set; }
        private static Harmony? _harmony;

        public static void Init(Harmony harmony)
        {
            _harmony = harmony;
            RoR2.Networking.NetworkManagerSystem.onClientConnectGlobal += OnClientConnect;
            RoR2.Networking.NetworkManagerSystem.onStartServerGlobal += OnServerStart;
            Run.onRunStartGlobal += OnRunStart;

            BodyCatalog.availability.CallWhenAvailable(() =>
            {
                AddControllerToDrifterPrefab();
            });

            CreateSeatPrefab();
        }

        private static void AddControllerToDrifterPrefab()
        {
            var drifterBody = BodyCatalog.FindBodyPrefab("DrifterBody");
            if (drifterBody)
            {
                if (!drifterBody.GetComponent<BottomlessBagNetworkController>())
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info("[BagStateSync] Adding BottomlessBagNetworkController to DrifterBody prefab");
                    drifterBody.AddComponent<BottomlessBagNetworkController>();
                    Log.Info("[BagStateSync] Successfully added BottomlessBagNetworkController to DrifterBody prefab!");
                }
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning("[BagStateSync] Could not find DrifterBody prefab to add BottomlessBagNetworkController!");
            }
        }

        private static void CreateSeatPrefab()
        {
            if (AdditionalSeatPrefab != null) return;

            AdditionalSeatPrefab = new GameObject("DrifterBossGrabAdditionalSeat");
            var ni = AdditionalSeatPrefab.AddComponent<NetworkIdentity>();
            ni.localPlayerAuthority = false;
            ni.serverOnly = false;

            var seat = AdditionalSeatPrefab.AddComponent<VehicleSeat>();

            var seatPosObj = new GameObject("SeatPosition");
            seatPosObj.transform.SetParent(AdditionalSeatPrefab.transform);
            seatPosObj.transform.localPosition = Vector3.zero;
            seat.seatPosition = seatPosObj.transform;

            var exitPosObj = new GameObject("ExitPosition");
            exitPosObj.transform.SetParent(AdditionalSeatPrefab.transform);
            exitPosObj.transform.localPosition = Vector3.zero;
            seat.exitPosition = exitPosObj.transform;

            seat.passengerState = new EntityStates.SerializableEntityStateType(typeof(EntityStates.Idle));

            seat.hidePassenger = true;
            seat.disablePassengerMotor = true;
            seat.disableAllCollidersAndHurtboxes = true;
            seat.isEquipmentActivationAllowed = true;

            seat.shouldSetIdle = true;

            var assetId = new Guid("d62f2e5a-7b3c-4e8a-9d1f-8c5e2a3b4d5e");
            ReflectionCache.NetworkIdentity.AssetId?.SetValue(ni, NetworkHash128.Parse(assetId.ToString()));
            GameObject.DontDestroyOnLoad(AdditionalSeatPrefab);
            AdditionalSeatPrefab.SetActive(false);

            ClientScene.RegisterPrefab(AdditionalSeatPrefab);
        }

        private static void OnClientConnect(NetworkConnection conn)
        {
            Log.Info("[BagStateSync] OnClientConnect firing");
            if (NetworkManager.singleton?.client != null)
            {
                // might use this later
            }
        }

        private static void OnServerStart()
        {
            Log.Info("[BagStateSync] OnServerStart firing");
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(DelayedServerHooksInit());
            }
        }

        private static System.Collections.IEnumerator DelayedServerHooksInit()
        {
            float timeout = 5f;
            float elapsed = 0f;
            while (!NetworkServer.active && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (NetworkServer.active)
            {
                Log.Info($"[BagStateSync] NetworkServer.active became true after {elapsed:F1}s, initializing server hooks");
                PersistenceNetworkHandler.RegisterServerHooks();
            }
            else
            {
                Log.Warning($"[BagStateSync] Timed out waiting for NetworkServer.active after {timeout}s");
            }
        }

        private static void OnRunStart(Run run)
        {
            if (NetworkServer.active)
            {
                Log.Info("[BagStateSync] OnRunStart - re-initializing server hooks");
                PersistenceNetworkHandler.RegisterServerHooks();
            }
        }



        public static void Cleanup()
        {
            RoR2.Networking.NetworkManagerSystem.onClientConnectGlobal -= OnClientConnect;
            RoR2.Networking.NetworkManagerSystem.onStartServerGlobal -= OnServerStart;
            Run.onRunStartGlobal -= OnRunStart;
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
