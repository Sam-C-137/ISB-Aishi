using AishiKeys.Networking;
using BepInEx.Logging;
using Comfort.Common;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AishiKeys.Fika
{
    internal sealed class FikaAishiKeysNetworkBridge : IAishiKeysNetworkBridge
    {
        private const int MaximumPayloadLength = 65535;
        private readonly ManualLogSource _log;
        private readonly object _queueLock = new object ();
        private readonly Queue<string> _receivedPayloads = new Queue<string>();
        private IFikaNetworkManager _networkManager;
        private IFikaNetworkManager _registeredNetworkManager;
        private bool _initialized;
        private bool _isServer;
        internal FikaAishiKeysNetworkBridge(ManualLogSource log)
        {
            _log = log;
        }

        public bool Active { get; private set; }

        public bool Ready
        {
            get
            {
                EnsureNetworkManager();
                return Active && _networkManager != null;
            }
        }

        public bool IsServer
        {
            get
            {
                RefreshRole();
                return _isServer;
            }
        }

        public bool IsHeadless
        {
            get
            {
                try
                {
                    return FikaBackendUtils.IsHeadless;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            Active = true;
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnNetworkManagerDestroyed);
            FikaEventDispatcher.SubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
            EnsureNetworkManager();
            _log.LogInfo("Aishi Keys Fika: direct network bridge initialized without reflection or runtime IL generation.");
        }

        public bool Broadcast(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > MaximumPayloadLength || !Ready || !IsServer)
            {
                return false;
            }

            try
            {
                AishiKeysPacket packet = new AishiKeysPacket
                {
                    Payload = payload
                };
                _networkManager.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Aishi Keys Fika: failed to broadcast packet: " + ex.Message);
                return false;
            }
        }

        public bool SendToServer(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > MaximumPayloadLength || !Ready || IsServer)
            {
                return false;
            }

            try
            {
                AishiKeysPacket packet = new AishiKeysPacket
                {
                    Payload = payload
                };
                _networkManager.SendData(ref packet, DeliveryMethod.ReliableOrdered, false);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Aishi Keys Fika: failed to send packet to authority: " + ex.Message);
                return false;
            }
        }

        public void DrainReceivedPayloads(Action<string> handler)
        {
            if (handler == null)
                return;
            long started = Stopwatch.GetTimestamp();
            for (int processed = 0; processed < 64 && (processed == 0 || Stopwatch.GetTimestamp() - started < Stopwatch.Frequency / 500) && TryDequeuePayload(out string payload); processed++)
            {
                try
                {
                    handler(payload);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Aishi Keys Fika: payload handler failed: " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (_initialized)
            {
                FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
                FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnNetworkManagerDestroyed);
                FikaEventDispatcher.UnsubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
            }

            _initialized = false;
            Active = false;
            _networkManager = null;
            _registeredNetworkManager = null;
            lock (_queueLock)
                _receivedPayloads.Clear();
        }

        private void OnNetworkManagerCreated(FikaNetworkManagerCreatedEvent fikaEvent)
        {
            if (!Active || fikaEvent == null)
                return;
            AttachNetworkManager(fikaEvent.Manager, "FikaNetworkManagerCreatedEvent");
        }

        private void OnNetworkManagerDestroyed(FikaNetworkManagerDestroyedEvent fikaEvent)
        {
            if (!Active || fikaEvent == null)
                return;
            if (_networkManager != null && !ReferenceEquals(_networkManager, fikaEvent.Manager))
                return;
            _networkManager = null;
            _registeredNetworkManager = null;

            lock (_queueLock)
                _receivedPayloads.Clear();
        }

        private void OnRaidStarted(FikaRaidStartedEvent fikaEvent)
        {
            if (!Active || fikaEvent == null)
                return;
            _isServer = fikaEvent.IsServer;
            EnsureNetworkManager();
        }

        private void EnsureNetworkManager()
        {
            if (!Active || _networkManager != null)
                return;
            try
            {
                if (!Singleton<IFikaNetworkManager>.Instantiated)
                    return;
                AttachNetworkManager(Singleton<IFikaNetworkManager>.Instance, "Singleton<IFikaNetworkManager> fallback");
            }
            catch (Exception ex)
            {
                _log.LogDebug("Aishi Keys Fika: network manager is not ready yet: " + ex.Message);
            }
        }

        private void AttachNetworkManager(IFikaNetworkManager networkManager, string source)
        {
            if (!Active || networkManager == null)
                return;
            if (ReferenceEquals(_registeredNetworkManager, networkManager))
            {
                _networkManager = networkManager;
                return;
            }

            try
            {
                networkManager.RegisterPacket<AishiKeysPacket>(OnPacketReceived);
                _networkManager = networkManager;
                _registeredNetworkManager = networkManager;
                RefreshRole();
                _log.LogInfo("Aishi Keys Fika: packet registered using " + source + ". manager=" + networkManager.GetType().FullName + ", server=" + _isServer + ", headless=" + IsHeadless + ".");
            }
            catch (Exception ex)
            {
                _networkManager = null;
                _registeredNetworkManager = null;
                _log.LogError("Aishi Keys Fika: packet registration failed: " + ex);
            }
        }

        private void OnPacketReceived(AishiKeysPacket packet)
        {
            if (!Active || string.IsNullOrEmpty(packet.Payload) || packet.Payload.Length > MaximumPayloadLength)
            {
                return;
            }

            lock (_queueLock)
            {
                _receivedPayloads.Enqueue(packet.Payload);
            }
        }

        private bool TryDequeuePayload(out string payload)
        {
            lock (_queueLock)
            {
                if (_receivedPayloads.Count == 0)
                {
                    payload = null;
                    return false;
                }

                payload = _receivedPayloads.Dequeue();
                return true;
            }
        }

        private void RefreshRole()
        {
            try
            {
                _isServer = FikaBackendUtils.IsServer;
            }
            catch
            {
            }

            if (_networkManager == null)
                return;
            string managerName = _networkManager.GetType().Name;
            if (managerName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
                _isServer = true;
            else if (managerName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0)
                _isServer = false;
        }
    }
}
