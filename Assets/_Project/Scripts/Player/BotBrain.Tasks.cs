using RocketWars.Combat;
using RocketWars.Flags;
using RocketWars.Launcher;
using RocketWars.Loot;
using RocketWars.Net;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>Дела бота «ногами»: союзники, ящики, флаг, охота, базука по шахтам, сопровождение, бинокль.</summary>
    public partial class BotBrain
    {
        float _lookoutLeft;

        // ---------- подъём союзников ----------

        float ScoreRevive(out PlayerHealth ally)
        {
            ally = null;
            float best = float.MaxValue;
            var bounds = _here.WorldBounds;
            bounds.Expand(new Vector3(0f, 6f, 0f));
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.IsSpawned || !p.IsDowned || p.Team != _health.Team || IsBlacklisted(p))
                    continue;
                if (!bounds.Contains(p.transform.position))
                    continue; // на другом острове или в воздухе — не дойти
                float d = Flat(p.transform.position - transform.position).magnitude;
                if (d < best)
                {
                    ally = p;
                    best = d;
                }
            }
            return ally != null ? 4f * _traits.Revive / (1f + best / 40f) : 0f;
        }

        bool RunRevive()
        {
            var target = _taskPlayer;
            if (target == null || !target.IsSpawned || !target.IsDowned)
                return false;

            float d = Flat(target.transform.position - transform.position).magnitude;
            if (d > ReviveReach || Mathf.Abs(target.transform.position.y - transform.position.y) > 2f)
            {
                if (!MoveTo(target.transform.position, true, ReviveReach))
                {
                    Blacklist(target);
                    return false;
                }
                ResetHold(target);
                return true;
            }

            Stop();
            _fps.BotCrouch = true; // поднимает, присев над союзником
            Face(target.transform.position - Eye);
            if (Hold(target, ReviveInteraction.ReviveDuration))
                _net.RequestRevive(target);
            return true;
        }

        // ---------- ящики ----------

        bool WantsCrate(LootCrate crate)
        {
            if (crate.Island != _here || crate.State == CrateState.Destroyed || crate.IsFalling || IsBlacklisted(crate))
                return false;
            if (crate.State == CrateState.Closed)
                return true;
            if (crate.Medkits > 0)
                return _health.Hp < WoundedHp;
            if (!crate.HasItem)
                return false;
            switch (crate.Kind)
            {
                case LootKind.MachineGun: return !_net.HasMachineGun;
                case LootKind.Sniper: return _traits.TakesSniper && !_net.HasSniper;
                case LootKind.Bazooka: return _traits.TakesBazooka && !_net.HasBazooka;
                default: return false; // броню и маяк бот оставляет игрокам
            }
        }

        float ScoreLoot(out LootCrate target)
        {
            target = null;
            float bestScore = 0f;
            bool wounded = _health.Hp < WoundedHp;
            foreach (var crate in LootCrate.All)
            {
                if (!WantsCrate(crate))
                    continue;
                float d = Flat(crate.Center - transform.position).magnitude;
                float value = crate.Medkits > 0 && wounded ? 3.5f : crate.State == CrateState.Closed ? 1.2f : 1.6f;
                float score = value * (crate.Medkits > 0 ? 1f : _traits.Loot) / (1f + d / 40f);
                if (score > bestScore)
                {
                    bestScore = score;
                    target = crate;
                }
            }
            return bestScore;
        }

        bool RunLoot()
        {
            var crate = _taskCrate;
            if (crate == null || !WantsCrate(crate))
                return false;

            float d = Flat(crate.Center - transform.position).magnitude;
            if (d > CrateReach)
            {
                if (!MoveTo(crate.Center, false, CrateReach))
                {
                    Blacklist(crate);
                    return false;
                }
                ResetHold(crate);
                return true;
            }

            Stop();
            Face(crate.Center - Eye);
            if (crate.State == CrateState.Closed)
            {
                // раненому сервер сразу применит аптечку из только что открытого ящика
                if (Hold(crate, LootCrate.OpenTime))
                    _net.OpenCrate(crate);
            }
            else if (crate.HasItem)
            {
                _net.TakeLoot(crate);
            }
            else if (crate.Medkits > 0 && _health.Hp < PlayerHealth.MaxHp)
            {
                _net.UseMedkit(crate);
            }
            return true;
        }

        // ---------- флаг ----------

        /// <summary>Вражеский флаг на том же острове, что и бот (обычно — после полёта на ракете), и его никто не несёт.</summary>
        float ScoreTakeFlag()
        {
            var flag = MatchNet.Instance != null ? MatchNet.Instance.GetFlag(1 - _health.Team) : null;
            if (flag == null || flag.State == FlagState.Carried || IsBlacklisted(flag) || IslandUnder(flag.Position) != _here)
                return 0f;
            float d = Flat(flag.Position - transform.position).magnitude;
            return 3f * _traits.Flag / (1f + d / 60f);
        }

        bool RunTakeFlag()
        {
            var flag = MatchNet.Instance != null ? MatchNet.Instance.GetFlag(1 - _health.Team) : null;
            if (flag == null || flag.State == FlagState.Carried || IslandUnder(flag.Position) != _here)
                return false;
            if (!MoveTo(flag.Position, true, 1.4f))
            {
                Blacklist(flag);
                return false;
            }
            if (Flat(flag.Position - transform.position).magnitude <= 1.6f)
                _net.TakeFlag(flag);
            return true;
        }

        // ---------- охота ----------

        /// <summary>Невидимый пока враг на этом острове: несущий наш флаг — в первую очередь, затем любой ближний.</summary>
        float ScoreHunt(out PlayerHealth prey)
        {
            prey = null;
            var ourFlag = MatchNet.Instance != null ? MatchNet.Instance.GetFlag(_health.Team) : null;
            ulong carrier = ourFlag != null && ourFlag.State == FlagState.Carried ? ourFlag.CarrierId : ulong.MaxValue;
            float best = 0f;
            foreach (var p in PlayerHealth.All)
            {
                if (!p.IsSpawned || p.IsDowned || p.Team == _health.Team || IslandUnder(p.transform.position) != _here)
                    continue;
                float d = Flat(p.transform.position - transform.position).magnitude;
                float score;
                if (p.NetworkObjectId == carrier)
                    score = 3f * _traits.Flag;
                else if (d < 70f)
                    score = (OnEnemyIsland ? 1f : 1.6f) * _traits.Hunt / (1f + d / 50f); // чужой на нашем острове — выгнать
                else
                    continue;
                if (score > best)
                {
                    best = score;
                    prey = p;
                }
            }
            return best;
        }

        bool RunHunt()
        {
            var prey = _taskPlayer;
            if (prey == null || !prey.IsSpawned || prey.IsDowned || IslandUnder(prey.transform.position) != _here || _taskTime > 15f)
                return false;
            float d = Flat(prey.transform.position - transform.position).magnitude;
            if (!MoveTo(prey.transform.position, d > 15f, 6f))
                return false;
            return d > 6f; // дошли, а врага так и не увидели — бросаем
        }

        // ---------- базука по шахтам ----------

        float ScoreBombard(out RocketSilo target)
        {
            target = null;
            if (!HasBazookaInHands || _bombardCooldown > 0f)
                return 0f;
            float best = 150f;
            foreach (var s in RocketSilo.All)
            {
                if (s.Team == _health.Team || s.State == SiloState.Destroyed)
                    continue;
                float d = Vector3.Distance(Eye, s.transform.position);
                if (d < best && d > BazookaMinRange && CanSeeSilo(s))
                {
                    best = d;
                    target = s;
                }
            }
            return target != null ? (_traits.TakesBazooka ? 1.5f : 0.8f) : 0f;
        }

        bool CanSeeSilo(RocketSilo silo)
        {
            Vector3 to = silo.transform.position + Vector3.up * 1.2f - Eye;
            var hits = Physics.RaycastAll(Eye, to.normalized, to.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<PlayerHealth>() == _health)
                    continue;
                return hit.collider.GetComponentInParent<RocketSilo>() == silo;
            }
            return false;
        }

        /// <summary>Стоим и кладём снаряды в шахту (с разбросом ±1,5 м и поправкой на проседание).</summary>
        bool RunBombard()
        {
            var silo = _taskSilo;
            if (silo == null || silo.State == SiloState.Destroyed || !HasBazookaInHands || _taskTime > 20f)
            {
                _bombardCooldown = 20f;
                return false;
            }
            Stop();
            Vector3 target = silo.transform.position + Vector3.up * 1f + _taskPoint;
            float flight = Vector3.Distance(Eye, target) / BazookaSpec.Speed;
            Vector3 aim = target + Vector3.up * (0.5f * BazookaSpec.Gravity * flight * flight);
            float angle = Face(aim - Eye);
            _shotTimer -= Time.deltaTime;
            if (angle < 3f && _shotTimer <= 0f && _taskTime > 0.8f)
            {
                _net.Shoot(Eye, (aim - Eye).normalized);
                _shotTimer = BazookaSpec.Cooldown + Random.Range(0.8f, 1.6f);
                _taskPoint = Random.insideUnitSphere * 1.5f; // следующий снаряд — с новым разбросом
            }
            return true;
        }

        // ---------- держаться рядом ----------

        float ScoreFollow(out PlayerHealth friend)
        {
            friend = null;
            float best = float.MaxValue;
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.IsSpawned || p.IsDowned || p.Team != _health.Team || IslandUnder(p.transform.position) != _here)
                    continue;
                // за живым игроком — охотнее, чем за ботом
                float d = Flat(p.transform.position - transform.position).magnitude * (p.GetComponent<NetworkPlayer>().IsBot ? 1.5f : 1f);
                if (d < best)
                {
                    best = d;
                    friend = p;
                }
            }
            return friend != null ? 0.5f * _traits.Follow : 0f;
        }

        bool RunFollow()
        {
            var friend = _taskPlayer;
            if (friend == null || !friend.IsSpawned || friend.IsDowned || IslandUnder(friend.transform.position) != _here || _taskTime > 20f)
                return false;
            float d = Flat(friend.transform.position - transform.position).magnitude;
            if (d > 5f)
                return MoveTo(friend.transform.position, d > 14f, 4.5f);
            // рядом — смотрим туда же, куда союзник
            Stop();
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.Euler(0f, friend.transform.eulerAngles.y, 0f), 90f * Time.deltaTime);
            return true;
        }

        // ---------- осмотреться в бинокль ----------

        float ScoreLookout(out Vector3 spot)
        {
            spot = default;
            if (_lookoutCooldown > 0f || OnEnemyIsland || EnemyIsland == null)
                return 0f;
            // точка у края, обращённого к врагу
            float best = float.MaxValue;
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = _here.GetRandomSpawnPoint(0f);
                float d = Flat(p - EnemyIsland.Center).magnitude;
                if (d < best)
                {
                    best = d;
                    spot = p;
                }
            }
            return 0.45f * _traits.Lively;
        }

        bool RunLookout()
        {
            if (_taskTime > 25f)
                return EndLookout();
            float d = Flat(_taskPoint - transform.position).magnitude;
            if (d > 1.5f)
            {
                _lookoutLeft = Random.Range(3f, 5f);
                return MoveTo(_taskPoint, false, 1.5f) || EndLookout();
            }
            Stop();
            Face(EnemyIsland.Center + Vector3.up * 2f - Eye);
            _net.SetBotOptics(true, false);
            _lookoutLeft -= Time.deltaTime;
            return _lookoutLeft > 0f || EndLookout();
        }

        bool EndLookout()
        {
            _net.SetBotOptics(false, false);
            _lookoutCooldown = Random.Range(30f, 50f);
            return false;
        }
    }
}
