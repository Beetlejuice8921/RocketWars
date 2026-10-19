using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Мягкий режим межстраничной рекламы: пока цель — рост аудитории, реклама не должна отпугивать.
    /// Показ — только после итогов матча (не раунда, не при выходе в меню), новичкам первые матчи без рекламы,
    /// и не чаще одного раза за MinInterval (отсчёт и от запуска игры).
    /// </summary>
    public static class AdPolicy
    {
        /// <summary>Сколько первых матчей новичок играет без рекламы.</summary>
        public const int FreeMatches = 2;

        /// <summary>Минимум секунд между показами (и от запуска игры до первого показа).</summary>
        public const float MinInterval = 240f;

        /// <summary>Ключ PlayerPrefs: сыгранные до конца матчи (хранится и в облаке площадки — WebPlatform.CloudPrefs).</summary>
        public const string MatchesKey = "matchesFinished";

        static float _lastShown;

        /// <summary>Матч закончился (экран итогов): засчитать матч и показать рекламу, если правила разрешают.</summary>
        public static void OnMatchFinished()
        {
            int matches = PlayerPrefs.GetInt(MatchesKey, 0) + 1;
            PlayerPrefs.SetInt(MatchesKey, matches);
            PlayerPrefs.Save();

            if (matches <= FreeMatches)
                return;
            if (Time.realtimeSinceStartup - _lastShown < MinInterval)
                return;
            _lastShown = Time.realtimeSinceStartup;
            GamePlatform.Current.ShowInterstitial(null);
        }
    }
}
