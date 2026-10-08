using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Процедурные звуки игры (синтез при первом обращении): сирена, пуск и гул ракеты, взрыв, кнопки пульта,
    /// шаги, урон, «лёг», подъём, свист падения, выстрел пистолета и попадание пули. Ассеты не нужны; при желании любой клип можно заменить файлом.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static AudioClip _siren, _launch, _explosion, _click, _step, _hurt, _downed, _revive, _fallWhistle, _rocketLoop, _gunshot, _bulletImpact, _crateOpen, _heal, _pickup, _hitTick, _hitKill, _powerUp, _pop;

        public static AudioClip Siren => _siren != null ? _siren : _siren = Make("Siren", 2.5f, SirenSample);
        public static AudioClip Launch => _launch != null ? _launch : _launch = MakeNoise("Launch", 2.2f, 0.08f, t => Mathf.Min(1f, t * 8f) * Mathf.Exp(-t * 1.2f));
        public static AudioClip Explosion => _explosion != null ? _explosion : _explosion = MakeExplosion();
        public static AudioClip Click => _click != null ? _click : _click = Make("Click", 0.06f, t => Mathf.Sin(2 * Mathf.PI * 1800f * t) * (1f - t / 0.06f) * 0.4f);

        /// <summary>Шаг: глухой короткий шорох.</summary>
        public static AudioClip Step => _step != null ? _step : _step = MakeNoise("Step", 0.12f, 0.12f, t => Mathf.Exp(-t * 45f) * 0.9f);

        /// <summary>Получил урон: короткий «бум» с понижением тона.</summary>
        public static AudioClip Hurt => _hurt != null ? _hurt : _hurt = Make("Hurt", 0.28f, t =>
            Square(Sweep(t, 320f, 140f, 0.28f)) * 0.35f * Mathf.Exp(-t * 9f));

        /// <summary>Лёг: долгий нисходящий звук.</summary>
        public static AudioClip Downed => _downed != null ? _downed : _downed = Make("Downed", 0.8f, t =>
            (Mathf.Sin(Sweep(t, 420f, 80f, 0.8f)) * 0.5f + Square(Sweep(t, 210f, 40f, 0.8f)) * 0.15f) * Mathf.Min(1f, (0.8f - t) * 6f));

        /// <summary>Поднялся: три восходящие ноты.</summary>
        public static AudioClip Revive => _revive != null ? _revive : _revive = Make("Revive", 0.6f, t =>
        {
            float f = t < 0.2f ? 523.25f : t < 0.4f ? 659.25f : 783.99f;
            float local = t % 0.2f;
            return Mathf.Sin(2 * Mathf.PI * f * t) * 0.35f * Mathf.Exp(-local * 8f);
        });

        /// <summary>Падение с острова: свист вниз.</summary>
        public static AudioClip FallWhistle => _fallWhistle != null ? _fallWhistle : _fallWhistle = Make("FallWhistle", 1.6f, t =>
            Mathf.Sin(Sweep(t, 1400f, 350f, 1.6f)) * 0.25f * Mathf.Min(1f, t * 10f) * Mathf.Min(1f, (1.6f - t) * 4f));

        /// <summary>Гул ракеты в полёте (зацикливается).</summary>
        public static AudioClip RocketLoop => _rocketLoop != null ? _rocketLoop : _rocketLoop = MakeNoise("RocketLoop", 2f, 0.05f, t =>
            (0.7f + 0.3f * Mathf.Sin(2 * Mathf.PI * 3f * t)) * Mathf.Min(1f, Mathf.Min(t, 2f - t) * 20f) * 0.8f);

        /// <summary>Выстрел пистолета: резкий хлопок с коротким «хвостом».</summary>
        public static AudioClip Gunshot => _gunshot != null ? _gunshot : _gunshot = MakeGunshot();

        /// <summary>Пуля попала в землю/камень: короткий сухой щелчок.</summary>
        public static AudioClip BulletImpact => _bulletImpact != null ? _bulletImpact : _bulletImpact = MakeNoise("BulletImpact", 0.1f, 0.35f, t => Mathf.Exp(-t * 60f) * 0.8f);

        /// <summary>Крышка ящика: скрип дерева и глухой стук откинутой крышки.</summary>
        public static AudioClip CrateOpen => _crateOpen != null ? _crateOpen : _crateOpen = MakeCrateOpen();

        /// <summary>Аптечка: мягкий восходящий «вздох».</summary>
        public static AudioClip Heal => _heal != null ? _heal : _heal = Make("Heal", 0.5f, t =>
            Mathf.Sin(Sweep(t, 440f, 880f, 0.5f)) * 0.3f * Mathf.Min(1f, t * 20f) * Mathf.Exp(-t * 4f));

        /// <summary>Взял предмет из ящика: два коротких металлических щелчка.</summary>
        public static AudioClip Pickup => _pickup != null ? _pickup : _pickup = Make("Pickup", 0.3f, t =>
        {
            float local = t < 0.12f ? t : t - 0.12f;
            float f = t < 0.12f ? 1200f : 1600f;
            return Mathf.Sin(2 * Mathf.PI * f * t) * 0.3f * Mathf.Exp(-local * 30f);
        });

        /// <summary>Попал: короткий сухой «тик» (слышит только стрелок).</summary>
        public static AudioClip HitTick => _hitTick != null ? _hitTick : _hitTick = Make("HitTick", 0.07f, t =>
            Square(2 * Mathf.PI * 1900f * t) * 0.22f * Mathf.Exp(-t * 60f));

        /// <summary>Положил цель: два восходящих «динь».</summary>
        public static AudioClip HitKill => _hitKill != null ? _hitKill : _hitKill = Make("HitKill", 0.3f, t =>
        {
            float f = t < 0.1f ? 1320f : 1760f;
            float local = t < 0.1f ? t : t - 0.1f;
            return Square(2 * Mathf.PI * f * t) * 0.18f * Mathf.Exp(-local * 18f);
        });

        static readonly float[] PowerUpNotes = { 261.6f, 329.6f, 392f, 523.3f, 659.3f, 784f, 1046.5f };

        /// <summary>Превращение в берсерка: восходящее арпеджио поверх нарастающего гула (2 с).</summary>
        public static AudioClip PowerUp => _powerUp != null ? _powerUp : _powerUp = Make("PowerUp", 2f, t =>
        {
            var notes = PowerUpNotes;
            int n = Mathf.Min(notes.Length - 1, Mathf.FloorToInt(t / 0.16f));
            float local = t - n * 0.16f;
            float arp = Square(2 * Mathf.PI * notes[n] * t) * 0.12f * Mathf.Exp(-local * 6f);
            float hum = Mathf.Sin(Sweep(t, 80f, 320f, 2f)) * 0.25f * Mathf.Min(1f, t * 2f) * Mathf.Min(1f, (2f - t) * 4f);
            return arp + hum;
        });

        /// <summary>Хлопок облачка (обратное превращение берсерка).</summary>
        public static AudioClip Pop => _pop != null ? _pop : _pop = Make("Pop", 0.15f, t =>
            Mathf.Sin(Sweep(t, 600f, 150f, 0.15f)) * 0.5f * Mathf.Exp(-t * 30f));

        static AudioClip MakeCrateOpen()
        {
            var rng = new System.Random(911);
            float lp = 0f;
            return Make("CrateOpen", 0.7f, t =>
            {
                // скрип: дребезжащий тон с «зубцами», затем удар крышки о ящик
                float creak = t < 0.45f ? Mathf.Sin(Sweep(t, 300f, 520f, 0.45f)) * (Mathf.Repeat(t * 38f, 1f) < 0.5f ? 1f : 0.4f) * 0.18f : 0f;
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.2f;
                float thump = t > 0.48f ? (lp * 2.5f + Mathf.Sin(2 * Mathf.PI * 110f * t) * 0.6f) * Mathf.Exp(-(t - 0.48f) * 25f) : 0f;
                return Mathf.Clamp(creak + thump, -1f, 1f);
            });
        }

        public static AudioClip SniperShot => _sniperShot != null ? _sniperShot : _sniperShot = MakeSniperShot();
        static AudioClip _sniperShot;
        /// <summary>Перезарядка затвора: «чк-чк».</summary>
        public static AudioClip Bolt => _bolt != null ? _bolt : _bolt = MakeBolt();
        static AudioClip _bolt;

        /// <summary>Выстрел базуки: хлопок и шипящий «вжух» уходящего снаряда.</summary>
        public static AudioClip BazookaShot => _bazookaShot != null ? _bazookaShot : _bazookaShot = MakeBazookaShot();
        static AudioClip _bazookaShot;

        static AudioClip MakeBazookaShot()
        {
            var rng = new System.Random(919);
            float lp = 0f;
            return Make("BazookaShot", 0.9f, t =>
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * Mathf.Lerp(0.6f, 0.15f, Mathf.Clamp01(t * 2f));
                float pop = white * Mathf.Exp(-t * 50f) * 0.8f + Mathf.Sin(2 * Mathf.PI * 60f * t) * Mathf.Exp(-t * 18f) * 0.8f;
                float hiss = lp * Mathf.Min(1f, t * 30f) * Mathf.Exp(-t * 3.5f) * 1.4f;
                return Mathf.Clamp(pop + hiss, -1f, 1f);
            });
        }

        static AudioClip MakeSniperShot()
        {
            var rng = new System.Random(777);
            float lp = 0f;
            return Make("SniperShot", 1.1f, t =>
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * Mathf.Lerp(0.95f, 0.05f, Mathf.Clamp01(t * 8f));
                float crack = white * Mathf.Exp(-t * 60f);
                float body = lp * Mathf.Exp(-t * 4.5f) * 2.2f;                       // длинный раскат
                float thump = Mathf.Sin(2 * Mathf.PI * 70f * t) * Mathf.Exp(-t * 14f) * 0.7f;
                return Mathf.Clamp(crack * 0.9f + body + thump, -1f, 1f);
            });
        }

        static AudioClip MakeBolt()
        {
            var rng = new System.Random(31);
            return Make("Bolt", 0.42f, t =>
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                float c1 = t < 0.05f ? (white * 0.6f + Mathf.Sin(2 * Mathf.PI * 2400f * t) * 0.4f) * Mathf.Exp(-t * 90f) : 0f;
                float t2 = t - 0.3f;
                float c2 = t2 > 0f ? (white * 0.6f + Mathf.Sin(2 * Mathf.PI * 1700f * t2) * 0.5f) * Mathf.Exp(-t2 * 70f) : 0f;
                return (c1 + c2) * 0.6f;
            });
        }

        static AudioClip MakeGunshot()
        {
            var rng = new System.Random(4242);
            float lp = 0f;
            return Make("Gunshot", 0.45f, t =>
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * Mathf.Lerp(0.9f, 0.12f, Mathf.Clamp01(t * 12f));
                float crack = white * Mathf.Exp(-t * 90f);
                float body = lp * Mathf.Exp(-t * 11f) * 1.6f;
                float thump = Mathf.Sin(2 * Mathf.PI * 95f * t) * Mathf.Exp(-t * 30f) * 0.5f;
                return Mathf.Clamp(crack * 0.7f + body + thump, -1f, 1f);
            });
        }

        public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float maxDistance = 150f, float pitch = 1f)
        {
            var go = new GameObject($"Sfx_{clip.name}");
            go.transform.position = position;
            var src = Configure(go.AddComponent<AudioSource>(), maxDistance);
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
            return src;
        }

        /// <summary>Звук «в голове» (без 3D), для локального игрока.</summary>
        public static void Play2D(AudioClip clip, float volume = 1f)
        {
            var go = new GameObject($"Sfx2D_{clip.name}");
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.clip = clip;
            src.volume = volume;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        /// <summary>Фаза синуса с линейным изменением частоты f0→f1 за length секунд.</summary>
        static float Sweep(float t, float f0, float f1, float length) =>
            2 * Mathf.PI * (f0 * t + (f1 - f0) * t * t / (2f * length));

        static float Square(float phase) => Mathf.Sign(Mathf.Sin(phase));

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

        static float SirenSample(float t)
        {
            // «вой»: частота качается 600–1100 Гц
            float phase = 2 * Mathf.PI * (850f * t - 250f / (2 * Mathf.PI * 0.9f) * Mathf.Cos(2 * Mathf.PI * 0.9f * t));
            float s = Mathf.Sin(phase);
            return (s * 0.6f + Mathf.Sign(s) * 0.15f) * Mathf.Min(1f, t * 20f) * 0.5f;
        }

        static AudioClip Make(string name, float length, System.Func<float, float> sample)
        {
            int n = Mathf.CeilToInt(length * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = sample(i / (float)Rate);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeNoise(string name, float length, float lowpass, System.Func<float, float> envelope)
        {
            var rng = new System.Random(name.GetHashCode());
            float lp = 0f;
            return Make(name, length, t =>
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * lowpass;
                return lp * envelope(t) * 3f;
            });
        }

        static AudioClip MakeExplosion()
        {
            var rng = new System.Random(77);
            float lp = 0f;
            return Make("Explosion", 2.5f, t =>
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * Mathf.Lerp(0.25f, 0.03f, Mathf.Clamp01(t * 2f));
                float thump = Mathf.Sin(2 * Mathf.PI * 45f * t) * Mathf.Exp(-t * 4f);
                return Mathf.Clamp((lp * 3.5f + thump) * Mathf.Exp(-t * 1.6f), -1f, 1f);
            });
        }
    }
}
