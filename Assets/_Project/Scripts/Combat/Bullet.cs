using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.Player;
using UnityEngine;

namespace RocketWars.Combat
{
    /// <summary>Параметры пистолета — оружия по умолчанию.</summary>
    public static class PistolSpec
    {
        public const float Damage = 20f;
        /// <summary>Скорость пули: ~113 м между центрами островов за ~0.75 с.</summary>
        public const float Speed = 150f;
        public const float Cooldown = 0.35f;
        public const float MaxRange = 300f;
        /// <summary>Визуальная пуля стартует чуть впереди глаз, чтобы не появляться внутри головы.</summary>
        public const float MuzzleForward = 0.6f;
    }

    /// <summary>Пулемёт из ящика: те же пули, но очередью, 30 патронов, лёгкий разброс.</summary>
    public static class MachineGunSpec
    {
        public const int Ammo = 30;
        public const float Cooldown = 0.1f;
        /// <summary>Разброс очереди, градусы.</summary>
        public const float Spread = 1.2f;
    }

    /// <summary>Снайперская винтовка из ящика: 3 патрона, пуля вдвое быстрее, урон 80, оптика ×4.</summary>
    public static class SniperSpec
    {
        public const int Ammo = 3;
        public const float Damage = 80f;
        public const float Speed = PistolSpec.Speed * 2f;
        public const float Cooldown = 1.2f;
        public const float MaxRange = 600f;
        public const float Zoom = 4f;
        /// <summary>Разброс «от бедра» (без прицела), градусы; в прицел — точно.</summary>
        public const float HipSpread = 3f;
    }

    /// <summary>Чем выпущена пуля: обычная (пистолет, пулемёты) или снайперская (быстрее, больнее, дальше).</summary>
    public enum BulletKind : byte
    {
        Normal,
        Sniper
    }

    /// <summary>Берсерк: по золотому пулемёту в каждой руке — бесконечные патроны, стреляют по очереди и чаще.</summary>
    public static class BerserkSpec
    {
        /// <summary>Анимация превращения (в воздухе, вид от третьего лица), затем сам режим.</summary>
        public const float TransformTime = 2f;
        public const float Duration = 30f;
        /// <summary>Обратное превращение — облачка «хлоп-хлоп».</summary>
        public const float RevertTime = 1f;
        public const float Armor = 200f;
        public const float LevitateHeight = 2f;
        public const float Cooldown = 0.07f;
    }

    /// <summary>
    /// Пуля-снаряд: летит по прямой с конечной скоростью, поэтому по движущейся цели нужно стрелять с упреждением.
    /// Серверная пуля (authoritative) наносит урон; клиентские — только трассер и пыль при попадании.
    /// </summary>
    public class Bullet : MonoBehaviour
    {
        Vector3 _dir;
        float _traveled;
        bool _authoritative;
        GameObject _shooter;
        Collider _shooterCollider;
        int _team;

        /// <summary>Сервер: пуля попала в игрока (стрелок, цель) — для маркера попадания.</summary>
        public static event System.Action<GameObject, PlayerHealth> ServerHit;

        // трассер: яркое бело-жёлтое ядро и оранжевый хвост позади. Рядом с камерой тонкий, вдали толще —
        // чтобы стрелок видел, куда летит пуля, но вспышка у самых глаз не слепила
        const float CoreLength = 1.3f, TailLength = 2.6f;
        const float MinCoreWidth = 0.035f, TailToCore = 0.7f;
        /// <summary>На сколько толщина трассера растёт с расстоянием до камеры (м на м).</summary>
        const float WidthPerMeter = 0.0035f;
        /// <summary>За сколько метров трассер своей пули сходится от дула к линии прицела.</summary>
        const float ConvergeDistance = 12f;
        static readonly Color CoreColor = new Color(1f, 0.95f, 0.6f), TailColor = new Color(1f, 0.5f, 0.12f);

        Transform _visual, _core, _tail;
        Vector3 _visualOffset;
        float _speed = PistolSpec.Speed, _damage = PistolSpec.Damage, _maxRange = PistolSpec.MaxRange;

        // дуло своего оружия для ближайшей пули (на хосте пулю создаёт сервер в том же кадре, что и выстрел)
        static GameObject _muzzleShooter;
        static Vector3 _muzzlePos;
        static int _muzzleFrame = -1;

        /// <summary>Стрелок у себя: трассер следующей пули должен вылететь из дула (а не из центра экрана).</summary>
        public static void SetLocalMuzzle(GameObject shooter, Vector3 muzzle)
        {
            _muzzleShooter = shooter;
            _muzzlePos = muzzle;
            _muzzleFrame = Time.frameCount;
        }

