using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.Launcher;
using RocketWars.Player;
using UnityEngine;

namespace RocketWars.Combat
{
    /// <summary>Базука из ящика: 2 заряда, снаряд летит почти прямо (лёгкое падение), взрыв задевает и своих.</summary>
    public static class BazookaSpec
    {
        public const int Ammo = 2;
        public const float Speed = 55f;
        /// <summary>Небольшое падение снаряда (м/с²): на 100 м уходит вниз примерно на 4 м.</summary>
        public const float Gravity = 2.5f;
        public const float Cooldown = 1.5f;
        public const float Lifetime = 6f;
        public const float InnerRadius = 2f, InnerDamage = 100f;
        public const float OuterRadius = 4f, OuterDamage = 50f;

        /// <summary>Урон каскадом: до 2 м — 100, от 2 до 4 м — 50.</summary>
        public static float DamageAtDistance(float distance) =>
            distance <= InnerRadius ? InnerDamage : distance <= OuterRadius ? OuterDamage : 0f;
    }

    /// <summary>
    /// Снаряд базуки: летит с конечной скоростью и лёгким падением, за ним дымный след.
    /// Серверный (authoritative) при попадании взрывается — урон по игрокам (и своим тоже) и ящикам, шахты и кубики не трогает;
    /// эффект взрыва всем рассылает стрелок (NetworkPlayer). Клиентские копии только летят и исчезают при попадании.
    /// </summary>
    public class BazookaShell : MonoBehaviour
    {
        /// <summary>За сколько метров снаряд своего выстрела сходится от трубы к линии прицела.</summary>
        const float ConvergeDistance = 10f;
        const float SmokeInterval = 0.025f, SmokeStart = 3f;

        Vector3 _velocity;
        float _age, _traveled, _smoke;
        bool _authoritative;
        GameObject _shooter;
        Collider _shooterCollider;
        int _team;
        Transform _visual;
        Vector3 _visualOffset;

        // труба своей базуки для ближайшего снаряда (на хосте снаряд создаёт сервер в том же кадре)
        static GameObject _muzzleShooter;
        static Vector3 _muzzlePos;
        static int _muzzleFrame = -1;

        public static void SetLocalMuzzle(GameObject shooter, Vector3 muzzle)
        {
            _muzzleShooter = shooter;
            _muzzlePos = muzzle;
            _muzzleFrame = Time.frameCount;
        }

