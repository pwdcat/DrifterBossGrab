#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using RoR2;
using RoR2.Networking;

namespace DrifterBossGrabMod.Networking
{

    public static class NetworkMessageRegistry
    {
        public delegate void SubMessageDelegate(NetworkReader reader, NetworkConnection conn);

        private static readonly Dictionary<byte, SubMessageDelegate> _clientSubHandlers = new Dictionary<byte, SubMessageDelegate>();
        private static readonly Dictionary<byte, SubMessageDelegate> _serverSubHandlers = new Dictionary<byte, SubMessageDelegate>();

        public class MultiplexedMessage : MessageBase
        {
            public uint magicSignature;
            public byte subMessageType;
            public byte[] payload = Array.Empty<byte>();

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(magicSignature);
                writer.Write(subMessageType);
                writer.Write(payload.Length);
                writer.Write(payload, payload.Length);
            }

            public override void Deserialize(NetworkReader reader)
            {
                magicSignature = reader.ReadUInt32();
                subMessageType = reader.ReadByte();
                int length = reader.ReadInt32();
                payload = reader.ReadBytes(length);
            }
        }

        public static void Initialize()
        {
            Log.Debug("[NetworkMessageRegistry] Initializing network message hooks...");
            NetworkManagerSystem.onStartClientGlobal -= OnStartClientGlobal;
            NetworkManagerSystem.onStartClientGlobal += OnStartClientGlobal;

            NetworkManagerSystem.onStartServerGlobal -= OnStartServerGlobal;
            NetworkManagerSystem.onStartServerGlobal += OnStartServerGlobal;

            NetworkManagerSystem.onClientConnectGlobal -= OnClientConnectGlobal;
            NetworkManagerSystem.onClientConnectGlobal += OnClientConnectGlobal;

            RegisterSubHandlers();
            RegisterIfNecessary();
        }

        public static void RegisterSubHandlers()
        {
            ConfigSyncHandler.RegisterMessages();
            CycleNetworkHandler.RegisterMessages();
            PersistenceNetworkHandler.RegisterMessages();
        }

