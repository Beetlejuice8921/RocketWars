using RocketWars.Core;
using RocketWars.Flags;
using RocketWars.Net;
using RocketWars.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketWars.Player
{
    /// <summary>
    /// Вражеский флаг у локального игрока: рядом с ним (на базе или на земле) нажать E — забрать.
    /// Ракета, подъём союзника и ящик рядом важнее. Решает сервер (NetworkPlayer.TakeFlag).
    /// </summary>
    [RequireComponent(typeof(PlayerHealth), typeof(NetworkPlayer))]
    public class FlagInteraction : MonoBehaviour
    {
        /// <summary>Вражеский флаг, который можно забрать прямо сейчас.</summary>
        public TeamFlag Target { get; private set; }
        /// <summary>Флаг, который несёт локальный игрок (null — не несёт).</summary>
        public TeamFlag Carrying { get; private set; }

        PlayerHealth _health;
        NetworkPlayer _net;
        RocketRider _rider;
        ReviveInteraction _revive;
        CrateInteraction _crates;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _net = GetComponent<NetworkPlayer>();
        }

        void Update()
        {
            if (_rider == null) _rider = GetComponent<RocketRider>();
            if (_revive == null) _revive = GetComponent<ReviveInteraction>();
            if (_crates == null) _crates = GetComponent<CrateInteraction>();

            Target = null;
            Carrying = null;
            var match = MatchNet.Instance;
            if (match == null)
                return;

            foreach (var flag in match.Flags)
            {
                if (flag == null)
                    continue;
                if (flag.State == FlagState.Carried && flag.CarrierId == _net.NetworkObjectId)
                    Carrying = flag;
                else if (flag.Team != _health.Team && flag.State != FlagState.Carried && InReach(flag))
                    Target = flag;
            }

            bool busy = _health.IsDowned || MatchManager.FreezePlayers || UiRoot.CursorFree ||
                        (_rider != null && (_rider.IsRiding || _rider.HasGrabTarget)) ||
                        (_revive != null && _revive.Target != null) ||
                        (_crates != null && _crates.Target != null);
            if (busy)
            {
                Target = null;
                return;
            }

            var kb = Keyboard.current;
            if (Target != null && kb != null && kb.eKey.wasPressedThisFrame)
                _net.TakeFlag(Target);
        }

        bool InReach(TeamFlag flag)
        {
            Vector3 d = flag.Position - transform.position;
            if (d.y < -2f || d.y > 2.5f)
                return false;
            d.y = 0f;
            return d.magnitude <= TeamFlag.PickupRange;
        }
    }
}
