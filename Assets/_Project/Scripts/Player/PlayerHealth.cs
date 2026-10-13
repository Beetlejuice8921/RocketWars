using System;
using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Player
{
    public enum PlayerState : byte
    {
        Alive,
        Downed
    }

    /// <summary>
    /// HP игрока. На 0 HP игрок не умирает, а ложится (Downed): неуязвим, не двигается, ждёт подъёма союзником.
    /// Бронежилет даёт ещё до 100 единиц брони: любой урон сначала снимает броню, остаток — здоровье.
    /// Значения живут в NetworkVariable, менять их может только сервер.
    /// </summary>
    public class PlayerHealth : NetworkBehaviour, IDamageable
    {
        public const float MaxHp = 100f;
        public const float FallPenalty = 50f;
        public const float ReviveHp = 50f;
        public const float CrouchDamageMultiplier = 2f / 3f;
        public const float MaxArmor = 100f;

        public static readonly List<PlayerHealth> All = new List<PlayerHealth>();

        // HP и броня зашифрованы в памяти (ObscuredFloat): иначе хост нашёл бы «100» ArtMoney и стал бессмертным
        readonly NetworkVariable<ObscuredFloat> _hp = new NetworkVariable<ObscuredFloat>(MaxHp);
        readonly NetworkVariable<ObscuredFloat> _armor = new NetworkVariable<ObscuredFloat>(0f);
        readonly NetworkVariable<PlayerState> _state = new NetworkVariable<PlayerState>(PlayerState.Alive);
        readonly NetworkVariable<int> _team = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString32Bytes> _name = new NetworkVariable<FixedString32Bytes>();

        public int Team => _team.Value;
        // ник читают каждый кадр (HUD, панель команд) — строку собираем только при смене имени
        FixedString32Bytes _displayNameSource;
        string _displayName = "";

        public string DisplayName
        {
            get
            {
                var name = _name.Value;
                if (name != _displayNameSource)
                {
                    _displayNameSource = name;
                    _displayName = name.ToString();
                }
                return _displayName;
            }
        }
        public float Hp => _hp.Value;
        public float Armor => _armor.Value;
        public PlayerState State => _state.Value;
        public bool IsDowned => State == PlayerState.Downed;

        /// <summary>Только на сервере: урон получен (info, фактически снятые HP).</summary>
        public event Action<DamageInfo, float> Damaged;
        /// <summary>У всех: HP или броня уменьшились (на сколько).</summary>
        public event Action<float> Hurt;
        /// <summary>У всех: изменилось состояние.</summary>
        public event Action<PlayerState> StateChanged;
        /// <summary>У всех: игрок перешёл в другую команду (лобби / после матча).</summary>
        public event Action<int> TeamChanged;
        /// <summary>Сервер: игрок лёг (кто и чем его положил) — для статистики.</summary>
        public static event Action<PlayerHealth, DamageInfo> AnyDowned;

        NetworkPlayer _net;
        int _pendingTeam;
        string _pendingName;

        void Awake() => _net = GetComponent<NetworkPlayer>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public override void OnNetworkSpawn()
        {
            if (IsServer && _pendingName != null)
            {
                _team.Value = _pendingTeam;
                _name.Value = new FixedString32Bytes(_pendingName);
            }
            _hp.OnValueChanged += OnHpChanged;
            _armor.OnValueChanged += OnHpChanged;
            _state.OnValueChanged += OnStateChanged;
            _team.OnValueChanged += OnTeamChanged;
        }

        public override void OnNetworkDespawn()
        {
            _hp.OnValueChanged -= OnHpChanged;
            _armor.OnValueChanged -= OnHpChanged;
            _state.OnValueChanged -= OnStateChanged;
            _team.OnValueChanged -= OnTeamChanged;
        }

        void OnTeamChanged(int oldValue, int newValue) => TeamChanged?.Invoke(newValue);

        void OnHpChanged(ObscuredFloat oldValue, ObscuredFloat newValue)
        {
            if (newValue < oldValue)
                Hurt?.Invoke(oldValue.Value - newValue.Value);
        }

        void OnStateChanged(PlayerState oldValue, PlayerState newValue) => StateChanged?.Invoke(newValue);

        /// <summary>Сервер, до Spawn: команда и имя (применятся при спавне).</summary>
        public void ServerInit(int team, string displayName)
        {
            _pendingTeam = team;
            _pendingName = displayName.Length > 28 ? displayName.Substring(0, 28) : displayName;
        }

        /// <summary>Сервер: перевести в другую команду (MatchNet.ServerSwitchTeam).</summary>
        public void ServerSetTeam(int team)
        {
            if (IsServer)
                _team.Value = team;
        }

        /// <summary>Команда, назначенная сервером, — доступна и до спавна.</summary>
        public int ServerTeam => IsSpawned ? Team : _pendingTeam;

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsServer || IsDowned || info.Amount <= 0)
                return;

            float amount = info.Amount;
            if (_net != null && _net.IsCrouching)
                amount *= CrouchDamageMultiplier;
            ApplyDamage(info, amount);
        }

        /// <summary>Сервер: штраф за падение с острова: −50 HP, при HP ≤ 50 — ложится. Лежащий остаётся лежать.</summary>
        public void ApplyFallPenalty()
        {
            if (!IsServer || IsDowned)
                return;
            ApplyDamage(new DamageInfo(FallPenalty, transform.position, gameObject, Team, DamageKind.Fall), FallPenalty);
        }

        void ApplyDamage(in DamageInfo info, float amount)
        {
            // сначала тратится броня, остаток урона — здоровье
            float absorbed = Mathf.Min(Armor, amount);
            if (absorbed > 0f)
                _armor.Value = Armor - absorbed;
            float applied = Mathf.Min(Hp, amount - absorbed);
            _hp.Value = Hp - applied;
            Damaged?.Invoke(info, absorbed + applied);
            if (Hp <= 0f)
            {
                SetState(PlayerState.Downed);
                AnyDowned?.Invoke(this, info);
            }
        }

        public void Revive()
        {
            if (!IsServer || !IsDowned)
                return;
            _hp.Value = ReviveHp;
            SetState(PlayerState.Alive);
        }

        /// <summary>Сервер: аптечка. false — лечить нечего (лежит или здоровье полное).</summary>
        public bool Heal(float amount)
        {
            if (!IsServer || IsDowned || Hp >= MaxHp)
                return false;
            _hp.Value = Mathf.Min(MaxHp, Hp + amount);
            return true;
        }

        /// <summary>Сервер: выставить броню напрямую (берсерк даёт 200, по окончании возвращает прежнюю).</summary>
        public void SetArmor(float value)
        {
            if (IsServer)
                _armor.Value = Mathf.Max(0f, value);
        }

        /// <summary>Сервер: надеть бронежилет (броня восстанавливается до максимума).</summary>
        public void GiveArmor()
        {
            if (IsServer)
                _armor.Value = MaxArmor;
        }

        /// <summary>Сервер: полный сброс в начале раунда.</summary>
        public void ResetFull()
        {
            if (!IsServer)
                return;
            _hp.Value = MaxHp;
            _armor.Value = 0f;
            SetState(PlayerState.Alive);
        }

        void SetState(PlayerState state)
        {
            if (state == PlayerState.Downed)
                _hp.Value = 0f;
            _state.Value = state;
        }
    }
}
