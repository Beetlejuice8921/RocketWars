using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Net;
using RocketWars.UI;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>Трюк на ракете: брейк (вращение на голове), руки-крылья, дэб.</summary>
    public enum RideTrick : byte { None, Breakdance, Wings, Dab }

    /// <summary>
    /// «Полёт на ракете» для локального игрока: зажал E рядом с ракетой — сел на неё верхом (до 4 человек друг за другом),
    /// отпустил — спрыгнул и летит дальше по инерции (а там раскроется парашют).
    /// Сесть можно на заряженную ракету во время сирены (она стоит носом вверх — сидят «как космонавты», лицом в небо;
    /// после пуска ракета ложится на курс, и сидящие оказываются сверху) или на пролетающую ракету в пределах 3 м.
    /// В полёте зажатый Q — трюк (вид от третьего лица, случайная анимация — её рисует PlayerVisual).
    /// Позицию сидящего ведёт владелец — NetworkTransform показывает её всем, а тело рисуется по локальной копии ракеты.
    /// </summary>
    [DefaultExecutionOrder(-50)] // раньше PlayerVisual: тело ставится на ракету уже после того, как сдвинулся корень игрока
    [RequireComponent(typeof(FpsController), typeof(NetworkPlayer), typeof(PlayerHealth))]
    public class RocketRider : MonoBehaviour
    {
        public const int MaxRiders = 4;
        const float GrabRange = 3f;
        /// <summary>От ног до вытянутых вверх кистей (тянемся к ракете).</summary>
        const float HangBelow = 2.05f;

        // верхом, друг за другом вдоль спины ракеты (система координат ракеты: +Z — нос, +Y — спина, в полёте она сверху);
        // заднее место — ещё над крышей шахты, пока ракета стоит
        static readonly float[] SeatZ = { 1.35f, 0.65f, -0.05f, -0.75f };
        static readonly float SeatLift = Core.Models.RocketRadius + 0.02f;
        /// <summary>От подошв (pivot тела) до низа бёдер сидящего: тело опускаем так, чтобы бёдра легли на корпус.</summary>
        public const float HipLift = 0.72f;
        /// <summary>Глаза сидящего относительно сиденья (в осях тела).</summary>
        public static readonly Vector3 SeatEye = new Vector3(0f, 1.75f - HipLift, 0.1f);
        /// <summary>Капсула игрока — над сиденьем, вне стоящей (твёрдой) ракеты: спрыгнул — и не застрял в корпусе.</summary>
        public const float RootOut = 0.45f;

        public bool IsRiding => _siloIndex >= 0;
        /// <summary>Ракета уже летит (можно делать трюки).</summary>
        public bool InFlight => IsRiding && _rocket != null;
        /// <summary>Есть за что схватиться прямо сейчас (для подсказки и приоритета над подъёмом союзника).</summary>
        public bool HasGrabTarget { get; private set; }
        public bool TargetFull { get; private set; }

        FpsController _fps;
        NetworkPlayer _net;
        PlayerHealth _health;
        int _siloIndex = -1;
        int _slot;
        Rocket _rocket;
        Vector3 _lastVelocity;

        void Awake()
        {
            _fps = GetComponent<FpsController>();
            _net = GetComponent<NetworkPlayer>();
            _health = GetComponent<PlayerHealth>();
        }

        void OnDisable()
        {
            if (IsRiding)
                Release();
        }

        void Update()
        {
            bool held = GameInput.IsPressed(GameAction.Use) && !UiRoot.CursorFree;

            if (IsRiding)
            {
                UpdateRiding(held);
                return;
            }

            FindTarget(out int silo, out Rocket rocket);
            if (HasGrabTarget && !TargetFull && held && !_health.IsDowned)
                Grab(silo, rocket);
        }

        void UpdateRiding(bool held)
        {
            HasGrabTarget = false;
            // сняли извне (телепорт нового раунда, возврат после падения) — просто забываем ракету
            if (!_fps.Attached)
            {
                Forget();
                return;
            }

            // ракета вылетела из шахты, на которой мы висели во время сирены
            if (_rocket == null)
                _rocket = FindRocketFromSilo(_siloIndex);

            var silo = MatchNet.Instance != null ? MatchNet.Instance.GetSilo(_siloIndex) : null;
            bool siloStillLaunching = silo != null && silo.State == SiloState.Launching;
            if (!held || _health.IsDowned || (_rocket == null && !siloStillLaunching))
            {
                Release();
                return;
            }

            // трюк — только в полёте и пока зажата клавиша трюка; какой именно — случайно в момент нажатия
            bool trick = _rocket != null && GameInput.IsPressed(GameAction.Trick) && !UiRoot.CursorFree;
            if (trick && _net.RideTrick == RideTrick.None)
                _net.SetTrick((RideTrick)Random.Range(1, 4));
            else if (!trick && _net.RideTrick != RideTrick.None)
                _net.SetTrick(RideTrick.None);
        }

        void LateUpdate()
        {
            if (!IsRiding || !_fps.Attached)
                return;

            if (_rocket == null)
                _rocket = FindRocketFromSilo(_siloIndex);
            _lastVelocity = _rocket != null ? _rocket.CurrentVelocity : Vector3.zero;
            if (TryGetSeat(_siloIndex, _slot, out var seat, out var rotation))
            {
                _fps.SetAttachedPosition(seat + rotation * Vector3.up * RootOut);
                // глаза — в голове сидящего (пока ракета стоит, смотрим из «космонавта», лежащего на спине)
                _fps.CameraRoot.position = seat + rotation * SeatEye;
            }
        }

        /// <summary>
        /// Место в слоте slot на ракете шахты silo: точка на спине ракеты под бёдрами и поворот тела
        /// (лицом к носу, макушкой от корпуса). По летящей ракете, а до пуска — по стоящей в шахте.
        /// Используется и для своих, и для чужих (по локальной копии ракеты).
        /// </summary>
        public static bool TryGetSeat(int silo, int slot, out Vector3 seat, out Quaternion rotation)
        {
            seat = default;
            rotation = Quaternion.identity;
            slot = Mathf.Clamp(slot, 0, SeatZ.Length - 1);
            var s = MatchNet.Instance != null ? MatchNet.Instance.GetSilo(silo) : null;
            var rocket = FindRocketFromSilo(silo);
            Vector3 center, axis, back;
            if (rocket != null)
            {
                center = rocket.transform.position;
                axis = rocket.transform.forward;
                back = rocket.transform.up;
                // на разгоне ракета доворачивает крен к цели — сидящие плавно переезжают со спины стоявшей ракеты
                float blend = rocket.LaunchBlend;
                if (s != null && blend < 1f)
                {
                    Vector3 b = Vector3.ProjectOnPlane(Vector3.Slerp(SiloBack(s), back, blend), axis);
                    if (b.sqrMagnitude > 1e-4f)
                        back = b.normalized;
                }
            }
            else
            {
                if (s == null)
                    return false;
                center = s.LoadedRocketCenter;
                axis = Vector3.up;
                back = SiloBack(s);
            }
            rotation = Quaternion.LookRotation(axis, back);
            seat = center + axis * SeatZ[slot] + back * SeatLift;
            return true;
        }

        /// <summary>
        /// Спина стоящей ракеты — сторона от вражеских шахт, по ближайшей оси (как крен ракеты на старте):
        /// при пуске по врагу сидящие уже на своих местах и после поворота на курс окажутся сверху.
        /// </summary>
        static Vector3 SiloBack(RocketSilo silo)
        {
            Vector3 enemy = Vector3.zero;
            int count = 0;
            foreach (var o in RocketSilo.All)
                if (o != null && o.Team != silo.Team)
                {
                    enemy += o.transform.position;
                    count++;
                }
            Vector3 away = count > 0 ? silo.transform.position - enemy / count : Vector3.back;
            return Mathf.Abs(away.x) > Mathf.Abs(away.z)
                ? new Vector3(Mathf.Sign(away.x), 0f, 0f)
                : new Vector3(0f, 0f, away.z < 0f ? -1f : 1f);
        }

        void FindTarget(out int silo, out Rocket rocket)
        {
            silo = -1;
            rocket = null;
            HasGrabTarget = false;
            TargetFull = false;
            if (_health.IsDowned)
                return;

            // руки — над головой: ищем ракету рядом с поднятыми кистями
            Vector3 hands = transform.position + Vector3.up * HangBelow;
            float best = GrabRange;

            foreach (var r in Rocket.Active)
            {
                Vector3 axis = r.transform.forward;
                float d = DistanceToSegment(hands, r.transform.position - axis * Core.Models.RocketTail, r.transform.position + axis * Core.Models.RocketNose)
                          - Core.Models.RocketRadius; // до поверхности корпуса, а не до оси
                if (d < best)
                {
                    best = d;
                    rocket = r;
                    silo = r.SiloIndex;
                }
            }

            foreach (var s in RocketSilo.All)
            {
                if (s.State != SiloState.Launching)
                    continue;
                float d = DistanceToSegment(hands, s.LoadedRocketBottom, s.LoadedRocketTop) - Core.Models.RocketRadius;
                if (d < best)
                {
                    best = d;
                    rocket = null;
                    silo = s.NetIndex;
                }
            }

            HasGrabTarget = silo >= 0;
            TargetFull = HasGrabTarget && FreeSlot(silo, _health) < 0;
        }

        void Grab(int silo, Rocket rocket)
        {
            int slot = FreeSlot(silo, _health);
            if (slot < 0)
                return;
            _siloIndex = silo;
            _slot = slot;
            _rocket = rocket;
            _fps.Attach();
            _net.SetRide(silo, slot);
        }

        void Release()
        {
            _fps.Release(_lastVelocity);
            Forget();
        }

        void Forget()
        {
            _siloIndex = -1;
            _rocket = null;
            _net.SetRide(-1, 0);
            _net.SetTrick(RideTrick.None);
        }

        /// <summary>Первый свободный слот на ракете этой шахты (по сетевым флагам других игроков) или −1.</summary>
        public static int FreeSlot(int silo, PlayerHealth self)
        {
            int used = 0;
            foreach (var p in PlayerHealth.All)
            {
                if (p == self || !p.TryGetComponent(out NetworkPlayer np) || np.RideSilo != silo)
                    continue;
                used |= 1 << np.RideSlot;
            }
            for (int i = 0; i < MaxRiders; i++)
                if ((used & (1 << i)) == 0)
                    return i;
            return -1;
        }

        public static Rocket FindRocketFromSilo(int silo)
        {
            foreach (var r in Rocket.Active)
                if (r.SiloIndex == silo)
                    return r;
            return null;
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
