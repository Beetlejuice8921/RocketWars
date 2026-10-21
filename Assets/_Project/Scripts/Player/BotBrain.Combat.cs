using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Flags;
using RocketWars.Launcher;
using RocketWars.Net;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Бой бота: стрельба «руками» (пистолет, пулемёт, снайперка, базука), движение в перестрелке и уклонение от ракет.
    /// Меткость одна на всех: каждый выстрел попадает с шансом 30%, промах уводит прицел мимо тела.
    /// </summary>
    public partial class BotBrain
    {
        const float ScanInterval = 0.25f;
        const float ReactionTime = 0.45f;
        const float HitChance = 0.3f;
        /// <summary>Дальний выстрел снайперки (враг на другом острове) — чуть реже, у цели есть блик и время укрыться.</summary>
        const float SniperLongHitChance = 0.25f;
        const float SniperRange = 250f;
        /// <summary>Сколько бот держит оптику у глаз перед дальним выстрелом (враг успевает заметить блик).</summary>
        const float ScopeTime = 1.3f;
        /// <summary>Ближе базуку не применяет — заденет себя; отходит.</summary>
        const float BazookaMinRange = 8f;
        const float AimTolerance = 8f;          // градусов: стреляем, когда корпус почти довернулся
        const float TurnSpeed = 540f;           // градусов в секунду
        const float RocketDangerTime = 7f;      // от ракет, которым лететь дольше, пока не бегаем
        const float RocketDangerRadius = Explosion.OuterRadius + 1f;
        /// <summary>Ниже — в перестрелке отходит к своим, а не стоит.</summary>
        const float RetreatHp = 40f;

        PlayerHealth _enemy;
        float _scanTimer, _seenFor, _shotTimer, _scopeTimer;
        Vector3 _enemyLastPos, _enemyVelocity;

        // движение в перестрелке
        float _strafeTimer;
        int _strafe;
        bool _combatCrouch;

        bool HasBazookaInHands => _net.HasBazooka && !_net.IsBerserk;
        bool HasSniperInHands => _net.HasSniper && !_net.HasBazooka && !_net.IsBerserk;

        // ---------- стрельба ----------

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
                    _scopeTimer = 0f;
                    if (found != null)
                    {
                        _enemyLastPos = found.transform.position;
                        _enemyVelocity = Vector3.zero;
                        if (_task == BotTask.Lookout)
                            _net.SetBotOptics(false, false); // с биноклем у глаз не стреляют
                    }
                }
            }

            if (_enemy == null || !_enemy.IsSpawned || _enemy.IsDowned)
            {
                if (_scopeTimer > 0f)
                    _net.SetBotOptics(false, false); // бинокль, если надо, снова поднимет дело «осмотреться»
                _enemy = null;
                _scopeTimer = 0f;
                if (_task != BotTask.Lookout && _task != BotTask.Bombard && _pauseTimer <= 0f)
                    _fps.SetPitch(0f);
                return;
            }

            // скорость цели — для упреждения
            Vector3 pos = _enemy.transform.position;
            if (Time.deltaTime > 0f)
                _enemyVelocity = Vector3.Lerp(_enemyVelocity, (pos - _enemyLastPos) / Time.deltaTime, 0.2f);
            _enemyLastPos = pos;

            bool bazooka = HasBazookaInHands;
            bool sniper = HasSniperInHands;
            bool far = _here == null || !_here.WorldBounds.Contains(new Vector3(pos.x, _here.WorldBounds.center.y, pos.z));
            float distance = Vector3.Distance(Eye, pos);

            Vector3 target = bazooka
                ? pos + Vector3.up * 0.2f // базукой — под ноги: осколки заденут и при небольшом промахе
                : pos + Vector3.up * (_enemy.TryGetComponent(out NetworkPlayer np) && np.IsCrouching ? 0.8f : 1.2f);
            float speed = bazooka ? BazookaSpec.Speed : sniper ? SniperSpec.Speed : PistolSpec.Speed;
            float flight = distance / speed;
            Vector3 aim = target + _enemyVelocity * flight;
            if (bazooka)
                aim += Vector3.up * (0.5f * BazookaSpec.Gravity * flight * flight); // снаряд проседает
            Vector3 dir = aim - Eye;

            float angle = Face(dir);
            _seenFor += Time.deltaTime;
            _shotTimer -= Time.deltaTime;

            // дальний выстрел снайперки — сначала оптика у глаз (у врага блеснёт прицел)
            bool scoped = sniper && far;
            if (scoped)
            {
                _scopeTimer += Time.deltaTime;
                _net.SetBotOptics(false, true);
            }
            else if (_scopeTimer > 0f)
            {
                _scopeTimer = 0f;
                _net.SetBotOptics(false, false);
            }

            if (_seenFor < ReactionTime || angle > AimTolerance || _shotTimer > 0f)
                return;
            if (scoped && _scopeTimer < ScopeTime)
                return;
            if (bazooka && distance < BazookaMinRange)
                return;

            float chance = scoped ? SniperLongHitChance : HitChance;
            if (Random.value >= chance)
            {
                // промах: смещаем точку прицела поперёк линии огня — мимо тела наверняка (базукой — дальше осколков)
                Vector3 side = Vector3.Cross(dir.normalized, Random.onUnitSphere);
                if (side.sqrMagnitude < 1e-4f)
                    side = Vector3.Cross(dir.normalized, Vector3.up);
                aim += side.normalized * (bazooka ? Random.Range(4.5f, 6f) : Random.Range(1.2f, 2f));
            }
            _net.Shoot(Eye, (aim - Eye).normalized);
            _shotTimer = bazooka ? BazookaSpec.Cooldown + Random.Range(0.8f, 1.6f)
                : sniper ? SniperSpec.Cooldown + Random.Range(0.4f, 1f)
                : _net.HasMachineGun || _net.IsBerserk ? MachineGunSpec.Cooldown * 1.5f
                : PistolSpec.Cooldown + Random.Range(0.15f, 0.4f);
            if (scoped)
                _scopeTimer = ScopeTime * 0.5f; // следующий дальний выстрел — после короткого перецеливания
        }

        /// <summary>
        /// Ближайший видимый враг на острове под ногами (или над ним — на парашюте, на ракете);
        /// со снайперкой — и на другом острове. Несущий наш флаг — в приоритете.
        /// </summary>
        PlayerHealth FindEnemy()
        {
            if (_here == null)
                return null;
            var bounds = _here.WorldBounds;
            bounds.Expand(new Vector3(2f, 40f, 2f));
            bool sniper = HasSniperInHands;
            var ourFlag = MatchNet.Instance != null ? MatchNet.Instance.GetFlag(_health.Team) : null;
            ulong carrier = ourFlag != null && ourFlag.State == FlagState.Carried ? ourFlag.CarrierId : ulong.MaxValue;

            PlayerHealth best = null;
            float bestScore = float.MaxValue;
            foreach (var p in PlayerHealth.All)
            {
                if (!p.IsSpawned || p.IsDowned || p.Team == _health.Team)
                    continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (!bounds.Contains(p.transform.position) && !(sniper && d <= SniperRange))
                    continue;
                float score = p.NetworkObjectId == carrier ? d * 0.4f : d;
                if (score < bestScore && CanSee(p))
                {
                    best = p;
                    bestScore = score;
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

        // ---------- движение в перестрелке ----------

        /// <summary>
        /// Ноги в бою: раненый отходит к своим (подальше от врага), с базукой вплотную — отступает,
        /// иначе — стрейфится, иногда приседает или подпрыгивает (насколько «живой» характер).
        /// Снайпер на дальней дистанции стоит на месте.
        /// </summary>
        void CombatMove()
        {
            Vector3 toEnemy = Flat(_enemy.transform.position - transform.position);
            float distance = toEnemy.magnitude;
            Vector3 fwd = distance > 0.01f ? toEnemy / distance : transform.forward;

            _strafeTimer -= Time.deltaTime;
            if (_strafeTimer <= 0f)
            {
                _strafeTimer = Random.Range(0.5f, 1.4f);
                _strafe = Random.value < 0.35f / _traits.Lively ? 0 : Random.value < 0.5f ? -1 : 1;
                _combatCrouch = Random.value < 0.2f * _traits.Lively;
                if (!_combatCrouch && Random.value < 0.12f * _traits.Lively)
                    _fps.BotJump();
            }

            bool retreat = _health.Hp < RetreatHp || (HasBazookaInHands && distance < BazookaMinRange);
            Vector3 move = Vector3.zero;
            if (retreat)
            {
                move = -fwd;
                var ally = NearestAlly(30f, aliveOnly: true);
                if (ally != null)
                    move = (move + Flat(ally.transform.position - transform.position).normalized).normalized;
            }
            else if (_scopeTimer > 0f)
            {
                _combatCrouch = true; // снайпер целится сидя
            }
            else if (_strafe != 0)
            {
                move = Vector3.Cross(Vector3.up, fwd) * _strafe;
            }

            _fps.BotCrouch = _combatCrouch && !retreat;
            if (move == Vector3.zero)
            {
                Stop();
                return;
            }
            if (!GroundAhead(move))
            {
                _strafe = -_strafe; // край или яма — в другую сторону
                Stop();
                return;
            }
            _fps.BotMove = move;
            _fps.BotSprint = retreat;
            CheckStuck();
        }

        PlayerHealth NearestAlly(float maxDistance, bool aliveOnly)
        {
            PlayerHealth best = null;
            float bestDist = maxDistance;
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.IsSpawned || p.Team != _health.Team || (aliveOnly && p.IsDowned))
                    continue;
                if (_here == null || IslandUnder(p.transform.position) != _here)
                    continue;
                float d = Flat(p.transform.position - transform.position).magnitude;
                if (d < bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }
            return best;
        }

        // ---------- уклонение от ракет ----------

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
            _fps.BotCrouch = false;
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
    }
}