        public static void RegisterIfNecessary()
        {
            if (NetworkManager.singleton?.client != null)
            {
                NetworkManager.singleton.client.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleClientMultiplexedMessage);
                Log.Debug("[NetworkMessageRegistry] Client multiplexer handler registered.");
            }
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleServerMultiplexedMessage);
                Log.Debug("[NetworkMessageRegistry] Server multiplexer handler registered.");
            }
        }

        public static void RegisterClientSubHandler(byte subType, SubMessageDelegate handler)
        {
            lock (_clientSubHandlers)
            {
                _clientSubHandlers[subType] = handler;
            }
        }

        public static void UnregisterClientSubHandler(byte subType)
        {
            lock (_clientSubHandlers)
            {
                _clientSubHandlers.Remove(subType);
            }
        }

        public static void RegisterServerSubHandler(byte subType, SubMessageDelegate handler)
        {
            lock (_serverSubHandlers)
            {
                _serverSubHandlers[subType] = handler;
            }
        }

        public static void UnregisterServerSubHandler(byte subType)
        {
            lock (_serverSubHandlers)
            {
                _serverSubHandlers.Remove(subType);
            }
        }

        private static void OnStartClientGlobal(NetworkClient client)
        {
            if (client != null)
            {
                client.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleClientMultiplexedMessage);
                Log.Debug($"[NetworkMessageRegistry] Client Registered Multiplexer MsgId {Constants.Network.MultiplexerMessageType}");
            }
        }

        private static void OnStartServerGlobal()
        {
            NetworkServer.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleServerMultiplexedMessage);
            Log.Debug($"[NetworkMessageRegistry] Server Registered Multiplexer MsgId {Constants.Network.MultiplexerMessageType}");
        }

        private static void OnClientConnectGlobal(NetworkConnection conn)
        {
            if (NetworkManager.singleton?.client != null)
            {
                NetworkManager.singleton.client.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleClientMultiplexedMessage);
                Log.Debug($"[NetworkMessageRegistry] (OnClientConnect) Verified Multiplexer MsgId {Constants.Network.MultiplexerMessageType} registered on client.");
            }

            if (!NetworkServer.active && NetworkClient.active)
            {
                ConfigSyncHandler.RequestConfigFromServer();
            }
        }

        private static void HandleClientMultiplexedMessage(NetworkMessage netMsg)
        {
            MultiplexedMessage? multiplexed = null;
            try
            {
                multiplexed = netMsg.ReadMessage<MultiplexedMessage>();
            }
            catch (Exception ex)
            {
                Log.Debug($"[NetworkMessageRegistry] Failed to deserialize multiplexed message on client: {ex.Message}");
                return;
            }

            if (multiplexed == null || multiplexed.magicSignature != Constants.Network.MSG_SIGNATURE)
            {
                return;
            }

            SubMessageDelegate? handler;
            lock (_clientSubHandlers)
            {
                _clientSubHandlers.TryGetValue(multiplexed.subMessageType, out handler);
            }

            if (handler != null)
            {
                try
                {
                    var reader = new NetworkReader(multiplexed.payload);
                    handler(reader, netMsg.conn);
                }
                catch (Exception ex)
                {
                    Log.Error($"[NetworkMessageRegistry] Error executing client sub-handler {multiplexed.subMessageType}: {ex}");
                }
            }
            else
            {
                Log.Debug($"[NetworkMessageRegistry] No client sub-handler registered for sub-type {multiplexed.subMessageType}. Ignoring.");
            }
        }

        private static void HandleServerMultiplexedMessage(NetworkMessage netMsg)
        {
            MultiplexedMessage? multiplexed = null;
            try
            {
                multiplexed = netMsg.ReadMessage<MultiplexedMessage>();
            }
            catch (Exception ex)
            {
                Log.Debug($"[NetworkMessageRegistry] Failed to deserialize multiplexed message on server: {ex.Message}");
                return;
            }

            if (multiplexed == null || multiplexed.magicSignature != Constants.Network.MSG_SIGNATURE)
            {
                return;
            }

            SubMessageDelegate? handler;
            lock (_serverSubHandlers)
            {
                _serverSubHandlers.TryGetValue(multiplexed.subMessageType, out handler);
            }

            if (handler != null)
            {
                try
                {
                    var reader = new NetworkReader(multiplexed.payload);
                    handler(reader, netMsg.conn);
                }
                catch (Exception ex)
                {
                    Log.Error($"[NetworkMessageRegistry] Error executing server sub-handler {multiplexed.subMessageType}: {ex}");
                }
            }
            else
            {
                Log.Debug($"[NetworkMessageRegistry] No server sub-handler registered for sub-type {multiplexed.subMessageType}. Ignoring.");
            }
        }

        public static void SendToServer(byte subMessageType, MessageBase msg)
        {
            if (NetworkManager.singleton?.client == null || !NetworkManager.singleton.client.isConnected) return;

            var writer = new NetworkWriter();
            msg.Serialize(writer);

            var multiplexed = new MultiplexedMessage
            {
                magicSignature = Constants.Network.MSG_SIGNATURE,
                subMessageType = subMessageType,
                payload = writer.ToArray()
            };

            NetworkManager.singleton.client.Send(Constants.Network.MultiplexerMessageType, multiplexed);
        }

        public static void SendToClient(NetworkConnection conn, byte subMessageType, MessageBase msg)
        {
            if (conn == null || !conn.isReady) return;

            var writer = new NetworkWriter();
            msg.Serialize(writer);

            var multiplexed = new MultiplexedMessage
            {
                magicSignature = Constants.Network.MSG_SIGNATURE,
                subMessageType = subMessageType,
                payload = writer.ToArray()
            };

            conn.Send(Constants.Network.MultiplexerMessageType, multiplexed);
        }

        public static void SendToAll(byte subMessageType, MessageBase msg)
        {
            if (!NetworkServer.active) return;

            var writer = new NetworkWriter();
            msg.Serialize(writer);

            var multiplexed = new MultiplexedMessage
            {
                magicSignature = Constants.Network.MSG_SIGNATURE,
                subMessageType = subMessageType,
                payload = writer.ToArray()
            };

            NetworkServer.SendToAll(Constants.Network.MultiplexerMessageType, multiplexed);
        }

        public static void Cleanup()
        {
            NetworkManagerSystem.onStartClientGlobal -= OnStartClientGlobal;
            NetworkManagerSystem.onStartServerGlobal -= OnStartServerGlobal;
            NetworkManagerSystem.onClientConnectGlobal -= OnClientConnectGlobal;

            lock (_clientSubHandlers) _clientSubHandlers.Clear();
            lock (_serverSubHandlers) _serverSubHandlers.Clear();

            Log.Debug("[NetworkMessageRegistry] Cleanup called.");
        }
    }
}
