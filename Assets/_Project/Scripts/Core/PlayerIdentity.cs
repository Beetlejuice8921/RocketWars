using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Постоянный ключ игрока для бана в стрим-комнате: в Telegram — id аккаунта (из initData),
    /// иначе — случайный ключ устройства в PlayerPrefs. Не секрет и не защита от подделки: чтобы
    /// исключённый зритель не вернулся в комнату простым перезаходом.
    /// </summary>
    public static class PlayerIdentity
    {
        const string DeviceKeyPref = "deviceKey";
        static readonly Regex TelegramUserId = new Regex("\"id\"\\s*:\\s*(\\d+)");

        public static string Key
        {
            get
            {
                // dev-сборки: «-deviceKey X» — разные «устройства» на одном ПК (проверка бана)
                if (Debug.isDebugBuild)
                {
                    var args = Environment.GetCommandLineArgs();
                    int i = Array.IndexOf(args, "-deviceKey");
                    if (i >= 0 && i + 1 < args.Length)
                        return "d:" + args[i + 1];
                }
                if (GamePlatform.Current.TryGetAccountProof(out string provider, out string data) && provider == "telegram")
                {
                    // initData — строка запроса, user=%7B%22id%22%3A123…
                    var match = TelegramUserId.Match(Uri.UnescapeDataString(data));
                    if (match.Success)
                        return "tg:" + match.Groups[1].Value;
                }
                string key = PlayerPrefs.GetString(DeviceKeyPref, "");
                if (key.Length == 0)
                {
                    key = Guid.NewGuid().ToString("N").Substring(0, 16);
                    PlayerPrefs.SetString(DeviceKeyPref, key);
                    PlayerPrefs.Save();
                }
                return "d:" + key;
            }
        }
    }
}
