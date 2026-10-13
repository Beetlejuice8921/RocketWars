using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Loot;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Поведение бота-балансира (только на сервере, бот принадлежит серверу). Приоритет от высокого к низкому:
    /// 1) стреляет по врагу, которого видит на своём острове (каждый выстрел попадает с шансом 30%);
    /// 2) убегает из зоны падения ракеты;
    /// 3) поднимает лежащих союзников (держит «E» 3 с, урон сбивает);
    /// 4) открывает ящики своего острова, из добычи берёт только пулемёт и лечится аптечками;
    /// 5) патрулирует остров.
    /// Стрельба занимает только «руки»: ноги в бою продолжают уклоняться от ракет, остальные дела ждут.
    /// Действия идут через те же запросы NetworkPlayer, что и у живого игрока, — сервер проверяет их так же.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class BotBrain : MonoBehaviour
    {
        const float ScanInterval = 0.25f;
        const float ReactionTime = 0.45f;
        const float HitChance = 0.3f;
        const float AimTolerance = 8f;          // градусов: стреляем, когда корпус почти довернулся
        const float TurnSpeed = 540f;           // градусов в секунду
        const float RocketDangerTime = 7f;      // от ракет, которым лететь дольше, пока не бегаем
        const float RocketDangerRadius = Explosion.OuterRadius + 1f;
        const float WoundedHp = 70f;            // ниже — идёт лечиться аптечкой
        const float CrateReach = 1.3f;
        const float ReviveReach = 1.6f;
        const float BlacklistTime = 30f;

        NetworkPlayer _net;
        PlayerHealth _health;
        FpsController _fps;

        // бой
        PlayerHealth _enemy;
        float _scanTimer, _seenFor, _shotTimer;
        Vector3 _enemyLastPos, _enemyVelocity;

        // удержание «E» (подъём / открытие ящика)
        Object _holdTarget;
        float _holdProgress;

        // патруль и застревание
        Vector3? _patrolPoint;
        float _patrolTimer;
        Vector3 _stuckCheckPos;
        float _stuckWindow, _stuckTime;
        bool _blocked;
        readonly Dictionary<Object, float> _blacklist = new Dictionary<Object, float>();

        void Awake()
        {
            _net = GetComponent<NetworkPlayer>();
            _health = GetComponent<PlayerHealth>();
            _fps = GetComponent<FpsController>();
        }

        void OnEnable() => _health.Hurt += OnHurt;
        void OnDisable() => _health.Hurt -= OnHurt;

        /// <summary>Урон сбивает подъём и открытие ящика — как у игрока.</summary>
        void OnHurt(float amount) => _holdProgress = 0f;

        VoxelIsland MyIsland => VoxelIsland.ForTeam(_health.Team);
        Vector3 Eye => _fps.CameraRoot.position;

        void Update()
        {
            if (!_net.IsSpawned || !_net.IsServer)
                return;

            _fps.MovementLocked = MatchManager.FreezePlayers;
            bool active = MatchManager.Instance != null && MatchManager.Instance.Phase == MatchPhase.Round;
            if (!active || _health.IsDowned || MyIsland == null || _fps.Attached)
            {
                Stop();
                _enemy = null;
                _holdProgress = 0f;
                _patrolPoint = null;
                return;
            }

            UpdateCombat();

            // ноги: ракета → союзник → ящик → патруль
            if (Dodge()) return;
            if (_enemy != null)
            {
                Stop(); // в бою стоим и стреляем, остальные дела подождут
                return;
            }
            if (Revive()) return;
            if (Crates()) return;
            Patrol();
        }

        // ---------- 1. стрельба ----------

        void UpdateCombat()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                var found = FindEnemy();
                if (found != _enemy)
                {
                    _enemy = found;
                    _seenFor = 0f;
                    if (found != null)
                    {
                        _enemyLastPos = found.transform.position;
                        _enemyVelocity = Vector3.zero;
                    }
                }
            }

            if (_enemy == null || !_enemy.IsSpawned || _enemy.IsDowned)
            {
                _enemy = null;
                _fps.SetPitch(0f);
                return;
            }

            // скорость цели — для упреждения (пуля летит 150 м/с)
            Vector3 pos = _enemy.transform.position;
            if (Time.deltaTime > 0f)
                _enemyVelocity = Vector3.Lerp(_enemyVelocity, (pos - _enemyLastPos) / Time.deltaTime, 0.2f);
            _enemyLastPos = pos;

            Vector3 chest = pos + Vector3.up * (_enemy.TryGetComponent(out NetworkPlayer np) && np.IsCrouching ? 0.8f : 1.2f);
            float flight = Vector3.Distance(Eye, chest) / PistolSpec.Speed;
            Vector3 aim = chest + _enemyVelocity * flight;
            Vector3 dir = aim - Eye;

            float angle = Face(dir);
            _seenFor += Time.deltaTime;
            _shotTimer -= Time.deltaTime;
            if (_seenFor < ReactionTime || angle > AimTolerance || _shotTimer > 0f)
                return;

            if (Random.value >= HitChance)
            {
                // промах: смещаем точку прицела на 1,2–2 м поперёк линии огня — мимо тела наверняка
                Vector3 side = Vector3.Cross(dir.normalized, Random.onUnitSphere);
                if (side.sqrMagnitude < 1e-4f)
                    side = Vector3.Cross(dir.normalized, Vector3.up);
                aim += side.normalized * Random.Range(1.2f, 2f);
            }
            _net.Shoot(Eye, (aim - Eye).normalized);
            _shotTimer = _net.HasMachineGun
                ? MachineGunSpec.Cooldown * 1.5f
                : PistolSpec.Cooldown + Random.Range(0.15f, 0.4f);
        }

        /// <summary>Ближайший видимый враг на своём острове (или над ним — на парашюте, на ракете).</summary>
        PlayerHealth FindEnemy()
        {
            var bounds = MyIsland.WorldBounds;
            bounds.Expand(new Vector3(2f, 40f, 2f));
            PlayerHealth best = null;
            float bestDist = float.MaxValue;
            foreach (var p in PlayerHealth.All)
            {
                if (!p.IsSpawned || p.IsDowned || p.Team == _health.Team || !bounds.Contains(p.transform.position))
                    continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d < bestDist && CanSee(p))
                {
                    best = p;
                    bestDist = d;
                }
            }
            return best;
        }

        bool CanSee(PlayerHealth target)
        {
            Vector3 to = target.transform.position + Vector3.up * 1.2f - Eye;
            var hits = Physics.RaycastAll(Eye, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var owner = hit.collider.GetComponentInParent<PlayerHealth>();
                if (owner == _health)
                    continue;
                return owner == target;
            }
            return true;
        }

        /// <summary>Довернуть корпус (рысканье) и взгляд (тангаж) к направлению; возвращает оставшийся угол.</summary>
        float Face(Vector3 dir)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 1e-4f)
            {
                var want = Quaternion.LookRotation(flat);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * Time.deltaTime);
            }
            float pitch = -Mathf.Atan2(dir.y, Mathf.Max(0.01f, flat.magnitude)) * Mathf.Rad2Deg;
            _fps.SetPitch(pitch);
            return Vector3.Angle(transform.forward, flat);
        }

        // ---------- 2. уклонение от ракет ----------

        static readonly float[] DodgeTurns = { 0f, 40f, -40f, 80f, -80f, 120f, -120f };

        bool Dodge()
        {
            Rocket danger = null;
            float dangerDist = RocketDangerRadius;
            foreach (var r in Rocket.Active)
            {
                if (r == null || !r.HitsGround || r.TimeToImpact > RocketDangerTime || r.TimeToImpact < 0f)
                    continue;
                float d = Flat(transform.position - r.Target).magnitude;
                if (d < dangerDist)
                {
                    danger = r;
                    dangerDist = d;
                }
            }
            if (danger == null)
                return false;

            _holdProgress = 0f;
            Vector3 away = Flat(transform.position - danger.Target);
            if (away.sqrMagnitude < 0.01f)
                away = transform.forward;
            // пробуем бежать прямо от точки падения, потом всё круче в стороны (край острова — не вариант)
            foreach (float turn in DodgeTurns)
            {
                Vector3 dir = Quaternion.Euler(0f, turn, 0f) * away.normalized;
                if (GroundAhead(dir))
                {
                    Drive(dir, true);
                    return true;
                }
            }
            Stop();
            return true;
        }

        // ---------- 3. подъём союзников ----------

        bool Revive()
        {
            PlayerHealth target = null;
            float best = float.MaxValue;
            var bounds = MyIsland.WorldBounds;
            bounds.Expand(new Vector3(0f, 6f, 0f));
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.IsSpawned || !p.IsDowned || p.Team != _health.Team || IsBlacklisted(p))
                    continue;
                if (!bounds.Contains(p.transform.position))
                    continue; // на чужом острове или в воздухе — не дойти
                float d = Flat(p.transform.position - transform.position).magnitude;
                if (d < best)
                {
                    target = p;
                    best = d;
                }
            }
            if (target == null)
                return false;

            if (best > ReviveReach || Mathf.Abs(target.transform.position.y - transform.position.y) > 2f)
            {
                if (!MoveTo(target.transform.position, true, ReviveReach))
                    Blacklist(target);
                ResetHold(target);
                return true;
            }

            Stop();
            Face(target.transform.position - Eye);
            if (Hold(target, ReviveInteraction.ReviveDuration))
                _net.RequestRevive(target);
            return true;
        }

        // ---------- 4. ящики ----------

        bool Crates()
        {
            LootCrate target = null;
            float best = float.MaxValue;
            bool wounded = _health.Hp < WoundedHp;
            foreach (var crate in LootCrate.All)
            {
                if (crate.Island != MyIsland || crate.State == CrateState.Destroyed || crate.IsFalling || IsBlacklisted(crate))
                    continue;
                bool wanted = crate.State == CrateState.Closed
                              || (crate.Kind == LootKind.MachineGun && crate.HasItem && !_net.HasMachineGun)
                              || (crate.Medkits > 0 && wounded);
                if (!wanted)
                    continue;
                float d = Flat(crate.Center - transform.position).magnitude;
                if (d < best)
                {
                    target = crate;
                    best = d;
                }
            }
            if (target == null)
                return false;

            if (best > CrateReach)
            {
                if (!MoveTo(target.Center, false, CrateReach))
                    Blacklist(target);
                ResetHold(target);
                return true;
            }

            Stop();
            Face(target.Center - Eye);
            if (target.State == CrateState.Closed)
            {
                // раненому сервер сразу применит аптечку из только что открытого ящика
                if (Hold(target, LootCrate.OpenTime))
                    _net.OpenCrate(target);
            }
            else if (target.Kind == LootKind.MachineGun && target.HasItem && !_net.HasMachineGun)
            {
                _net.TakeLoot(target);
            }
            else if (target.Medkits > 0 && _health.Hp < PlayerHealth.MaxHp)
            {
                _net.UseMedkit(target);
            }
            // снайперку, базуку, броню и маяк бот не берёт — оставляет игрокам
            return true;
        }

        // ---------- 5. патруль ----------

        void Patrol()
        {
            _patrolTimer -= Time.deltaTime;
            if (_patrolPoint == null || _patrolTimer <= 0f || _blocked)
            {
                _patrolPoint = MyIsland.GetRandomSpawnPoint(0f);
                _patrolTimer = 20f;
                _blocked = false;
            }
            if (MoveTo(_patrolPoint.Value, false, 1.5f))
                _patrolPoint = null; // дошли — следующая точка
        }

        // ---------- движение ----------

        /// <summary>Идти к точке; true — уже рядом. false с _blocked — путь не найден (край, застряли).</summary>
        bool MoveTo(Vector3 point, bool sprint, float stopDistance)
        {
            Vector3 to = Flat(point - transform.position);
            if (to.magnitude <= stopDistance)
            {
                Stop();
                return true;
            }

            Vector3 dir = to.normalized;
            if (!GroundAhead(dir))
            {
                // обход ямы или края: пробуем отклониться
                bool found = false;
                foreach (float turn in new[] { 35f, -35f, 70f, -70f })
                {
                    Vector3 alt = Quaternion.Euler(0f, turn, 0f) * dir;
                    if (GroundAhead(alt))
                    {
                        dir = alt;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Stop();
                    _blocked = true;
                    return false;
                }
            }
            Drive(dir, sprint);
            return !_blocked;
        }

        void Drive(Vector3 dir, bool sprint)
        {
            _fps.BotMove = dir;
            _fps.BotSprint = sprint;
            if (_enemy == null)
            {
                Face(dir);
                _fps.SetPitch(0f);
            }
            CheckStuck();
        }

        void Stop()
        {
            _fps.BotMove = Vector3.zero;
            _fps.BotSprint = false;
            _stuckWindow = 0f;
            _stuckTime = 0f;
            _stuckCheckPos = transform.position;
        }

        /// <summary>Каждые 0,5 с: если почти не сдвинулись — прыгаем; застряли больше 3 с — путь закрыт.</summary>
        void CheckStuck()
        {
            _stuckWindow += Time.deltaTime;
            if (_stuckWindow < 0.5f)
                return;
            float moved = Flat(transform.position - _stuckCheckPos).magnitude;
            _stuckCheckPos = transform.position;
            if (moved < 0.4f && _fps.IsGrounded)
            {
                _stuckTime += _stuckWindow;
                _fps.BotJump();
                if (_stuckTime > 3f)
                {
                    _blocked = true;
                    _stuckTime = 0f;
                }
            }
            else
            {
                _stuckTime = 0f;
                _blocked = false;
            }
            _stuckWindow = 0f;
        }

        /// <summary>Есть ли впереди земля не глубже ~2,5 м (иначе край острова или глубокая воронка).</summary>
        bool GroundAhead(Vector3 dir)
        {
            if (!_fps.IsGrounded)
                return true; // в воздухе не выбираем
            foreach (float dist in new[] { 1f, 2f })
            {
                Vector3 origin = transform.position + dir * dist + Vector3.up * 1.5f;
                if (!Physics.Raycast(origin, Vector3.down, 4.1f, ~0, QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }

        // ---------- удержание «E» ----------

        /// <summary>Копит время удержания на цели; true — время вышло (действие выполнено, счётчик сброшен).</summary>
        bool Hold(Object target, float duration)
        {
            ResetHold(target);
            _holdProgress += Time.deltaTime;
            if (_holdProgress < duration)
                return false;
            _holdProgress = 0f;
            return true;
        }

        void ResetHold(Object target)
        {
            if (_holdTarget == target)
                return;
            _holdTarget = target;
            _holdProgress = 0f;
        }

        // ---------- недостижимые цели ----------

        bool IsBlacklisted(Object target) => _blacklist.TryGetValue(target, out float until) && Time.time < until;

        void Blacklist(Object target)
        {
            if (!_blocked)
                return;
            _blacklist[target] = Time.time + BlacklistTime;
            _blocked = false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
