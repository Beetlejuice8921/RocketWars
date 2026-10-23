using RocketWars.Core;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Подъём лежащего союзника: удерживать E рядом 3 с. Прерывается отпусканием или уходом из радиуса;
    /// урон по поднимающему прогресс не сбрасывает.
    /// Прогресс считает владелец, сам подъём подтверждает сервер.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth), typeof(FpsController), typeof(NetworkPlayer))]
    public class ReviveInteraction : MonoBehaviour
    {
        public const float ReviveDuration = 3f;
        /// <summary>Дальность подъёма — до ближайшей точки лежащего тела (а не до ног), поэтому подойти можно с любой стороны.</summary>
        public const float Range = 3f;
        /// <summary>Лежащее тело вытянуто вдоль forward на ±BodyHalfLength от центра (см. PlayerVisual.UpdatePose).</summary>
        const float BodyHalfLength = 1f;

        public PlayerHealth Target { get; private set; }
        public float Progress { get; private set; }
        public float Progress01 => Progress / ReviveDuration;
        public bool IsReviving => Progress > 0f;

        PlayerHealth _health;
        FpsController _controller;
        NetworkPlayer _net;
        bool _needRelease;
        RocketRider _rider;
        PlayerHealth _holdTarget;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _controller = GetComponent<FpsController>();
            _net = GetComponent<NetworkPlayer>();
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
            bool held = GameInput.IsPressed(GameAction.Use);
            if (!held)
                _needRelease = false;

            if (Target == null || !held || _needRelease)
            {
                Progress = 0f;
                return;
            }

            // начало удержания (или смена цели) — серверу: он сам засекает, прошло ли 3 с
            if (Progress <= 0f || Target != _holdTarget)
            {
                Progress = 0f;
                _holdTarget = Target;
                _net.BeginHold(HoldKind.Revive, Target.NetworkObjectId);
            }
            Progress += Time.deltaTime;
            if (Progress >= ReviveDuration)
            {
                _net.RequestRevive(Target);
                Progress = 0f;
                _needRelease = true;
            }
        }

        /// <summary>Расстояние от точки до лежащего тела: до отрезка «ноги — голова», а не до одной точки в центре.</summary>
        public static float DistanceToBody(Vector3 from, PlayerHealth target)
        {
            var t = target.transform;
            Vector3 axis = t.forward * BodyHalfLength;
            Vector3 a = t.position - axis, b = t.position + axis;
            Vector3 ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(from - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(from, a + ab * k);
        }

        PlayerHealth FindTarget()
        {
            PlayerHealth best = null;
            float bestDist = Range;
            foreach (var p in PlayerHealth.All)
            {
                if (p == _health || !p.IsDowned || p.Team != _health.Team)
                    continue;
                float d = DistanceToBody(transform.position, p);
                if (d <= bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }

            // держим ту же цель, если она ещё в радиусе, чтобы прогресс не сбрасывался
            if (Target != null && Target != best && Target.IsDowned &&
                DistanceToBody(transform.position, Target) <= Range)
                return Target;
            if (best != Target)
                Progress = 0f;
            return best;
        }
    }
}
