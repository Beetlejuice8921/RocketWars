using System;
using System.Text;

namespace RocketWars.Net
{
    /// <summary>
    /// Совместимость версий по сети. Площадки обновляются не одновременно (Яндекс модерирует несколько дней),
    /// поэтому клиент другой версии не должен попасть в комнату: интернет-комнаты фильтруются по Tag,
    /// а сервер отклоняет подключение с другой Version (прямой вход по коду или IP).
    /// </summary>
    public static class NetProtocol
    {
        /// <summary>
        /// Поднимать при ЛЮБОМ изменении сетевых данных: NetworkVariable/NetworkList, RPC (сигнатуры и порядок),
        /// INetworkSerializable-структуры, список сетевых префабов, порядок генерации мира из сида.
        /// Правки, не трогающие сеть, версию не меняют — такие сборки играют вместе.
        /// </summary>
        public const int Version = 1;

        /// <summary>Сообщение об отказе — русский ключ Loc, клиент переводит его сам.</summary>
        public const string MismatchReason = "Версия игры не совпадает с версией комнаты. Обновите игру";

        // данные подключения: "RW" + версия (2 байта) + имя в UTF-8; старые сборки слали только имя
        static readonly byte[] Magic = { (byte)'R', (byte)'W' };
        const int HeaderSize = 4;

        /// <summary>Метка интернет-комнаты: версия + транспортная группа площадки (pc / web).</summary>
        public static string Tag => $"{Version}-{Core.GamePlatform.Current.NetGroup}";

        public static byte[] BuildPayload(string playerName)
        {
            var name = Encoding.UTF8.GetBytes(playerName ?? "");
            var data = new byte[HeaderSize + name.Length];
            data[0] = Magic[0];
            data[1] = Magic[1];
            data[2] = (byte)(Version & 0xFF);
            data[3] = (byte)(Version >> 8);
            Buffer.BlockCopy(name, 0, data, HeaderSize, name.Length);
            return data;
        }

        /// <summary>Разбор данных подключения; version = -1 — сборка без версии (старее этой схемы).</summary>
        public static void ParsePayload(byte[] data, out int version, out string playerName)
        {
            version = -1;
            playerName = null;
            if (data == null || data.Length < HeaderSize || data[0] != Magic[0] || data[1] != Magic[1])
                return;
            version = data[2] | (data[3] << 8);
            int length = Math.Min(data.Length - HeaderSize, 64);
            if (length > 0)
                playerName = Encoding.UTF8.GetString(data, HeaderSize, length);
        }
    }
}
