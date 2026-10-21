using RocketWars.Core;
using RocketWars.UI;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Бинокль локального игрока: пока зажата ПКМ — увеличение ×5 (сужение поля зрения), сниженная чувствительность
    /// мыши и дальномер. Состояние уходит в сеть через NetworkPlayer — остальные видят бинокль у лица.
    /// Лёжа, в паузе и в меню бинокль недоступен.
    /// Со снайперкой в руках ПКМ — её оптика ×4 (IsScope): своя маска на экране, стрелять можно, врагам виден блик.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(FpsController), typeof(PlayerHealth))]
    public class Binoculars : MonoBehaviour
    {
        public const float Zoom = 5f;
        public const float RaiseTime = 0.15f;
        const float MaxRange = 600f;

        public Camera cam;
        public float baseFov = 75f;

        /// <summary>0 — опущен, 1 — у глаз (плавно).</summary>
        public float Amount { get; private set; }
        public bool IsUp { get; private set; }
        /// <summary>Расстояние до точки в центре экрана (м), или −1, если там пустота.</summary>
        public float Distance { get; private set; } = -1f;
        /// <summary>Это оптика снайперки (×4), а не бинокль.</summary>
        public bool IsScope { get; private set; }

        NetworkPlayer _net;
        FpsController _fps;
        PlayerHealth _health;
        PistolView _weapon;
        float _zoomFov, _scopeFov;
        bool _waitRelease;

        void Awake()
        {
            _net = GetComponent<NetworkPlayer>();
            _fps = GetComponent<FpsController>();
            _health = GetComponent<PlayerHealth>();
            // ×5 — это в 5 раз меньший тангенс половины угла обзора
            _zoomFov = ZoomedFov(Zoom);
            _scopeFov = ZoomedFov(Combat.SniperSpec.Zoom);
        }

        float ZoomedFov(float zoom) => 2f * Mathf.Atan(Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / zoom) * Mathf.Rad2Deg;

        void OnDisable()
        {
            if (_net != null)
            {
                _net.LocalBinoculars = false;
                _net.LocalSniperAim = false;
            }
            if (_fps != null)
                _fps.LookSensitivityScale = 1f;
            if (cam != null)
                cam.fieldOfView = baseFov;
        }

        void Update()
        {
            if (_weapon == null)
                _weapon = GetComponent<PistolView>();
            bool sniper = _weapon != null && _weapon.HoldingSniper;
            bool pressed = GameInput.IsPressed(GameAction.Aim);
            // кончились патроны снайперки с оптикой у глаз — опускаем, бинокль только по новому нажатию
            if (IsUp && IsScope && !sniper)
                _waitRelease = true;
            if (!pressed)
                _waitRelease = false;
            bool want = pressed && !_waitRelease && !_health.IsDowned && !UiRoot.CursorFree;
            if (want && !IsUp)
                IsScope = sniper; // что поднимаем — решается в момент нажатия
            if (want != IsUp)
            {
                IsUp = want;
                Sfx.PlayAt(want ? Sfx.BinocularOn : Sfx.BinocularOff, transform.position + Vector3.up * 1.7f, 0.6f, 10f);
            }
            _net.LocalBinoculars = IsUp && !IsScope;
            _net.LocalSniperAim = IsUp && IsScope;

            Amount = Mathf.MoveTowards(Amount, IsUp ? 1f : 0f, Time.deltaTime / RaiseTime);
            float k = Amount * Amount * (3f - 2f * Amount); // плавный разгон и торможение
            if (cam != null)
                cam.fieldOfView = Mathf.Lerp(baseFov, IsScope ? _scopeFov : _zoomFov, k);
            _fps.LookSensitivityScale = Mathf.Lerp(1f, 1f / (IsScope ? Combat.SniperSpec.Zoom : Zoom), k);

            Distance = -1f;
            if (Amount > 0.5f && cam != null &&
                Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, MaxRange, ~0, QueryTriggerInteraction.Ignore))
                Distance = hit.distance;
        }
    }
}