        public static Bullet Spawn(Vector3 origin, Vector3 direction, GameObject shooter, int team, bool authoritative, float catchUpSeconds = 0f,
            BulletKind kind = BulletKind.Normal)
        {
            var go = new GameObject(authoritative ? "Bullet (server)" : "Bullet");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(direction);

            var b = go.AddComponent<Bullet>();
            b._visual = new GameObject("Tracer").transform;
            b._visual.SetParent(go.transform, false);
            b._core = TracerPart(b._visual, CoreColor, 4f);
            b._tail = TracerPart(b._visual, TailColor, 1.3f); // ярче — «перегорает» в жёлтый, а хвост должен быть оранжевым
            if (shooter != null && shooter == _muzzleShooter && _muzzleFrame == Time.frameCount)
            {
                // своя пуля: путь (и попадание) — из глаз по прицелу, а трассер — из дула, сходится к прицелу
                b._visualOffset = _muzzlePos - origin;
                _muzzleShooter = null;
            }
            if (kind == BulletKind.Sniper)
            {
                b._speed = SniperSpec.Speed;
                b._damage = SniperSpec.Damage;
                b._maxRange = SniperSpec.MaxRange;
            }
            b.ScaleTracer();
            b._dir = direction.normalized;
            b._authoritative = authoritative;
            b._shooter = shooter;
            b._shooterCollider = shooter != null ? shooter.GetComponent<Collider>() : null;
            b._team = team;
            if (catchUpSeconds > 0f)
                b.Step(Mathf.Min(catchUpSeconds, 0.5f) * b._speed);
            return b;
        }

        static Transform TracerPart(Transform parent, Color color, float glow)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimeMaterials.Emissive(color, glow);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part.transform;
        }

        /// <summary>
        /// Трассер: у своей пули сдвинут к дулу и сходится к линии прицела; «вырастает» из точки вылета
        /// (не тянется назад в камеру); толщина растёт с расстоянием до камеры — вдали не превращается в точку.
        /// </summary>
        void ScaleTracer()
        {
            float converge = Mathf.Clamp01(1f - _traveled / ConvergeDistance);
            _visual.position = transform.position + _visualOffset * converge;

            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, _visual.position) : 0f;
            float width = Mathf.Max(MinCoreWidth, dist * WidthPerMeter);
            // своя пуля «вырастает» из дула (ровно на пройденный путь), чужая — из точки чуть впереди глаз стрелка
            float flown = _traveled + (_visualOffset == Vector3.zero ? PistolSpec.MuzzleForward : 0f);
            float coreLength = Mathf.Min(CoreLength, flown);
            float tailLength = Mathf.Clamp(flown - CoreLength, 0f, TailLength);
            _core.localScale = new Vector3(width, width, Mathf.Max(0.01f, coreLength));
            _core.localPosition = new Vector3(0f, 0f, -coreLength * 0.5f);
            _tail.gameObject.SetActive(tailLength > 0.05f);
            _tail.localScale = new Vector3(width * TailToCore, width * TailToCore, Mathf.Max(0.01f, tailLength));
            _tail.localPosition = new Vector3(0f, 0f, -coreLength - tailLength * 0.5f + 0.05f);
        }

        void Update()
        {
            Step(_speed * Time.deltaTime);
            if (this != null && _core != null)
                ScaleTracer();
        }

        void Step(float distance)
        {
            if (this == null || !gameObject.activeSelf)
                return;

            Vector3 pos = transform.position;
            // луч на длину шага; свой коллайдер стрелка пропускаем
            var hits = Physics.RaycastAll(pos, _dir, distance, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit? nearest = null;
            foreach (var h in hits)
            {
                if (h.collider == _shooterCollider)
                    continue;
                if (nearest == null || h.distance < nearest.Value.distance)
                    nearest = h;
            }

            if (nearest != null)
            {
                OnHit(nearest.Value);
                return;
            }

            transform.position = pos + _dir * distance;
            _traveled += distance;
            if (_traveled > _maxRange)
                Destroy(gameObject);
        }

        void OnHit(RaycastHit hit)
        {
            var health = hit.collider.GetComponentInParent<PlayerHealth>();
            if (_authoritative && health != null && !health.IsDowned)
            {
                health.TakeDamage(new DamageInfo(_damage, hit.point, _shooter, _team, DamageKind.Bullet));
                ServerHit?.Invoke(_shooter, health);
            }

            // попадание в игрока: красные пиксели брызгают от места удара (звук урона играет сам игрок)
            if (health != null && !health.IsDowned)
            {
                for (int i = 0; i < 8; i++)
                    VoxelParticles.Instance.Emit(hit.point - _dir * 0.1f,
                        (-_dir * 0.6f + Random.insideUnitSphere) * Random.Range(2f, 4f), Random.Range(0.3f, 0.5f),
                        0.1f, 0.06f, i % 2 == 0 ? VoxelParticles.HitRed : VoxelParticles.HitRedDark,
                        gravity: 9f, growEnd: 0.05f, shrinkStart: 0.5f);
            }

            // пыль/искры в месте попадания
            if (health == null)
            {
                for (int i = 0; i < 5; i++)
                    VoxelParticles.Instance.Emit(hit.point + hit.normal * 0.05f,
                        (hit.normal + Random.insideUnitSphere * 0.6f) * Random.Range(1.5f, 3f), Random.Range(0.3f, 0.5f),
                        0.12f, 0.08f, VoxelParticles.RandomSmoke(), gravity: 6f, growEnd: 0.1f, shrinkStart: 0.4f);
                Sfx.PlayAt(Sfx.BulletImpact, hit.point, 0.5f, 30f, Random.Range(0.9f, 1.15f));
            }
            Destroy(gameObject);
        }
    }
}