        public static BazookaShell Spawn(Vector3 origin, Vector3 direction, GameObject shooter, int team, bool authoritative, float catchUpSeconds = 0f)
        {
            var go = new GameObject(authoritative ? "BazookaShell (server)" : "BazookaShell");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(direction);
            var s = go.AddComponent<BazookaShell>();
            s._velocity = direction.normalized * BazookaSpec.Speed;
            s._authoritative = authoritative;
            s._shooter = shooter;
            s._shooterCollider = shooter != null ? shooter.GetComponent<Collider>() : null;
            s._team = team;

            var (mesh, mats) = Models.BazookaShell();
            s._visual = BoxModel.Spawn(mesh, mats, go.transform, Vector3.zero, "Shell").transform;
            foreach (var r in s._visual.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (shooter != null && shooter == _muzzleShooter && _muzzleFrame == Time.frameCount)
            {
                s._visualOffset = _muzzlePos - origin; // свой снаряд — из трубы, а летит по линии прицела
                _muzzleShooter = null;
            }
            s.UpdateVisual();

            // гул полёта летит вместе со снарядом
            var audio = Sfx.Configure(go.AddComponent<AudioSource>(), 80f);
            audio.clip = Sfx.BazookaFly;
            audio.loop = true;
            audio.volume = 0.8f;
            audio.dopplerLevel = 0.7f;
            audio.Play();
            if (catchUpSeconds > 0f)
                s.Step(Mathf.Min(catchUpSeconds, 0.5f));
            return s;
        }

        void Update()
        {
            Step(Time.deltaTime);
            if (this == null || !gameObject.activeSelf)
                return;
            UpdateVisual();

            // дымный след и огонёк из сопла
            // первые метры без дыма: у стрелка он был бы прямо перед глазами
            _smoke -= Time.deltaTime;
            if (_smoke <= 0f && _traveled > SmokeStart)
            {
                _smoke = SmokeInterval;
                Vector3 tail = _visual.position - transform.forward * 0.25f;
                VoxelParticles.Instance.Emit(tail + Random.insideUnitSphere * 0.08f, Random.insideUnitSphere * 0.4f + Vector3.up * 0.3f,
                    Random.Range(0.6f, 1.0f), 0.12f, Random.Range(0.3f, 0.45f), VoxelParticles.RandomSmoke(), drag: 1.5f,
                    growEnd: 0.15f, shrinkStart: 0.45f);
                VoxelParticles.Instance.Emit(tail, -transform.forward * 3f + Random.insideUnitSphere, Random.Range(0.06f, 0.12f),
                    0.1f, 0.06f, Random.value < 0.5f ? VoxelParticles.Fire0 : VoxelParticles.Fire1, growEnd: 0.1f, shrinkStart: 0.3f);
            }
        }

        void UpdateVisual()
        {
            float converge = Mathf.Clamp01(1f - _traveled / ConvergeDistance);
            _visual.position = transform.position + _visualOffset * converge;
        }

        void Step(float dt)
        {
            if (this == null || !gameObject.activeSelf)
                return;
            _age += dt;
            Vector3 newVelocity = _velocity + Vector3.down * (BazookaSpec.Gravity * dt);
            Vector3 move = (_velocity + newVelocity) * 0.5f * dt;
            _velocity = newVelocity;
            float distance = move.magnitude;
            Vector3 pos = transform.position;

            RaycastHit? nearest = null;
            if (distance > 0f)
                foreach (var h in Physics.RaycastAll(pos, move / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == _shooterCollider)
                        continue;
                    if (nearest == null || h.distance < nearest.Value.distance)
                        nearest = h;
                }

            if (nearest != null)
            {
                Explode(nearest.Value.point + nearest.Value.normal * 0.1f);
                return;
            }

            transform.position = pos + move;
            transform.rotation = Quaternion.LookRotation(_velocity);
            _traveled += distance;
            if (_age > BazookaSpec.Lifetime)
                Destroy(gameObject); // улетел в пустоту
        }

        void Explode(Vector3 point)
        {
            if (_authoritative)
            {
                ServerDamage(point, _shooter, _team);
                var net = _shooter != null ? _shooter.GetComponent<NetworkPlayer>() : null;
                if (net != null && net.IsSpawned)
                    net.ServerShellExploded(point); // эффект — всем
                else
                    ExplosionFx(point);
            }
            Destroy(gameObject);
        }

        /// <summary>Сервер: урон каскадом по игрокам (без учёта команд, и самому стрелку) и ящикам. Шахты базука не берёт.</summary>
        static void ServerDamage(Vector3 position, GameObject source, int sourceTeam)
        {
            var damaged = new HashSet<IDamageable>();
            foreach (var col in Physics.OverlapSphere(position, BazookaSpec.OuterRadius, ~0, QueryTriggerInteraction.Collide))
            {
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || target is RocketSilo || !damaged.Add(target))
                    continue;
                float distance = Vector3.Distance(position, col.bounds.ClosestPoint(position));
                float amount = BazookaSpec.DamageAtDistance(distance);
                if (amount > 0f)
                    target.TakeDamage(new DamageInfo(amount, position, source, sourceTeam, DamageKind.Explosion));
            }
        }

        /// <summary>У всех: небольшой огненный шар, дым, вспышка, тряска и звук (без разрушения кубиков).</summary>
        public static void ExplosionFx(Vector3 pos)
        {
            var p = VoxelParticles.Instance;
            for (int i = 0; i < 28; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.7f + 0.1f;
                p.Emit(pos + dir * 0.2f, dir * Random.Range(4f, 8f), Random.Range(0.6f, 1.0f), 0.5f, Random.Range(0.8f, 1.2f),
                    Random.value < 0.5f ? VoxelParticles.Fire0 : VoxelParticles.Fire1, drag: 3f, growEnd: 0.15f, shrinkStart: 0.55f,
                    colorLate: VoxelParticles.RandomSmoke(), colorSwitch: Random.Range(0.25f, 0.4f));
            }
            for (int i = 0; i < 14; i++)
                p.Emit(pos + Random.insideUnitSphere * 0.8f, Random.insideUnitSphere * 1.5f + Vector3.up * Random.Range(1.5f, 3f),
                    Random.Range(1.4f, 2.2f), 0.6f, Random.Range(1f, 1.5f), VoxelParticles.RandomSmoke(), drag: 1.5f,
                    growEnd: 0.2f, shrinkStart: 0.5f);
            VoxelFx.Flash(pos);
            CameraShake.AddAt(pos, 0.35f);
            Sfx.PlayAt(Sfx.BazookaExplosion, pos, 1f, 150f, Random.Range(0.95f, 1.05f));
        }
    }
}
