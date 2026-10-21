using RocketWars.Net;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Фоновый звук (2D, зациклен): в главном меню — Sounds/AmbientMenu, в игре (лобби и матч) — Sounds/AmbientMatch.
    /// Слои плавно переключаются (~2 с), когда игрок входит в игру или выходит в меню. Без звука в batchmode (тестовые клиенты).
    /// </summary>
    public class Ambience : MonoBehaviour
    {
        const float MenuVolume = 0.45f;
        const float MatchVolume = 0.084f; // ветер нормализован до −1 дБ пика — фоном держим примерно на 13 дБ тише музыки меню
        const float FadeSpeed = 0.5f; // доля громкости в секунду

        AudioSource _menu, _match;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Application.isBatchMode)
                return;
            var go = new GameObject("Ambience");
            DontDestroyOnLoad(go);
            go.AddComponent<Ambience>();
        }

        void Start()
        {
            _menu = CreateSource(Sfx.AmbientMenu);
            _match = CreateSource(Sfx.AmbientMatch);
        }

        AudioSource CreateSource(AudioClip clip)
        {
            if (clip == null)
                return null;
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.volume = 0f;
            return src;
        }

        void Update()
        {
            bool inMatch = NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsRunning;
            Fade(_menu, inMatch ? 0f : MenuVolume);
            Fade(_match, inMatch ? MatchVolume : 0f);
        }

        /// <summary>Источники, поставленные на паузу затуханием (src.time не читаем: в браузере звук грузится асинхронно и Unity ругается).</summary>
        static readonly System.Collections.Generic.HashSet<AudioSource> Paused = new System.Collections.Generic.HashSet<AudioSource>();

        static void Fade(AudioSource src, float target)
        {
            if (src == null)
                return;
            src.volume = Mathf.MoveTowards(src.volume, target, FadeSpeed * Time.unscaledDeltaTime);
            if (src.volume > 0f && !src.isPlaying)
            {
                if (Paused.Remove(src)) src.UnPause();
                else src.Play();
            }
            else if (src.volume <= 0f && src.isPlaying)
            {
                src.Pause(); // продолжит с того же места
                Paused.Add(src);
            }
        }
    }
}
