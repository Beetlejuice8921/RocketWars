using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Net;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Ракеты у бота: пульт шахты (линии X и Y останавливаются как у игрока — нажатием в момент, когда линия
    /// проходит через выбранную точку), пуск, полёт верхом на ракете и прыжок с неё над островом-целью.
    /// Цель обычного пуска — вражеская шахта, место, где сейчас примерно стоит враг, или случайная точка;
    /// с разбросом в несколько метров. С вражеским флагом бот пускает ВРАЖЕСКУЮ шахту по своему острову и летит домой.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>С какого расстояния до стойки пульта бот жмёт кнопки (сервер принимает до 5 м).</summary>
        const float ConsoleStand = 1.8f;
        /// <summary>Сколько бот готов провозиться у пульта (кто-то мешает, нажатия не проходят) — потом бросает.</summary>
        const float ConsoleGiveUp = 25f;
        /// <summary>Руки бота до поверхности ракеты (у игрока — 3 м).</summary>
        const float GrabReach = 2.6f;
        const float HangBelow = 2.05f;

        enum ConsoleStep { Approach, Wait, Think, AimX, AimY, Confirm }

        /// <summary>Какой бот занял пульт — второй бот к нему не идёт.</summary>
        static readonly Dictionary<RocketSilo, BotBrain> ConsoleClaims = new Dictionary<RocketSilo, BotBrain>();

        ConsoleStep _consoleStep;
        float _stepTimer, _consoleTime, _prevLine = float.NaN;
        Vector2 _aimUv;
        bool _rideAfter, _aimHome;

        // полёт
        int _rideSilo = -1;
        Rocket _rideRocket;
        Vector3 _rideVelocity;
        float _trickTimer;

        // ---------- выбор ----------

        float ScoreArtillery(out RocketSilo silo)
        {
            silo = null;
            if (_artilleryCooldown > 0f || OnEnemyIsland || !MatchManager.ConsolesEnabled)
                return 0f;
            float best = 0f;
            foreach (var s in RocketSilo.All)
            {
                if (!UsableConsole(s, _health.Team, maxReload: 10f))
                    continue;
                float d = Flat(s.Console.transform.position - transform.position).magnitude;
                float score = 1.6f * _traits.Artillery / (1f + d / 60f);
                if (s.State == SiloState.Reloading)
                    score *= 0.6f;
                if (score > best)
                {
                    best = score;
                    silo = s;
                }
            }
            return best;
        }

        /// <summary>Пульт команды team на острове под ногами, к которому можно идти: шахта цела, скоро готова, не занята.</summary>
        bool UsableConsole(RocketSilo s, int team, float maxReload)
        {
            if (s == null || s.Team != team || s.Island != _here || s.State == SiloState.Destroyed || s.State == SiloState.Launching)
                return false;
            if (s.State == SiloState.Reloading && s.ReloadLeft > maxReload)
                return false;
            if (IsBlacklisted(s) || OtherAtConsole(s, 4f))
                return false;
            return !ConsoleClaims.TryGetValue(s, out var other) || other == null || other == this;
        }

        bool OtherAtConsole(RocketSilo s, float radius)
        {
            Vector3 c = s.Console.transform.position;
            foreach (var p in PlayerHealth.All)
                if (p != _health && p.IsSpawned && !p.IsDowned && Flat(p.transform.position - c).magnitude < radius)
                    return true;
            return false;
        }

        /// <summary>Своя шахта под сиреной рядом, ракета летит на вражеский остров, место на ней есть — можно успеть сесть.</summary>
        float ScoreRide(out RocketSilo silo)
        {
            silo = null;
            if (OnEnemyIsland)
                return 0f;
            foreach (var s in RocketSilo.All)
            {
                if (s.Team != _health.Team || s.Island != _here || s.State != SiloState.Launching)
                    continue;
                float d = Flat(s.transform.position - transform.position).magnitude;
                if (d > 25f || d / FpsController.SprintSpeed + 0.3f > s.SirenLeft)
                    continue;
                if (IslandUnder(s.PendingTarget) != EnemyIsland || RocketRider.FreeSlot(s.NetIndex, _health) < 0)
                    continue;
                silo = s;
                return 2.2f * _traits.Ride;
            }
            return 0f;
        }

        // ---------- пульт ----------

        void BeginConsole(bool home)
        {
            _aimHome = home;
            _rideAfter = home || Random.value < _traits.RideChance;
            _consoleStep = ConsoleStep.Approach;
            _consoleTime = 0f;
            _prevLine = float.NaN;
            if (_taskSilo != null)
                ConsoleClaims[_taskSilo] = this;
        }

        void ReleaseConsole()
        {
            if (_taskSilo != null && ConsoleClaims.TryGetValue(_taskSilo, out var owner) && owner == this)
                ConsoleClaims.Remove(_taskSilo);
        }

        /// <summary>Флаг у нас, мы на вражеском острове: пускаем вражескую шахту по своему острову и улетаем на ней.</summary>
        bool RunCarryFlag()
        {
            if (CarriedFlag() == null)
                return false;
            if (!OnEnemyIsland)
            {
                Patrol(); // дома флаг засчитывается сам, как только встали на свою землю
                return true;
            }
            if (_taskSilo == null || _taskSilo.State == SiloState.Destroyed || _taskSilo.Island != _here)
            {
                ReleaseConsole();
                _taskSilo = null;
                float best = float.MaxValue;
                foreach (var s in RocketSilo.All)
                {
                    if (!UsableConsole(s, 1 - _health.Team, maxReload: 20f))
                        continue;
                    float d = Flat(s.Console.transform.position - transform.position).magnitude;
                    if (d < best)
                    {
                        best = d;
                        _taskSilo = s;
                    }
                }
                if (_taskSilo == null)
                {
                    Patrol(); // все шахты заняты или разбиты — держимся на ногах, ждём
                    return true;
                }
                BeginConsole(true);
            }
            return RunConsole();
        }

        bool RunConsole()
        {
            var silo = _taskSilo;
            if (silo == null || silo.State == SiloState.Destroyed || !MatchManager.ConsolesEnabled)
                return false;
            if (ConsoleClaims.TryGetValue(silo, out var other) && other != null && other != this)
                return false;
            ConsoleClaims[silo] = this;

            var console = silo.Console;
            Vector3 stand = console.transform.position;
            if (_consoleStep == ConsoleStep.Approach)
            {
                float d = Flat(stand - transform.position).magnitude;
                if (d > ConsoleStand)
                {
                    if (!MoveTo(stand, d > 12f || _aimHome, ConsoleStand))
                    {
                        Blacklist(silo);
                        return false;
                    }
                    return true;
                }
                _consoleStep = ConsoleStep.Wait;
                _consoleTime = 0f;
            }

            Stop();
            Face(stand + Vector3.up * 1.3f - Eye); // смотрит на экран пульта
            _consoleTime += Time.deltaTime;
            if (_consoleTime > ConsoleGiveUp)
            {
                _artilleryCooldown = 10f;
                return false;
            }
            if (Flat(stand - transform.position).magnitude > ConsoleStand + 1.5f)
            {
                _consoleStep = ConsoleStep.Approach; // оттолкнули / отбежали от ракеты
                return true;
            }
            // к пульту подошёл живой игрок — уступаем, пока не начали целиться
            if (_consoleStep <= ConsoleStep.Think && OtherAtConsole(silo, 3f))
                return _consoleTime < 6f;

            switch (_consoleStep)
            {
                case ConsoleStep.Wait:
                    if (silo.State == SiloState.Ready)
                    {
                        _consoleStep = ConsoleStep.Think;
                        _stepTimer = Random.Range(0.4f, 1.4f);
                        return true;
                    }
                    return silo.State == SiloState.Reloading && silo.ReloadLeft < 15f; // ждём перезарядку у пульта

                case ConsoleStep.Think:
                    _stepTimer -= Time.deltaTime;
                    if (_stepTimer > 0f)
                        return true;
                    _aimUv = PickAimUv(silo);
                    _consoleStep = ConsoleStep.AimX;
                    _prevLine = float.NaN;
                    return true;

                case ConsoleStep.AimX:
                    if (!console.XRunning)
                    {
                        Press(silo, ConsoleButtonKind.X, NetClock.Now); // пустить горизонтальную линию
                        _prevLine = float.NaN;
                        return true;
                    }
                    if (Crossed(console.V, _aimUv.y, out double tx))
                    {
                        Press(silo, ConsoleButtonKind.X, tx);
                        _consoleStep = ConsoleStep.AimY;
                        _prevLine = float.NaN;
                    }
                    return true;

                case ConsoleStep.AimY:
                    if (!console.YRunning)
                    {
                        Press(silo, ConsoleButtonKind.Y, NetClock.Now);
                        _prevLine = float.NaN;
                        return true;
                    }
                    if (Crossed(console.U, _aimUv.x, out double ty))
                    {
                        Press(silo, ConsoleButtonKind.Y, ty);
                        _consoleStep = ConsoleStep.Confirm;
                        _stepTimer = Random.Range(0.3f, 0.9f);
                    }
                    return true;

                case ConsoleStep.Confirm:
                    _stepTimer -= Time.deltaTime;
                    if (_stepTimer > 0f)
                        return true;
                    if (!console.HasTarget || (!_aimHome && console.TargetOnOwnIsland()))
                    {
                        // линии сбились или цель на своём острове — целимся заново
                        _consoleStep = ConsoleStep.Think;
                        _stepTimer = Random.Range(0.2f, 0.6f);
                        return true;
                    }
                    if (!console.CanLaunch)
                        return silo.State == SiloState.Ready;
                    Press(silo, ConsoleButtonKind.Launch, NetClock.Now);
                    if (silo.State != SiloState.Launching)
                        return false;
                    Debug.Log($"[RocketWars] bot {_health.DisplayName} launched silo {silo.NetIndex}{(_rideAfter ? " and rides it" : "")}");
                    _artilleryCooldown = Random.Range(15f, 35f) / _traits.Artillery;
                    if (!_rideAfter)
                        return false;
                    // сразу к ракете — сирена 2,5 с
                    ReleaseConsole();
                    _task = BotTask.Ride;
                    _taskTime = 0f;
                    return true;
            }
            return true;
        }

        void Press(RocketSilo silo, ConsoleButtonKind kind, double time) => _net.PressConsole(silo.NetIndex, kind, time);

        /// <summary>
        /// Линия прошла через нужную позицию между прошлым и этим кадром; time — момент пересечения по общим часам
        /// (сервер принимает нажатие «задним числом» в пределах 0,75 с — точно как у игрока с пингом).
        /// </summary>
        bool Crossed(float line, float target, out double time)
        {
            time = NetClock.Now;
            float prev = _prevLine;
            _prevLine = line;
            if (float.IsNaN(prev) || Mathf.Abs(line - prev) > 0.5f)
                return false;
            if ((prev - target) * (line - target) > 0f)
                return false;
            float step = Mathf.Abs(line - prev);
            float back = step > 1e-5f ? Mathf.Abs(line - target) / step : 0f;
            time -= Time.deltaTime * Mathf.Clamp01(back);
            return true;
        }

        /// <summary>Точка на экране пульта (uv карты команды шахты), куда бот хочет попасть.</summary>
        Vector2 PickAimUv(RocketSilo silo)
        {
            Vector3 point;
            var enemy = EnemyIsland;
            if (_aimHome)
            {
                point = SafeLandingPoint(MyIsland);
            }
            else if (_rideAfter)
            {
                // полетим сами — в середину острова, чтобы после прыжка с ракеты не перелететь его
                Vector2 r = Random.insideUnitCircle * 10f;
                point = enemy.Center + new Vector3(r.x, 0f, r.y);
            }
            else
            {
                float roll = Random.value;
                if (roll < 0.4f && TryRandomEnemySilo(out var target))
                    point = target.transform.position;
                else if (roll < 0.7f && TryRandomEnemyOn(enemy, out var victim))
                    point = victim.transform.position + RandomFlat(6f); // «примерно там», а не точно
                else
                    point = enemy.GetRandomSpawnPoint(0f);
                point += RandomFlat(Role == BotRole.Artillery ? 3f : 6f);
            }

            var map = TacticalMap.Instance;
            Vector2 uv = map != null ? map.WorldToScreen(silo.Team, point) : new Vector2(0.5f, 0.5f);
            return new Vector2(Mathf.Clamp(uv.x, 0.03f, 0.97f), Mathf.Clamp(uv.y, 0.03f, 0.97f));
        }

        bool TryRandomEnemySilo(out RocketSilo silo)
        {
            silo = null;
            int seen = 0;
            foreach (var s in RocketSilo.All)
                if (s.Team != _health.Team && s.State != SiloState.Destroyed && Random.Range(0, ++seen) == 0)
                    silo = s;
            return silo != null;
        }

        bool TryRandomEnemyOn(VoxelIsland island, out PlayerHealth enemy)
        {
            enemy = null;
            int seen = 0;
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned && !p.IsDowned && p.Team != _health.Team && IslandUnder(p.transform.position) == island
                    && Random.Range(0, ++seen) == 0)
                    enemy = p;
            return enemy != null;
        }

        /// <summary>Точка у середины своего острова подальше от союзников — туда летит ракета с флагом (взрыв никого не заденет).</summary>
        Vector3 SafeLandingPoint(VoxelIsland island)
        {
            Vector3 best = island.Center;
            float bestDist = -1f;
            for (int i = 0; i < 10; i++)
            {
                Vector3 p = island.GetRandomSpawnPoint(0f);
                if (!InInterior(island, p, 14f))
                    continue;
                float nearest = float.MaxValue;
                foreach (var ally in PlayerHealth.All)
                    if (ally != _health && ally.IsSpawned && ally.Team == _health.Team)
                        nearest = Mathf.Min(nearest, Flat(ally.transform.position - p).magnitude);
                if (nearest > bestDist)
                {
                    bestDist = nearest;
                    best = p;
                }
            }
            return best;
        }

        static Vector3 RandomFlat(float radius)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            return new Vector3(r.x, 0f, r.y);
        }

        // ---------- полёт ----------

        /// <summary>Бежим к шахте под сиреной и хватаемся за ракету.</summary>
        bool RunRideApproach()
        {
            var silo = _taskSilo;
            if (silo == null || silo.State != SiloState.Launching)
                return false;
            if (InGrabReach(silo))
            {
                Grab(silo);
                return false; // дальше — UpdateRide
            }
            if (!MoveTo(silo.transform.position, true, 1.9f))
                return false;
            if (Flat(silo.transform.position - transform.position).magnitude <= 1.9f)
                _fps.BotJump(); // не дотянулись — подпрыгнуть
            return true;
        }

        bool InGrabReach(RocketSilo silo)
        {
            Vector3 hands = transform.position + Vector3.up * HangBelow;
            Vector3 a = silo.LoadedRocketBottom, b = silo.LoadedRocketTop, ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(hands - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector3.Distance(hands, a + ab * t) - Models.RocketRadius <= GrabReach;
        }

        void Grab(RocketSilo silo)
        {
            int slot = RocketRider.FreeSlot(silo.NetIndex, _health);
            if (slot < 0)
                return;
            Stop();
            _fps.BotCrouch = false;
            _net.SetBotOptics(false, false);
            _fps.Attach();
            _net.SetRide(silo.NetIndex, slot);
            _rideSilo = silo.NetIndex;
            _rideRocket = null;
            _rideVelocity = Vector3.zero;
            _trickTimer = Random.Range(0.8f, 2f);
            _enemy = null;
            Debug.Log($"[RocketWars] bot {_health.DisplayName} rides rocket of silo {silo.NetIndex}");
        }

        void UpdateRide(bool active)
        {
            if (!_fps.Attached)
            {
                ForgetRide(); // сняли извне: новый раунд, телепорт
                return;
            }
            if (_rideRocket == null)
                _rideRocket = RocketRider.FindRocketFromSilo(_rideSilo);
            var silo = MatchNet.Instance != null ? MatchNet.Instance.GetSilo(_rideSilo) : null;
            bool waiting = _rideRocket == null && silo != null && silo.State == SiloState.Launching;
            if (!active || _health.IsDowned || (_rideRocket == null && !waiting))
            {
                ReleaseRide();
                return;
            }
            if (_rideRocket == null)
                return;

            if (ShouldJump(_rideRocket, silo))
            {
                ReleaseRide();
                return;
            }

            // трюки в полёте — у «живых» характеров чаще
            _trickTimer -= Time.deltaTime;
            if (_trickTimer <= 0f)
            {
                _trickTimer = Random.Range(0.8f, 2f);
                bool trick = _net.RideTrick == RideTrick.None && Random.value < 0.5f * _traits.Lively;
                _net.SetTrick(trick ? (RideTrick)Random.Range(1, 4) : RideTrick.None);
            }
        }

        /// <summary>Прыгаем, когда ракета снижается над серединой острова-цели (парашют раскроется сам), или в последний момент.</summary>
        bool ShouldJump(Rocket rocket, RocketSilo silo)
        {
            if (rocket.TimeToImpact < 0.7f)
                return true;
            if (rocket.LaunchBlend < 1f)
                return false;
            var dest = IslandUnder(rocket.Target);
            if (dest == null && silo != null)
                dest = VoxelIsland.ForTeam(1 - silo.Team);
            Vector3 p = rocket.transform.position;
            return dest != null && IslandUnder(p) == dest && InInterior(dest, p, 12f)
                   && rocket.TimeToImpact < 3.5f && rocket.CurrentVelocity.y < 0f;
        }

        void LateUpdate()
        {
            if (_rideSilo < 0 || !_fps.Attached)
                return;
            if (_rideRocket == null)
                _rideRocket = RocketRider.FindRocketFromSilo(_rideSilo);
            _rideVelocity = _rideRocket != null ? _rideRocket.CurrentVelocity : Vector3.zero;
            if (RocketRider.TryGetSeat(_rideSilo, _net.RideSlot, out var seat, out var rotation))
            {
                _fps.SetAttachedPosition(seat + rotation * Vector3.up * RocketRider.RootOut);
                _fps.CameraRoot.position = seat + rotation * RocketRider.SeatEye;
            }
        }

        void ReleaseRide()
        {
            _fps.Release(_rideVelocity);
            ForgetRide();
        }

        void ForgetRide()
        {
            _rideSilo = -1;
            _rideRocket = null;
            _net.SetRide(-1, 0);
            _net.SetTrick(RideTrick.None);
            _thinkTimer = 0f;
        }
    }
}
