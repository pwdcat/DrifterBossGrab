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

        public static void Init(Harmony harmony)
        {
            RoR2.Networking.NetworkManagerSystem.onClientConnectGlobal += OnClientConnect;
            RoR2.Networking.NetworkManagerSystem.onStartServerGlobal += OnServerStart;
            Run.onRunStartGlobal += OnRunStart;
            
            // Use CallWhenAvailable to ensure DLC bodies (like Drifter) are loaded
            BodyCatalog.availability.CallWhenAvailable(() => {
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
            // Default settings, will be copied from actual seat during spawn
            seat.passengerState = new EntityStates.SerializableEntityStateType(typeof(EntityStates.Idle));
            
            // Register it so it can be spawned
            // Use a stable hash for the assetId
            var assetId = new Guid("d62f2e5a-7b3c-4e8a-9d1f-8c5e2a3b4d5e");
            typeof(NetworkIdentity).GetField("m_AssetId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(ni, NetworkHash128.Parse(assetId.ToString()));

            // Prevent it from being destroyed
            GameObject.DontDestroyOnLoad(AdditionalSeatPrefab);
            AdditionalSeatPrefab.SetActive(false);

            ClientScene.RegisterPrefab(AdditionalSeatPrefab);
        }

        private static void OnClientConnect(NetworkConnection conn)
        {
            Log.Info("[BagStateSync] OnClientConnect firing");
            PersistenceNetworkHandler.RegisterNetworkHandlers();
            
            // Register additional handlers for bag state
            if (NetworkManager.singleton?.client != null)
            {
                // might use this later
            }
        }

        private static void OnServerStart()
        {
            Log.Info("[BagStateSync] OnServerStart firing");
            // NetworkServer.active may not be true yet, so use a coroutine to wait
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(DelayedCycleHandlerInit());
            }
        }
        
        private static System.Collections.IEnumerator DelayedCycleHandlerInit()
        {
            // Wait until NetworkServer.active is true
            float timeout = 5f;
            float elapsed = 0f;
            while (!NetworkServer.active && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
            
            if (NetworkServer.active)
            {
                Log.Info($"[BagStateSync] NetworkServer.active became true after {elapsed:F1}s, initializing CycleNetworkHandler");
                CycleNetworkHandler.Init();
            }
            else
            {
                Log.Warning($"[BagStateSync] Timed out waiting for NetworkServer.active after {timeout}s");
            }
        }
        
        private static void OnRunStart(Run run)
        {
            // Re-register the cycle handler when a run starts (in case it was lost during scene transition)
            if (NetworkServer.active)
            {
                Log.Info("[BagStateSync] OnRunStart - re-initializing CycleNetworkHandler");
                CycleNetworkHandler.Init();
            }
        }

        public static BottomlessBagNetworkController? GetNetworkController(DrifterBagController controller)
        {
            return controller.GetComponent<BottomlessBagNetworkController>();
        }
    }
}
