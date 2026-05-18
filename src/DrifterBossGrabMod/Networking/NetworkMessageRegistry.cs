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

    public static class NetworkMessageRegistry
    {
        private class HandlerInfo
        {
            public short msgType;
            public NetworkMessageDelegate handlerDelegate = null!;
        }

        private static readonly List<HandlerInfo> _clientHandlers = new List<HandlerInfo>();
        private static readonly List<HandlerInfo> _serverHandlers = new List<HandlerInfo>();

        public static void Initialize()
        {
            _clientHandlers.Clear();
            _serverHandlers.Clear();

            try
            {
                Type[] types;
                try
                {
                    types = Assembly.GetExecutingAssembly().GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                    Log.Warning($"[NetworkMessageRegistry] Some types failed to load (soft dependency missing?). Loaded {types.Length} types. Loader exceptions: {string.Join(", ", ex.LoaderExceptions.Select(e => e?.Message ?? "null"))}");
                }

                var methods = types
                    .SelectMany(t =>
                    {
                        try
                        {
                            return t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        }
                        catch (Exception)
                        {
                            return Array.Empty<MethodInfo>();
                        }
                    })
                    .Where(m =>
                    {
                        try
                        {
                            return m.GetCustomAttribute<NetworkMessageHandlerAttribute>() != null;
                        }
                        catch (Exception)
                        {
                            return false;
                        }
                    });

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

            Log.Debug($"[NetworkMessageRegistry] Found {_clientHandlers.Count} client handlers and {_serverHandlers.Count} server handlers.");

            NetworkManagerSystem.onStartClientGlobal += OnStartClientGlobal;
            NetworkManagerSystem.onStartServerGlobal += OnStartServerGlobal;
        }

        private static void OnStartClientGlobal(NetworkClient client)
        {
            foreach (var handler in _clientHandlers)
            {
                client.RegisterHandler(handler.msgType, handler.handlerDelegate);
                Log.Debug($"[NetworkMessageRegistry] Client Registered MsgId {handler.msgType} on {client.connection?.connectionId}");
            }
        }

        private static void OnStartServerGlobal()
        {
            foreach (var handler in _serverHandlers)
            {
                NetworkServer.RegisterHandler(handler.msgType, handler.handlerDelegate);
                Log.Debug($"[NetworkMessageRegistry] Server Registered MsgId {handler.msgType}");
            }
        }

        public static void Cleanup()
        {
            NetworkManagerSystem.onStartClientGlobal -= OnStartClientGlobal;
            NetworkManagerSystem.onStartServerGlobal -= OnStartServerGlobal;

            _clientHandlers.Clear();
            _serverHandlers.Clear();

            Log.Debug("[NetworkMessageRegistry] Cleanup called.");
        }
    }
}
