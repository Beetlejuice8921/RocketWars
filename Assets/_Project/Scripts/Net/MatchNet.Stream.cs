using System;
using System.Collections.Generic;
using RocketWars.Flags;
using RocketWars.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Net
{
    /// <summary>Настройки стрим-комнаты, которые видят все (StreamHost — у хоста).</summary>
    public struct StreamState : INetworkSerializeByMemcpy, IEquatable<StreamState>
    {
        public bool Enabled;
        public bool Locked;
        public bool Rotate;
        public byte Slots;
        public byte WinsNeeded;

        public bool Equals(StreamState o) =>
            Enabled == o.Enabled && Locked == o.Locked && Rotate == o.Rotate && Slots == o.Slots && WinsNeeded == o.WinsNeeded;

        public override bool Equals(object obj) => obj is StreamState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Enabled, Locked, Rotate, Slots, WinsNeeded);
    }

    /// <summary>Зритель в очереди стрим-комнаты (первый в списке войдёт в игру первым).</summary>
    public struct QueueEntry : INetworkSerializeByMemcpy, IEquatable<QueueEntry>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;

        public bool Equals(QueueEntry o) => ClientId == o.ClientId && Name.Equals(o.Name);
        public override bool Equals(object obj) => obj is QueueEntry o && Equals(o);
        public override int GetHashCode() => ClientId.GetHashCode();
    }

    /// <summary>
    /// Стрим-комната (стример играет с подписчиками). Зрителей больше, чем мест в матче, поэтому каждый
    /// подключившийся сначала встаёт в очередь и смотрит матч с обзорной камеры. Свободное место (кто-то вышел,
    /// хост добавил мест) сразу занимает первый в очереди. Перед новым матчем, если включена смена игроков,
    /// дольше всех игравшие уходят в конец очереди, а их места занимают ждущие. Стример (хост) играет всегда.
    /// </summary>
    public partial class MatchNet
    {
        readonly NetworkVariable<StreamState> _stream = new NetworkVariable<StreamState>();
        NetworkList<QueueEntry> _queue;
        /// <summary>Сервер: зрители в игре (кроме хоста), раньше вошедшие — в начале.</summary>
        readonly List<ulong> _inPlay = new List<ulong>();

        public bool IsStream => _stream.Value.Enabled;
        public StreamState Stream => _stream.Value;
        public NetworkList<QueueEntry> Queue => _queue;

        /// <summary>Место в очереди (1 — следующий), 0 — не в очереди.</summary>
        public int QueuePosition(ulong clientId)
        {
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].ClientId == clientId)
                    return i + 1;
            return 0;
        }

        static bool HasPlayer(NetworkClient client) => client.PlayerObject != null && client.PlayerObject.IsSpawned;

        /// <summary>Сервер, каждый кадр: отразить настройки хоста и пустить ждущих на свободные места.</summary>
        void UpdateStream()
        {
            var state = new StreamState
            {
                Enabled = StreamHost.Enabled,
                Locked = StreamHost.Locked,
                Rotate = StreamHost.Rotate,
                Slots = (byte)StreamHost.Slots,
                WinsNeeded = (byte)StreamHost.WinsNeeded
            };
            if (!_stream.Value.Equals(state))
                _stream.Value = state;
            if (state.Enabled)
                FillFreeSlots();
        }

        /// <summary>Людей в игре вместе с хостом.</summary>
        int PlayersInGame
        {
            get
            {
                _inPlay.RemoveAll(id => !NetworkManager.ConnectedClients.TryGetValue(id, out var c) || !HasPlayer(c));
                return _inPlay.Count + 1;
            }
        }

        void FillFreeSlots()
        {
            while (_queue.Count > 0 && PlayersInGame < StreamHost.Slots)
            {
                ulong id = _queue[0].ClientId;
                _queue.RemoveAt(0);
                if (SpawnPlayerNow(id))
                {
                    _inPlay.Add(id);
                    _botBalanceTimer = 0f; // бот уступает место человеку сразу
                    Debug.Log($"[RocketWars] stream: {NetworkBootstrap.GetPlayerName(id)} enters the game");
                }
            }
        }

        void Enqueue(ulong clientId)
        {
            if (QueuePosition(clientId) > 0)
                return;
            _queue.Add(new QueueEntry { ClientId = clientId, Name = new FixedString32Bytes(NetworkBootstrap.GetPlayerName(clientId)) });
        }

        void OnStreamClientLeft(ulong clientId)
        {
            _inPlay.Remove(clientId);
            int pos = QueuePosition(clientId);
            if (pos > 0)
                _queue.RemoveAt(pos - 1);
        }

        /// <summary>
        /// Сервер, перед новым матчем: после сыгранного матча (afterMatch) дольше всех игравшие уходят в конец
        /// очереди, ждущие занимают их места (не больше, чем ждущих); лишние уходят и в лобби, если хост уменьшил
        /// число мест. true — состав поменялся, матч начнётся на следующем кадре (ушедшие объекты игроков к тому
        /// времени удалятся).
        /// </summary>
        public bool ServerRotateQueue(bool afterMatch)
        {
            if (!IsServer || !StreamHost.Enabled)
                return false;
            int over = Mathf.Max(0, PlayersInGame - StreamHost.Slots);
            int swap = StreamHost.Rotate && afterMatch ? Mathf.Min(_queue.Count, _inPlay.Count) : 0;
            int leave = Mathf.Min(_inPlay.Count, Mathf.Max(over, swap));
            if (leave == 0)
                return false;

            var leaving = _inPlay.GetRange(0, leave);
            foreach (var id in leaving)
                MoveToQueue(id);
            FillFreeSlots();
            Debug.Log($"[RocketWars] stream: rotation, {leave} players back to the queue");
            return true;
        }

        void MoveToQueue(ulong clientId)
        {
            _inPlay.Remove(clientId);
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) && HasPlayer(client))
            {
                var np = client.PlayerObject.GetComponent<NetworkPlayer>();
                // чужой флаг на спине возвращается домой
                foreach (var flag in _flags)
                    if (flag != null && flag.State == FlagState.Carried && flag.CarrierId == client.PlayerObject.NetworkObjectId)
                        flag.ServerReturnHome();
                _switchQueue.Remove(np);
                client.PlayerObject.Despawn(true);
            }
            Enqueue(clientId);
            _botBalanceTimer = 0f;
        }

        // ---------- управление хоста ----------

        /// <summary>Хост исключает зрителя (ban — и не пускает обратно, пока комната открыта).</summary>
        public void ServerKick(ulong clientId, bool ban)
        {
            if (!IsServer || clientId == NetworkManager.ServerClientId)
                return;
            if (ban)
                StreamHost.Ban(clientId);
            OnStreamClientLeft(clientId);
            NetworkManager.DisconnectClient(clientId, ban ? StreamHost.BannedReason : StreamHost.KickedReason);
            Debug.Log($"[RocketWars] stream: {NetworkBootstrap.GetPlayerName(clientId)} {(ban ? "banned" : "kicked")}");
        }

        /// <summary>Хост убирает неподходящий ник: игрок становится «Player N».</summary>
        public void ServerResetName(ulong clientId)
        {
            if (!IsServer || clientId == NetworkManager.ServerClientId)
                return;
            string name = $"Player{clientId}";
            NetworkBootstrap.SetPlayerName(clientId, name);
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) && HasPlayer(client))
                client.PlayerObject.GetComponent<PlayerHealth>().ServerSetName(name);
            int pos = QueuePosition(clientId);
            if (pos > 0)
                _queue[pos - 1] = new QueueEntry { ClientId = clientId, Name = new FixedString32Bytes(name) };
        }

        /// <summary>Хост пропускает зрителя вне очереди: он войдёт следующим.</summary>
        public void ServerQueueToFront(ulong clientId)
        {
            int pos = QueuePosition(clientId);
            if (!IsServer || pos <= 1)
                return;
            var entry = _queue[pos - 1];
            _queue.RemoveAt(pos - 1);
            _queue.Insert(0, entry);
        }

        /// <summary>Хост отправляет игрока в очередь (уступить место): в её конец.</summary>
        public void ServerSendToQueue(ulong clientId)
        {
            if (IsServer && clientId != NetworkManager.ServerClientId && _inPlay.Contains(clientId))
                MoveToQueue(clientId);
        }
    }
}
