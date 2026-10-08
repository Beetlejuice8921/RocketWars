using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Net
{
    /// <summary>
    /// Поиск комнат в локальной сети: хост раз в секунду шлёт UDP-broadcast «RWAR1|порт|имя|игроков»,
    /// клиенты в меню слушают и собирают список.
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public const int DiscoveryPort = 47777;
        const string Magic = "RWAR1";
        const float BeaconInterval = 1f;
        const float HostTimeout = 4f;

        public struct LanHost
        {
            public string Address;
            public ushort Port;
            public string Name;
            public int Players;
            public float LastSeen;
        }

        readonly Dictionary<string, LanHost> _hosts = new Dictionary<string, LanHost>();
        readonly List<LanHost> _hostList = new List<LanHost>();
        UdpClient _sender, _listener;
        ushort _gamePort;
        float _nextBeacon;

        public IReadOnlyList<LanHost> Hosts => _hostList;
        public bool IsListening => _listener != null;
        public string Error { get; private set; }

        public void StartBroadcast(ushort gamePort)
        {
            StopListening();
            _gamePort = gamePort;
            try
            {
                _sender = new UdpClient { EnableBroadcast = true };
            }
            catch (Exception e)
            {
                Error = e.Message;
                Debug.LogWarning($"[RocketWars] LAN beacon failed: {e.Message}");
            }
        }

        public void StartListening()
        {
            if (_listener != null)
                return;
            try
            {
                _listener = new UdpClient();
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.Message;
                _listener?.Close();
                _listener = null;
            }
        }

        public void StopListening()
        {
            _listener?.Close();
            _listener = null;
            _hosts.Clear();
            _hostList.Clear();
        }

        void Update()
        {
            if (_sender != null && Time.unscaledTime >= _nextBeacon)
            {
                _nextBeacon = Time.unscaledTime + BeaconInterval;
                SendBeacon();
            }

            if (_listener != null)
                Receive();
        }

        void SendBeacon()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
                return;
            string name = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.PlayerName : "Host";
            var bytes = Encoding.UTF8.GetBytes($"{Magic}|{_gamePort}|{name.Replace('|', ' ')}|{nm.ConnectedClientsIds.Count}");
            try
            {
                _sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (SocketException e)
            {
                Error = e.Message;
            }
        }

        void Receive()
        {
            try
            {
                while (_listener.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var data = _listener.Receive(ref from);
                    var parts = Encoding.UTF8.GetString(data).Split('|');
                    if (parts.Length < 4 || parts[0] != Magic || !ushort.TryParse(parts[1], out var hostPort))
                        continue;
                    int.TryParse(parts[3], out int players);
                    string key = $"{from.Address}:{hostPort}";
                    _hosts[key] = new LanHost
                    {
                        Address = from.Address.ToString(),
                        Port = hostPort,
                        Name = parts[2],
                        Players = players,
                        LastSeen = Time.unscaledTime
                    };
                }
            }
            catch (SocketException e)
            {
                Error = e.Message;
            }

            _hostList.Clear();
            foreach (var h in _hosts.Values)
                if (Time.unscaledTime - h.LastSeen < HostTimeout)
                    _hostList.Add(h);
        }

        void OnDestroy()
        {
            _sender?.Close();
            _listener?.Close();
        }
    }
}
