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
        public delegate void SubMessageDelegate(NetworkReader reader, NetworkConnection conn);

        private static bool _isNetworkRegistered = false;
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
            Log.Debug("[NetworkMessageRegistry] Initializing event hooks...");
            NetworkManagerSystem.onStartClientGlobal += OnStartClientGlobal;
            NetworkManagerSystem.onStartServerGlobal += OnStartServerGlobal;

            RegisterIfNecessary();
        }

        public static void RegisterIfNecessary()
        {
            if (_isNetworkRegistered) return;

            bool needsMultiplexer = PluginConfig.Instance.BottomlessBagEnabled.Value || PluginConfig.Instance.EnableObjectPersistence.Value;

            if (needsMultiplexer)
            {
                _isNetworkRegistered = true;

                ConfigSyncHandler.RegisterMessages();
                CycleNetworkHandler.RegisterMessages();
                PersistenceNetworkHandler.RegisterMessages();

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

                Log.Debug("[NetworkMessageRegistry] Successfully registered all network message handlers.");
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
            if (_isNetworkRegistered)
            {
                client.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleClientMultiplexedMessage);
                Log.Debug($"[NetworkMessageRegistry] Client Registered Multiplexer MsgId {Constants.Network.MultiplexerMessageType}");
            }
        }

        private static void OnStartServerGlobal()
        {
            if (_isNetworkRegistered)
            {
                NetworkServer.RegisterHandler(Constants.Network.MultiplexerMessageType, HandleServerMultiplexedMessage);
                Log.Debug($"[NetworkMessageRegistry] Server Registered Multiplexer MsgId {Constants.Network.MultiplexerMessageType}");
            }
        }

        private static void HandleClientMultiplexedMessage(NetworkMessage netMsg)
        {
            MultiplexedMessage? multiplexed = null;
            try
            {
                multiplexed = netMsg.ReadMessage<MultiplexedMessage>();
            }
            catch (Exception)
            {
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
                var reader = new NetworkReader(multiplexed.payload);
                handler(reader, netMsg.conn);
            }
        }

        private static void HandleServerMultiplexedMessage(NetworkMessage netMsg)
        {
            MultiplexedMessage? multiplexed = null;
            try
            {
                multiplexed = netMsg.ReadMessage<MultiplexedMessage>();
            }
            catch (Exception)
            {
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
                var reader = new NetworkReader(multiplexed.payload);
                handler(reader, netMsg.conn);
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

            lock (_clientSubHandlers) _clientSubHandlers.Clear();
            lock (_serverSubHandlers) _serverSubHandlers.Clear();
            _isNetworkRegistered = false;

            Log.Debug("[NetworkMessageRegistry] Cleanup called.");
        }
    }
}
