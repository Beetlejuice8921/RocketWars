using RocketWars.Launcher;
using RocketWars.Net;
using RocketWars.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketWars.Player
{
    /// <summary>
    /// «Полёт на ракете» для локального игрока: зажал E рядом с ракетой — повис на ней (до 4 человек на ракету),
    /// отпустил — отцепился и летит дальше по инерции (а там раскроется парашют).
    /// Схватиться можно за заряженную ракету во время сирены (тогда улетаешь вместе с ней при пуске)
    /// или за пролетающую ракету в пределах 3 м. Позицию висящего ведёт владелец — NetworkTransform показывает её всем.
    /// </summary>
    [RequireComponent(typeof(FpsController), typeof(NetworkPlayer), typeof(PlayerHealth))]
    public class RocketRider : MonoBehaviour
    {
        public const int MaxRiders = 4;
        const float GrabRange = 3f;
        /// <summary>От ног до вытянутых вверх кистей.</summary>
        const float HangBelow = 2.05f;

        // в полёте: держатся за брюхо ракеты — по двое ближе к носу и к хвосту (система координат ракеты, +Z — нос)
        static readonly float Belly = -(Core.Models.RocketRadius + 0.05f);
        static readonly Vector3[] Slots =
        {
            new Vector3(0.5f, Belly, 1.2f), new Vector3(-0.5f, Belly, 1.2f),
            new Vector3(0.5f, Belly, -0.9f), new Vector3(-0.5f, Belly, -0.9f),
        };

        // во время сирены: стоят на крыше шахты вокруг ракеты и держатся за неё
        const float SiloGripHeight = 4.6f;
        // дальше радиуса капсулы игрока от корпуса: стоящая ракета теперь твёрдая
        static readonly float SiloGripRadius = Core.Models.RocketRadius + 0.45f;

        public bool IsRiding => _siloIndex >= 0;
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
            var kb = Keyboard.current;
            bool held = kb != null && kb.eKey.isPressed && !UiRoot.CursorFree;

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
                Release();
        }

        void LateUpdate()
        {
            if (!IsRiding || !_fps.Attached)
                return;

            if (_rocket == null)
                _rocket = FindRocketFromSilo(_siloIndex);
            _lastVelocity = _rocket != null ? _rocket.CurrentVelocity : Vector3.zero;
            if (TryGetHangPosition(_siloIndex, _slot, out var pos))
                _fps.SetAttachedPosition(pos);
        }

        /// <summary>
        /// Где должен быть (ногами) игрок, висящий в слоте slot на ракете шахты silo: по летящей ракете,
        /// а до пуска — по ракете, стоящей в шахте. Используется и для своих, и для чужих (по локальной копии ракеты).
        /// </summary>
        public static bool TryGetHangPosition(int silo, int slot, out Vector3 feet)
        {
            feet = default;
            slot = Mathf.Clamp(slot, 0, Slots.Length - 1);
            var rocket = FindRocketFromSilo(silo);
            Vector3 attach;
            if (rocket != null)
            {
                attach = rocket.transform.position + rocket.transform.rotation * Slots[slot];
            }
            else
            {
                var s = MatchNet.Instance != null ? MatchNet.Instance.GetSilo(silo) : null;
                if (s == null)
                    return false;
                // ракета в шахте стоит носом вверх: четверо по кругу на крыше башни
                Vector3 dir = Quaternion.Euler(0f, 45f + slot * 90f, 0f) * Vector3.forward;
                attach = s.transform.position + Vector3.up * SiloGripHeight + dir * SiloGripRadius;
            }
            feet = attach - Vector3.up * HangBelow;
            return true;
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
            TargetFull = HasGrabTarget && FreeSlot(silo) < 0;
        }

        void Grab(int silo, Rocket rocket)
        {
            int slot = FreeSlot(silo);
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
        }

        /// <summary>Первый свободный слот на ракете этой шахты (по сетевым флагам других игроков) или −1.</summary>
        int FreeSlot(int silo)
        {
            int used = 0;
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.TryGetComponent(out NetworkPlayer np) || np.RideSilo != silo)
                    continue;
                used |= 1 << np.RideSlot;
            }
            for (int i = 0; i < MaxRiders; i++)
                if ((used & (1 << i)) == 0)
                    return i;
            return -1;
        }

        static Rocket FindRocketFromSilo(int silo)
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
