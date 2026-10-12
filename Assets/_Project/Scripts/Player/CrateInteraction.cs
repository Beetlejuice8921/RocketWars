using RocketWars.Core;
using RocketWars.Loot;
using RocketWars.UI;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Ящики с припасами у локального игрока: удерживать E рядом с закрытым ящиком 1,5 с — открыть
    /// (раненый сразу применяет аптечку); предмет из открытого ящика берётся нажатием E;
    /// у ящика с аптечками нажать E — вылечиться на 60 HP (аптечку нельзя унести с собой).
    /// Ракета и подъём союзника важнее ящика. Сами решения принимает сервер.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth), typeof(NetworkPlayer))]
    public class CrateInteraction : MonoBehaviour
    {
        public LootCrate Target { get; private set; }
        public float Progress { get; private set; }
        public float Progress01 => Progress / LootCrate.OpenTime;
        public bool IsOpening => Progress > 0f;

        PlayerHealth _health;
        NetworkPlayer _net;
        RocketRider _rider;
        ReviveInteraction _revive;
        bool _needRelease;
        LootCrate _holdTarget;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _net = GetComponent<NetworkPlayer>();
        }

        void OnEnable()
        {
            _health.Hurt += OnHurt;
            _net.LootReceived += OnLoot;
        }

        void OnDisable()
        {
            _health.Hurt -= OnHurt;
            _net.LootReceived -= OnLoot;
            SetTarget(null);
        }

        /// <summary>Урон сбивает открытие — как и подъём союзника.</summary>
        void OnHurt(float amount)
        {
            if (IsOpening)
                _needRelease = true;
            Progress = 0f;
        }

        /// <summary>Взятый предмет (пулемёт, снайперка, броня, маяк) — щелчок «взял»; аптечки остаются в ящике.</summary>
        void OnLoot(LootKind kind)
        {
            if (kind != LootKind.Medkits)
                Sfx.Play2D(Sfx.Pickup, 0.8f);
        }

        void Update()
        {
            if (_rider == null) _rider = GetComponent<RocketRider>();
            if (_revive == null) _revive = GetComponent<ReviveInteraction>();

            bool held = GameInput.IsPressed(GameAction.Use) && !UiRoot.CursorFree;
            bool pressed = GameInput.WasPressed(GameAction.Use) && !UiRoot.CursorFree;
            if (!held)
                _needRelease = false;

            bool busy = _health.IsDowned || MatchManager.FreezePlayers ||
                        (_rider != null && (_rider.IsRiding || _rider.HasGrabTarget)) ||
                        (_revive != null && _revive.Target != null);
            SetTarget(busy ? null : FindTarget());
            if (Target == null)
            {
                Progress = 0f;
                return;
            }

            if (Target.State == CrateState.Closed)
            {
                if (!held || _needRelease)
                {
                    Progress = 0f;
                }
                else
                {
                    // начало удержания (или другой ящик) — серверу: он сам засекает, прошло ли 1,5 с
                    if (Progress <= 0f || Target != _holdTarget)
                    {
                        Progress = 0f;
                        _holdTarget = Target;
                        _net.BeginHold(HoldKind.Crate, (ulong)Target.NetIndex);
                    }
                    Progress += Time.deltaTime;
                    if (Progress >= LootCrate.OpenTime)
                    {
                        _net.OpenCrate(Target);
                        Progress = 0f;
                        _needRelease = true; // предмет из только что открытого ящика — отдельным нажатием
                    }
                }
            }
            else
            {
                Progress = 0f;
                if (pressed && !_needRelease && Target.HasItem)
                    _net.TakeLoot(Target);
                else if (pressed && !_needRelease && Target.Medkits > 0 && _health.Hp < PlayerHealth.MaxHp)
                    _net.UseMedkit(Target);
            }
        }

        void SetTarget(LootCrate crate)
        {
            if (Target == crate)
                return;
            Target = crate;
            Progress = 0f;
        }

        /// <summary>Ближайший ящик рядом: закрытый — открыть, открытый — только если в нём что-то осталось.</summary>
        LootCrate FindTarget()
        {
            LootCrate best = null;
            float bestDist = LootCrate.UseRange;
            foreach (var crate in LootCrate.All)
            {
                if (crate.State == CrateState.Destroyed || crate.IsFalling)
                    continue;
                if (crate.State == CrateState.Open && crate.Items <= 0)
                    continue;
                Vector3 d = crate.Center - transform.position;
                if (d.y < -2f || d.y > 2.5f)
                    continue;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist <= bestDist)
                {
                    best = crate;
                    bestDist = dist;
                }
            }
            return best;
        }
    }
}
