using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Net
{
    /// <summary>
    /// Настройки стрим-комнаты на хосте (стример играет с подписчиками): сколько человек в игре одновременно,
    /// до скольких побед матч, закрыт ли вход, сменять ли игроков после матча, баны. Живут, пока хост в комнате;
    /// клиентам их отражает MatchNet (StreamState), очередь — MatchNet.Queue.
    /// </summary>
    public static class StreamHost
    {
        /// <summary>Сколько подключений держит стрим-комната (в игре + в очереди).</summary>
        public const int MaxConnections = 40;

        public const int MinSlots = 2;
        public const int MaxSlots = NetworkBootstrap.MaxPlayers;
        public const int DefaultSlots = 8;
        public const int MinWins = 1;
        public const int MaxWins = 4;
        /// <summary>Короткие матчи (до 2 побед) — очередь движется быстрее.</summary>
        public const int DefaultWins = 2;

        // причины отказа — русские ключи Loc, клиент переводит сам
        public const string LockedReason = "Хост закрыл вход в комнату";
        public const string BannedReason = "Вы заблокированы в этой комнате";
        public const string KickedReason = "Хост исключил вас из комнаты";

        public static bool Enabled { get; private set; }
        public static bool Locked { get; set; }
        public static bool Rotate { get; set; } = true;
        public static int Slots { get; private set; } = DefaultSlots;
        public static int WinsNeeded { get; private set; } = DefaultWins;
        /// <summary>QR-код со ссылкой на комнату на экране хоста (только у него, не по сети).</summary>
        public static bool ShowQr { get; set; } = true;

        static readonly HashSet<string> Banned = new HashSet<string>();
        static readonly Dictionary<ulong, string> Keys = new Dictionary<ulong, string>();
        static readonly Dictionary<ulong, string> PendingRejections = new Dictionary<ulong, string>();

        /// <summary>Хост создаёт комнату: stream — стрим-комната с настройками по умолчанию, иначе обычная.</summary>
        public static void Begin(bool stream)
        {
            Reset();
            Enabled = stream;
        }

        public static void Reset()
        {
            Enabled = Locked = false;
            Rotate = ShowQr = true;
            Slots = DefaultSlots;
            WinsNeeded = DefaultWins;
            Banned.Clear();
            Keys.Clear();
            PendingRejections.Clear();
        }

        public static void SetSlots(int slots) => Slots = Mathf.Clamp(slots, MinSlots, MaxSlots);

        public static void SetWins(int wins) => WinsNeeded = Mathf.Clamp(wins, MinWins, MaxWins);

        /// <summary>Одобрение подключения: почему не пускать (null — пускать).</summary>
        public static string Rejection(string identity)
        {
            if (!Enabled)
                return null;
            if (!string.IsNullOrEmpty(identity) && Banned.Contains(identity))
                return BannedReason;
            return Locked ? LockedReason : null;
        }

        /// <summary>
        /// Отказ при одобрении через Relay до клиента не доходит (соединение рвётся раньше, чем приходит причина),
        /// поэтому забаненного пускаем и отключаем сразу после подключения — как при кике, с причиной.
        /// </summary>
        public static void RejectAfterConnect(ulong clientId, string reason) => PendingRejections[clientId] = reason;

        /// <summary>Причина отключить только что подключившегося (null — пускать).</summary>
        public static string TakeRejection(ulong clientId)
        {
            if (!PendingRejections.TryGetValue(clientId, out var reason))
                return null;
            PendingRejections.Remove(clientId);
            return reason;
        }

        public static void Remember(ulong clientId, string identity)
        {
            if (!string.IsNullOrEmpty(identity))
                Keys[clientId] = identity;
        }

        public static void Forget(ulong clientId)
        {
            Keys.Remove(clientId);
            PendingRejections.Remove(clientId);
        }

        public static void Ban(ulong clientId)
        {
            if (Keys.TryGetValue(clientId, out var key))
                Banned.Add(key);
        }
    }
}
