using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RocketWars.Core;
using RocketWars.Dev;
using RocketWars.Launcher;
using RocketWars.Net;
using RocketWars.Player;
using RocketWars.Terrain;
using RocketWars.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.EditorTools.Trailer
{
    /// <summary>
    /// Постановка кадров ролика в Play mode (вызывается через UnityMCP execute_code): хост, боты N×N,
    /// запуски ракет, езда хоста на ракете, интерфейс. Только для редактора.
    /// </summary>
    public static class TrailerKit
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static MatchNet Net => MatchNet.Instance;
        public static NetworkPlayer Me => NetworkPlayer.LocalPlayer;

        /// <summary>Комната на этом ПК без балансира ботов (ботов ставим сами).</summary>
        public static string Host()
        {
            MatchNet.SpawnTestBots = false;
            bool ok = NetworkBootstrap.Instance.StartLanHost();
            // флаг тестовых ботов выключает балансир; сами тестовые боты спавнятся только при старте сети — он уже прошёл
            MatchNet.SpawnTestBots = true;
            PlayerVisual.ShowCigar = false;
            return ok ? "host ok" : "host failed: " + NetworkBootstrap.LastMessage;
        }

        /// <summary>Добить обе команды ботами до perTeam игроков.</summary>
        public static string Bots(int perTeam)
        {
            var create = typeof(MatchNet).GetMethod("CreatePlayer", Any);
            int added = 0;
            for (int team = 0; team < 2; team++)
            {
                int have = PlayerHealth.All.Count(p => p.Team == team);
                for (int i = have; i < perTeam; i++)
                {
                    var bot = (PlayerHealth)create.Invoke(Net, new object[] { team, $"Бот {team * 10 + i + 1}", true });
                    bot.gameObject.AddComponent<BotBrain>();
                    bot.NetworkObject.Spawn(true);
                    added++;
                }
            }
            return $"bots +{added}, players {PlayerHealth.All.Count(p => p.Team == 0)}:{PlayerHealth.All.Count(p => p.Team == 1)}";
        }

        public static void StartMatch() => MatchManager.Instance.RequestStartMatch();

        /// <summary>Новый раунд на новых островах (биом меняется по кругу: луга → пустыня → снег).</summary>
        public static string NextRound()
        {
            MatchManager.Instance.DebugRestartRound();
            return "biome " + Biomes.Current;
        }

        public static List<RocketSilo> Silos(int team)
        {
            var list = new List<RocketSilo>();
            for (int i = 0; ; i++)
            {
                var s = Net.GetSilo(i);
                if (s == null) break;
                if (s.Team == team && s.State != SiloState.Destroyed) list.Add(s);
            }
            return list;
        }

        public static VoxelIslandRef Island(int team) => new VoxelIslandRef(Net.Islands[team]);

        /// <summary>Все уцелевшие шахты — снова заряжены (обходим 60 с перезарядки).</summary>
        public static void Reload()
        {
            var set = typeof(RocketSilo).GetMethod("SetState", Any);
            for (int t = 0; t < 2; t++)
                foreach (var s in Silos(t))
                    if (s.State == SiloState.Reloading)
                        set.Invoke(s, new object[] { SiloState.Ready });
        }

        /// <summary>Запуск из шахты по точке (сирена 2.5 с, затем полёт).</summary>
        public static bool Launch(RocketSilo silo, Vector3 target) => silo != null && silo.ServerLaunch(target, true, null, silo.Team);

        /// <summary>Залп: из count шахт команды по случайным точкам острова врага.</summary>
        public static int Volley(int team, int count)
        {
            Reload();
            int n = 0;
            foreach (var s in Silos(team).OrderBy(_ => Random.value).Take(count))
                if (Launch(s, Net.Islands[1 - team].GetRandomSpawnPoint(0f)))
                    n++;
            return n;
        }

        /// <summary>Хост встаёт у шахты и хватается за её ракету (держит «E» программно, пока не вызвать Unride).</summary>
        public static string Ride(RocketSilo silo)
        {
            var me = Me;
            var rider = me.GetComponent<RocketRider>();
            me.ServerTeleport(silo.transform.position + silo.transform.right * 1.2f + Vector3.up * 0.5f, Quaternion.LookRotation(silo.transform.position - me.transform.position));
            typeof(RocketRider).GetMethod("Grab", Any).Invoke(rider, new object[] { silo.NetIndex, null });
            GameInput.Simulate(GameAction.Use, true);
            return "riding silo " + silo.NetIndex;
        }

        public static void Unride() => GameInput.Simulate(GameAction.Use, false);

        public static void Hold(GameAction action, bool held) => GameInput.Simulate(action, held);

        public static void ReleaseAll() => GameInput.ClearSimulated();

        /// <summary>Спрятать / показать весь интерфейс (для «киношных» планов).</summary>
        public static void Hud(bool visible)
        {
            var root = UiRoot.Instance.GetComponent<UIDocument>().rootVisualElement;
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Повернуть взгляд хоста: yaw — поворот тела, pitch — наклон головы (вниз +).</summary>
        public static void Look(float yaw, float pitch)
        {
            var me = Me;
            me.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            me.GetComponent<FpsController>().SetPitch(pitch);
        }

        /// <summary>Нажать кнопку пульта шахты от имени хоста (как клик ЛКМ).</summary>
        public static void Press(RocketSilo silo, ConsoleButtonKind kind) => silo.Console.ServerPress(kind, Me, NetClock.Now);

        /// <summary>Дубль: запись сразу, остановка через seconds (таймлайн режиссёра задаётся отдельно).</summary>
        public static void Shot(string name, float seconds)
        {
            TrailerCapture.Begin(name, 30f);
            TrailerDirector.At(TrailerDirector.Now + seconds, TrailerCapture.End);
        }

        /// <summary>Плавно перевести взгляд хоста с точки a на точку b за отрезок [start, start+duration].</summary>
        public static void Pan(float start, float duration, Vector3 a, Vector3 b) =>
            TrailerDirector.Span(start, duration, u => LookAt(Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, u))));

        public static void LookAt(Vector3 point)
        {
            var me = Me;
            var fps = me.GetComponent<FpsController>();
            Vector3 eye = fps.CameraRoot.position;
            Vector3 d = point - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            Look(yaw, pitch);
        }
    }

    /// <summary>Остров для съёмки: центр, точки на поверхности.</summary>
    public readonly struct VoxelIslandRef
    {
        readonly VoxelIsland _island;
        public VoxelIslandRef(VoxelIsland island) => _island = island;
        public Vector3 Center => _island.Center;
        public Vector3 RandomPoint => _island.GetRandomSpawnPoint(0f);
    }
}
