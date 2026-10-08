using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketWars.Player
{
    /// <summary>
    /// Подъём лежащего союзника: удерживать E рядом 3 с. Прерывается отпусканием, уходом из радиуса
    /// или уроном по поднимающему (после урона нужно отпустить и нажать E заново).
    /// Прогресс считает владелец, сам подъём подтверждает сервер.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth), typeof(FpsController), typeof(NetworkPlayer))]
    public class ReviveInteraction : MonoBehaviour
    {
        public const float ReviveDuration = 3f;
        public const float Range = 2.5f;

        public PlayerHealth Target { get; private set; }
        public float Progress { get; private set; }
        public float Progress01 => Progress / ReviveDuration;
        public bool IsReviving => Progress > 0f;

        PlayerHealth _health;
        FpsController _controller;
        NetworkPlayer _net;
        bool _needRelease;
        RocketRider _rider;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _controller = GetComponent<FpsController>();
            _net = GetComponent<NetworkPlayer>();
        }

        void OnEnable() => _health.Hurt += OnHurt;
        void OnDisable() => _health.Hurt -= OnHurt;

        void OnHurt(float amount)
        {
            if (IsReviving)
                _needRelease = true;
            Progress = 0f;
        }

        void Update()
        {
            if (!_controller.IsLocal)
                return;

            // рядом ракета или уже висим на ней — E хватает ракету, а не поднимает союзника
            if (_rider == null)
                _rider = GetComponent<RocketRider>();
            bool rocketBusy = _rider != null && (_rider.IsRiding || _rider.HasGrabTarget);
            Target = _health.IsDowned || rocketBusy ? null : FindTarget();
            var kb = Keyboard.current;
            bool held = kb != null && kb.eKey.isPressed;
            if (!held)
                _needRelease = false;

            if (Target == null || !held || _needRelease)
            {
                Progress = 0f;
                return;
            }

            Progress += Time.deltaTime;
            if (Progress >= ReviveDuration)
            {
                _net.RequestRevive(Target);
                Progress = 0f;
                _needRelease = true;
            }
        }

        PlayerHealth FindTarget()
        {
            PlayerHealth best = null;
            float bestDist = Range;
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.IsDowned || p.Team != _health.Team)
                    continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d <= bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }

            // держим ту же цель, если она ещё в радиусе, чтобы прогресс не сбрасывался
            if (Target != null && Target != best && Target.IsDowned &&
                Vector3.Distance(transform.position, Target.transform.position) <= Range)
                return Target;
            if (best != Target)
                Progress = 0f;
            return best;
        }
    }
}
