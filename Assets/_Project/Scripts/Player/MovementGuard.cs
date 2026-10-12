using RocketWars.Launcher;
using RocketWars.Net;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Сервер: присматривает за движением удалённого игрока (движение считает его клиент — NetworkTransform Owner).
    /// Раз в полсекунды сравнивает пройденное по горизонтали с пределом; спидхак или телепорт через подмену
    /// координат в памяти — игрока возвращают в последнюю честную точку. Порог вдвое выше спринта, чтобы
    /// рывки сети честных игроков не трогать. Исключения: висит на ракете, летит после неё по инерции,
    /// падает с острова (возврат наверх — законный «телепорт»), сервер сам его переставил.
    /// </summary>
    public class MovementGuard : MonoBehaviour
    {
        const float SampleInterval = 0.5f;
        /// <summary>Предел горизонтальной скорости, м/с: спринт ×2 с запасом на неровную доставку пакетов.</summary>
        const float MaxHorizontalSpeed = FpsController.SprintSpeed * 2f;
        /// <summary>Сколько секунд после схода с ракеты скорость не проверяем (инерция ракеты, парашют).</summary>
        const float RocketGrace = 20f;
        /// <summary>Ракета или шахта должны быть ближе этого, чтобы «вишу на ракете» было правдой.</summary>
        const float RideCheckRadius = 12f;
        /// <summary>Как и в FallRespawn: ниже дна острова на столько — игрок падает.</summary>
        const float BelowIslandMargin = 3f;

        NetworkPlayer _net;
        PlayerHealth _health;
        Vector3 _lastGood;
        float _nextSample;
        float _graceUntil;
        /// <summary>После возврата: ждём, пока клиент применит телепорт; последнюю честную точку не трогаем.</summary>
        float _correctionUntil;
        int _violations;

        void Awake()
        {
            _net = GetComponent<NetworkPlayer>();
            _health = GetComponent<PlayerHealth>();
            _lastGood = transform.position;
            Grace(3f); // первая позиция придёт от клиента чуть позже спавна
        }

        /// <summary>Сервер переставил игрока сам (новый раунд и т. п.) — новую точку принимаем без проверки.</summary>
        public void Grace(float seconds)
        {
            _graceUntil = Mathf.Max(_graceUntil, Time.time + seconds);
            _nextSample = Time.time + SampleInterval;
        }

        void Update()
        {
            if (Time.time < _nextSample || Time.time < _correctionUntil)
                return;
            _nextSample = Time.time + SampleInterval;

            Vector3 pos = transform.position;
            if (_net.IsRiding && RideIsReal(pos))
                _graceUntil = Time.time + RocketGrace;

            if (Time.time < _graceUntil || IsFalling(_lastGood) || IsFalling(pos))
            {
                _lastGood = pos;
                return;
            }

            Vector3 d = pos - _lastGood;
            d.y = 0f;
            if (d.magnitude <= MaxHorizontalSpeed * SampleInterval)
            {
                _lastGood = pos;
                return;
            }

            // слишком быстро — назад в последнюю честную точку
            _violations++;
            Debug.LogWarning($"[AntiCheat] {_health.DisplayName}: moved {d.magnitude:0.0} m in {SampleInterval} s, " +
                             $"pulled back (violation {_violations})");
            _net.TeleportRpc(_lastGood, transform.rotation);
            _correctionUntil = Time.time + 1f; // если клиент телепорт проигнорирует — после паузы вернём снова
        }

        /// <summary>Ниже дна своего острова — падает; возврат на остров через 3 с — честный телепорт.</summary>
        bool IsFalling(Vector3 pos)
        {
            var island = VoxelIsland.ForTeam(_health.Team);
            return island == null || pos.y < island.transform.position.y - BelowIslandMargin;
        }

        /// <summary>Флаг «вишу на ракете» пишет клиент — верим, только если рядом и правда ракета или её шахта.</summary>
        bool RideIsReal(Vector3 pos)
        {
            foreach (var rocket in Rocket.Active)
                if (rocket != null && (rocket.transform.position - pos).sqrMagnitude < RideCheckRadius * RideCheckRadius)
                    return true;
            var silo = MatchNet.Instance != null ? MatchNet.Instance.GetSilo(_net.RideSilo) : null;
            return silo != null && (silo.transform.position - pos).sqrMagnitude < RideCheckRadius * RideCheckRadius;
        }
    }
}
