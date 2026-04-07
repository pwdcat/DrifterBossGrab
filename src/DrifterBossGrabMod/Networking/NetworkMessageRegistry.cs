#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.Networking;

namespace DrifterBossGrabMod.Networking
{
    // Network message registry for centralized message type constants.
    public static class NetworkMessageRegistry
    {
        private class HandlerInfo
        {
            public short msgType;
            public NetworkMessageDelegate handlerDelegate = null!;
        }

        private static readonly List<HandlerInfo> _clientHandlers = new List<HandlerInfo>();
        private static readonly List<HandlerInfo> _serverHandlers = new List<HandlerInfo>();

        // Initializes the network message system.
        public static void Initialize()
        {
            _clientHandlers.Clear();
            _serverHandlers.Clear();

            try
            {
                var methods = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    .Where(m => m.GetCustomAttribute<NetworkMessageHandlerAttribute>() != null);

                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<NetworkMessageHandlerAttribute>();
                    if (attr == null) continue;

                    var handlerDelegate = (NetworkMessageDelegate)Delegate.CreateDelegate(typeof(NetworkMessageDelegate), method);
                    var handlerInfo = new HandlerInfo { msgType = attr.msgType, handlerDelegate = handlerDelegate };

                    if (attr.client)
                    {
                        _clientHandlers.Add(handlerInfo);
                    }
                    if (attr.server)
                    {
                        _serverHandlers.Add(handlerInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[NetworkMessageRegistry] Failed to scan NetworkMessageHandler attributes: {ex}");
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[NetworkMessageRegistry] Found {_clientHandlers.Count} client handlers and {_serverHandlers.Count} server handlers.");
            }

            NetworkManagerSystem.onStartClientGlobal += OnStartClientGlobal;
            NetworkManagerSystem.onStartServerGlobal += OnStartServerGlobal;
        }

        private static void OnStartClientGlobal(NetworkClient client)
        {
            foreach (var handler in _clientHandlers)
            {
                client.RegisterHandler(handler.msgType, handler.handlerDelegate);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[NetworkMessageRegistry] Client Registered MsgId {handler.msgType} on {client.connection?.connectionId}");
                }
            }
        }

        private static void OnStartServerGlobal()
        {
            foreach (var handler in _serverHandlers)
            {
                NetworkServer.RegisterHandler(handler.msgType, handler.handlerDelegate);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[NetworkMessageRegistry] Server Registered MsgId {handler.msgType}");
                }
            }
        }

        // Cleanup method for logging purposes and preventing memory leaks for server/client handler sets.
        public static void Cleanup()
        {
            NetworkManagerSystem.onStartClientGlobal -= OnStartClientGlobal;
            NetworkManagerSystem.onStartServerGlobal -= OnStartServerGlobal;
            
            _clientHandlers.Clear();
            _serverHandlers.Clear();

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[NetworkMessageRegistry] Cleanup called.");
            }
        }
    }
}
