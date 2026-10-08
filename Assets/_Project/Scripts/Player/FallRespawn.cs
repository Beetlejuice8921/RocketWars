using RocketWars.Core;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Падение с острова: как только игрок ушёл ниже дна своего острова, через 3 с он появляется сверху
    /// своего острова. Считает и переставляет владелец (у него движение), штраф −50 HP начисляет сервер.
    /// </summary>
    [RequireComponent(typeof(FpsController), typeof(PlayerHealth), typeof(NetworkPlayer))]
    public class FallRespawn : MonoBehaviour
    {
        public const float RespawnDelay = 3f;
        public float belowIslandMargin = 3f;
        public float spawnHeightAbove = 6f;

        public bool IsFalling { get; private set; }
        public float TimeLeft { get; private set; }

        FpsController _controller;
        PlayerHealth _health;
        NetworkPlayer _net;

        void Awake()
        {
            _controller = GetComponent<FpsController>();
            _health = GetComponent<PlayerHealth>();
            _net = GetComponent<NetworkPlayer>();
        }

        void Update()
        {
            if (!_net.IsSpawned || !_net.IsOwner)
                return;
            var island = VoxelIsland.ForTeam(_health.Team);
            if (island == null)
                return;

            if (!IsFalling)
            {
                if (transform.position.y < island.transform.position.y - belowIslandMargin)
                {
                    IsFalling = true;
                    TimeLeft = RespawnDelay;
                    if (_controller.IsLocal)
                        Sfx.Play2D(Sfx.FallWhistle, 0.8f);
                }
                return;
            }

            // игрока переставили на остров (новый раунд) — падение отменяется, штрафа нет
            if (transform.position.y >= island.transform.position.y - belowIslandMargin)
            {
                IsFalling = false;
                return;
            }

            TimeLeft -= Time.deltaTime;
            if (TimeLeft > 0f)
                return;

            IsFalling = false;
            _controller.Teleport(island.GetRandomSpawnPoint(spawnHeightAbove));
            _net.ReportFall();
        }
    }
}
