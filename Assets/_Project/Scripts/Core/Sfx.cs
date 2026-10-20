using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Звуки игры — файлы в Assets/_Project/Resources/Sounds, имя файла = имя свойства (Explosion.wav, Gunshot.ogg…;
    /// подходят wav/ogg/mp3, расширение не важно). Чтобы заменить звук, положите файл с тем же именем
    /// (список и требования — в Sounds/README.md). Нет файла — звук молча не играет, в консоль одно предупреждение.
    /// </summary>
    public static class Sfx
    {
        const string Folder = "Sounds/";

        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        /// <summary>Клип по имени файла (без расширения) из Resources/Sounds; null — файла нет.</summary>
        public static AudioClip Get(string name)
        {
            if (Cache.TryGetValue(name, out var clip))
                return clip;
            clip = Resources.Load<AudioClip>(Folder + name);
            if (clip == null)
                Debug.LogWarning($"[RocketWars] Нет звука Resources/{Folder}{name} — он не будет играть");
            Cache[name] = clip;
            return clip;
        }

        // ---------- ракеты и шахты ----------

        /// <summary>Сирена шахты перед пуском (зацикливается, 3D).</summary>
        public static AudioClip Siren => Get(nameof(Siren));
        /// <summary>Пуск и полёт ракеты — играет источник на самой ракете (3D).</summary>
        public static AudioClip Launch => Get(nameof(Launch));
        /// <summary>Взрыв ракеты; базука играет его же выше тоном (3D).</summary>
        public static AudioClip Explosion => Get(nameof(Explosion));
        /// <summary>Кнопка пульта (3D, тихо).</summary>
        public static AudioClip Click => Get(nameof(Click));
        /// <summary>Пульт: линия пошла (3D, слышит нажавший).</summary>
        public static AudioClip ConsoleStart => Get(nameof(ConsoleStart));
        /// <summary>Пульт: линия зафиксирована (3D, слышит нажавший).</summary>
        public static AudioClip ConsoleConfirm => Get(nameof(ConsoleConfirm));
        /// <summary>Пульт: кнопка сейчас не нажимается — занят другим, шахта не готова, Launch без цели (3D, слышит нажавший).</summary>
        public static AudioClip ConsoleError => Get(nameof(ConsoleError));
        /// <summary>Схватился за ракету (3D).</summary>
        public static AudioClip RocketGrab => Get(nameof(RocketGrab));

        // ---------- персонаж ----------

        /// <summary>Шаг шагом — случайный из Footsteps/Foot_Step_grass_01..16 (3D, до 15 м).</summary>
        public static AudioClip Step => StepClips.Pick();
        /// <summary>Шаг бегом — случайный из Footsteps/Foot_Run_grass_01..16 (3D, до 20 м).</summary>
        public static AudioClip RunStep => RunClips.Pick();

        static readonly Variants StepClips = new Variants("Footsteps/Foot_Step_grass_", 16);
        static readonly Variants RunClips = new Variants("Footsteps/Foot_Run_grass_", 16);

        /// <summary>Набор вариантов одного звука: файлы prefix01..NN, случайный — не повторяя предыдущий.</summary>
        sealed class Variants
        {
            readonly string _prefix;
            readonly AudioClip[] _clips;
            int _last = -1;

            public Variants(string prefix, int count)
            {
                _prefix = prefix;
                _clips = new AudioClip[count];
            }

            public AudioClip Pick()
            {
                int i = Random.Range(0, _clips.Length - 1);
                if (i >= _last && _last >= 0)
                    i++;
                _last = i;
                if (_clips[i] == null)
                    _clips[i] = Get(_prefix + (i + 1).ToString("00"));
                return _clips[i];
            }
        }

        /// <summary>
        /// Затухание шагов: громко вплотную, дальше ~1/d (как Logarithmic Rolloff в инспекторе), к maxDistance — тишина.
        /// X нормирован на maxDistance.
        /// </summary>
        public static readonly AnimationCurve FootstepRolloff = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.067f, 1f),
            new Keyframe(0.11f, 0.5f),
            new Keyframe(0.18f, 0.25f),
            new Keyframe(0.31f, 0.12f),
            new Keyframe(0.56f, 0.055f),
            new Keyframe(1f, 0f));
        /// <summary>Оттолкнулся в прыжке (3D, до 20 м).</summary>
        public static AudioClip Jump => Get(nameof(Jump));
        /// <summary>Приземлился после прыжка или падения (3D, до 20 м).</summary>
        public static AudioClip Land => Get(nameof(Land));
        /// <summary>Получил урон (3D).</summary>
        public static AudioClip Hurt => Get(nameof(Hurt));
        /// <summary>Лёг (3D).</summary>
        public static AudioClip Downed => Get(nameof(Downed));
        /// <summary>Подняли союзника (3D).</summary>
        public static AudioClip Revive => Get(nameof(Revive));
        /// <summary>Сорвался с острова (2D, слышит только упавший).</summary>
        public static AudioClip FallWhistle => Get(nameof(FallWhistle));

        // ---------- оружие ----------

        /// <summary>Выстрел пистолета и пулемёта (свой — 2D, чужой — 3D).</summary>
        public static AudioClip Gunshot => Get(nameof(Gunshot));
        /// <summary>Выстрел снайперки.</summary>
        public static AudioClip SniperShot => Get(nameof(SniperShot));
        /// <summary>Затвор снайперки после выстрела (2D).</summary>
        public static AudioClip Bolt => Get(nameof(Bolt));
        /// <summary>Выстрел базуки.</summary>
        public static AudioClip BazookaShot => Get(nameof(BazookaShot));
        /// <summary>Пуля попала в землю/камень (3D).</summary>
        public static AudioClip BulletImpact => Get(nameof(BulletImpact));
        /// <summary>Попал по игроку — случайный из BulletHits/BulletHit_third_person_01..04 (2D, слышит стрелок).</summary>
        public static AudioClip HitTick => HitTickClips.Pick();
        /// <summary>В меня попала пуля — случайный из BulletHits/BulletHit_first_person_01..04 (2D, слышит только раненый).</summary>
        public static AudioClip BulletHitMe => HitMeClips.Pick();

        static readonly Variants HitTickClips = new Variants("BulletHits/BulletHit_third_person_", 4);
        static readonly Variants HitMeClips = new Variants("BulletHits/BulletHit_first_person_", 4);
        /// <summary>Положил игрока (2D, слышит стрелок).</summary>
        public static AudioClip HitKill => Get(nameof(HitKill));

        // ---------- ящики ----------

        /// <summary>Открылась крышка ящика (3D).</summary>
        public static AudioClip CrateOpen => Get(nameof(CrateOpen));
        /// <summary>Использована аптечка (3D).</summary>
        public static AudioClip Heal => Get(nameof(Heal));
        /// <summary>Надел бронежилет из ящика (2D, слышит взявший).</summary>
        public static AudioClip ArmorUp => Get(nameof(ArmorUp));
        /// <summary>Взял предмет из ящика (2D).</summary>
        public static AudioClip Pickup => Get(nameof(Pickup));

        // ---------- берсерк ----------

        /// <summary>Превращение в берсерка, 2 с (3D).</summary>
        public static AudioClip PowerUp => Get(nameof(PowerUp));
        /// <summary>Хлопок облачка при обратном превращении (3D).</summary>
        public static AudioClip Pop => Get(nameof(Pop));

        // ---------- конец матча (играет MatchJingle) ----------

        /// <summary>Победа своей команды в матче (2D, стерео).</summary>
        public static AudioClip Win => Get(nameof(Win));
        /// <summary>Поражение своей команды в матче (2D, стерео).</summary>
        public static AudioClip Lose => Get(nameof(Lose));

        // ---------- фон (играет Ambience) ----------

        /// <summary>Фон главного меню (зацикливается, 2D).</summary>
        public static AudioClip AmbientMenu => Get(nameof(AmbientMenu));
        /// <summary>Фон в игре — лобби и матч, ветер (зацикливается без щелчка на стыке, 2D).</summary>
        public static AudioClip AmbientMatch => Get(nameof(AmbientMatch));

        // ---------- проигрывание ----------

        // разовые звуки играют из пула источников: шаги, выстрелы и попадания не плодят GameObject на каждый звук
        const int MaxVoices = 64;
        static readonly List<AudioSource> Voices = new List<AudioSource>();
        static Transform _voiceRoot;
        static int _nextVoice;

        /// <summary>Свободный источник из пула; если все заняты — забираем по кругу (обрывается самый старый).</summary>
        static AudioSource TakeVoice()
        {
            if (_voiceRoot == null)
            {
                Voices.Clear(); // источники прошлой сцены/сессии уничтожены вместе с корнем
                var root = new GameObject("SfxVoices");
                Object.DontDestroyOnLoad(root);
                _voiceRoot = root.transform;
            }

            for (int i = 0; i < Voices.Count; i++)
                if (!Voices[i].isPlaying)
                    return Voices[i];

            if (Voices.Count < MaxVoices)
            {
                var src = new GameObject("Voice").AddComponent<AudioSource>();
                src.transform.SetParent(_voiceRoot, false);
                src.playOnAwake = false;
                Voices.Add(src);
                return src;
            }

            _nextVoice = (_nextVoice + 1) % Voices.Count;
            return Voices[_nextVoice];
        }

        /// <summary>Разовый 3D-звук. Источник принадлежит пулю — не храните ссылку на него.</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float maxDistance = 150f, float pitch = 1f,
            AnimationCurve rolloff = null)
        {
            if (clip == null)
                return null;
            var src = Configure(TakeVoice(), maxDistance);
            if (rolloff != null)
            {
                src.rolloffMode = AudioRolloffMode.Custom;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
            }
            src.transform.position = position;
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.Play();
            return src;
        }

        /// <summary>Звук «в голове» (без 3D), для локального игрока.</summary>
        public static void Play2D(AudioClip clip, float volume = 1f)
        {
            if (clip == null)
                return;
            var src = TakeVoice();
            src.spatialBlend = 0f;
            src.clip = clip;
            src.volume = volume;
            src.pitch = 1f;
            src.Play();
        }

        public static AudioSource Configure(AudioSource src, float maxDistance)
        {
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.playOnAwake = false;
            return src;
        }
    }
}
