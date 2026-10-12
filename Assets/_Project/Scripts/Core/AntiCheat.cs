using RocketWars.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RocketWars.Core
{
    /// <summary>
    /// Защита от программ-«редакторов памяти» (ArtMoney, Cheat Engine):
    /// важные числа (HP, броня, патроны, счёт, параметры движения) лежат в памяти зашифрованными (ObscuredFloat/Int),
    /// а сервер сам проверяет то, что считает клиент (время удержания E, скорость перемещения — MovementGuard).
    /// Подмена зашифрованного значения однозначна (ложных срабатываний нет) — игра выходит в меню с сообщением.
    /// </summary>
    public static class AntiCheat
    {
        public const string TamperMessage = "Обнаружено вмешательство в память игры";

        static bool _reported;

        /// <summary>Контрольная сумма значения не сошлась: выход из комнаты (в следующем кадре, не посреди сетевого кода).</summary>
        public static void ReportTamper(string what)
        {
            if (_reported || !Application.isPlaying)
                return;
            _reported = true;
            Debug.LogWarning($"[AntiCheat] memory tamper detected ({what})");
            var go = new GameObject("AntiCheatExit");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ExitRunner>();
        }

        class ExitRunner : MonoBehaviour
        {
            bool _requested;

            void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
            void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

            void Update()
            {
                if (_requested)
                    return;
                _requested = true;
                if (NetworkBootstrap.Instance != null)
                    NetworkBootstrap.Instance.ReturnToMenu(Loc.T(TamperMessage));
                else
                    NetworkBootstrap.LastMessage = Loc.T(TamperMessage);
            }

            /// <summary>Меню загружено заново — следим дальше.</summary>
            void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                _reported = false;
                Destroy(gameObject);
            }
        }
    }
}
