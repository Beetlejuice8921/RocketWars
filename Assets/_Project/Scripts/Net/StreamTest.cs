using System.IO;
using System.Linq;
using System.Text;
using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Player;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Net
{
    /// <summary>
    /// Проверка стрим-комнаты в dev-сборках (-streamtest). Хост (-streamhost [-slots N] [-expect N]) создаёт
    /// интернет-стрим-комнату, ждёт зрителей, снимает экран с QR-кодом, проводит короткий матч (кладёт команду
    /// зелёных), после матча запускает новый (смена игроков), потом открывает окно «Управление стримом» и банит последнего в очереди. Каждые 2 с все
    /// пишут в лог состояние: кто в игре, очередь, своё место. -shot &lt;папка&gt; — куда сохранять снимки экрана.
    /// </summary>
    public class StreamTest : MonoBehaviour
    {
        const string Tag = "[STREAMTEST]";
        const float LogInterval = 2f;
        const float KnockAfter = 6f;

        public int expectClients = 3;
        public string shotDir;
        public float timeout = 300f;

        float _nextLog, _phaseStart, _waitStart = -1f;
        MatchPhase _lastPhase = (MatchPhase)255;
        int _lastRound = -1, _step;
        bool _knocked;

        static void Log(string message) => Debug.Log($"{Tag} {message}");

        void Start() => Log($"started, host {StreamHost.Enabled}, expect {expectClients}");

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now > timeout)
            {
                Log("TIMEOUT");
                Application.Quit();
                return;
            }

            var net = MatchNet.Instance;
            var match = MatchManager.Instance;
            if (net == null || !net.IsSpawned || match == null || !match.IsSpawned)
                return;

            if (now >= _nextLog)
            {
                _nextLog = now + LogInterval;
                LogState(net, match);
            }

            var s = match.State;
            if (s.Phase != _lastPhase || s.Round != _lastRound)
            {
                _lastPhase = s.Phase;
                _lastRound = s.Round;
                _phaseStart = now;
                _knocked = false;
                Log($"phase {s.Phase} round {s.Round} score {s.Score0}:{s.Score1} winsTarget {match.WinsTarget}");
            }

            if (!net.IsServer)
            {
                // клиент: снимок экрана, пока ждёт в очереди
                if (_step == 0 && NetworkPlayer.LocalPlayer == null && net.QueuePosition(net.NetworkManager.LocalClientId) > 0 && now > 8f)
                {
                    _step = 1;
                    Shot("client_waiting");
                }
                return;
            }

            HostScript(net, match, s, now);
        }

        void HostScript(MatchNet net, MatchManager match, MatchState s, float now)
        {
            int clients = NetworkManager.Singleton.ConnectedClientsIds.Count - 1;
            switch (_step)
            {
                case 0 when clients >= expectClients:
                    _step = 1;
                    _waitStart = now;
                    break;
                case 1 when now - _waitStart > 4f:
                    Shot("host_lobby_qr");
                    Log("host: starting first match");
                    match.RequestStartMatch();
                    _step = 2;
                    break;
                case 2 when s.Phase == MatchPhase.MatchEnd && now - _phaseStart > 6f:
                    Shot("host_match_end");
                    Log("host: starting second match (rotation)");
                    match.RequestStartMatch();
                    _step = 3;
                    _waitStart = now;
                    break;
                case 3 when now - _waitStart > 5f:
                    LogState(net, match);
                    Shot("host_after_rotation");
                    UI.UiRoot.Instance?.OpenStreamPanel();
                    _step = 4;
                    _waitStart = now;
                    break;
                case 4 when now - _waitStart > 2f:
                    Shot("host_panel");
                    if (net.Queue.Count > 0)
                    {
                        var victim = net.Queue[net.Queue.Count - 1];
                        Log($"host: banning {victim.Name} (client {victim.ClientId})");
                        net.ServerKick(victim.ClientId, true);
                    }
                    _step = 5;
                    _waitStart = now;
                    break;
                case 5 when now - _waitStart > 3f:
                    Shot("host_panel_after_ban");
                    _step = 6;
                    _waitStart = now;
                    break;
                case 6 when now - _waitStart > 35f:
                    LogState(net, match);
                    Log("DONE");
                    Application.Quit();
                    break;
            }

            // в раундах — кладём зелёных, чтобы матч до 2 побед закончился быстро
            if (s.Phase == MatchPhase.Round && !_knocked && now - _phaseStart > KnockAfter)
            {
                _knocked = true;
                foreach (var p in PlayerHealth.All.ToArray())
                    if (p.IsSpawned && !p.IsDowned && p.Team == 1)
                        p.TakeDamage(new DamageInfo(1000f, p.transform.position, null, -1, DamageKind.Explosion));
                Log("host: knocked down team 1");
            }
        }

        static void LogState(MatchNet net, MatchManager match)
        {
            var sb = new StringBuilder();
            sb.Append($"state stream {net.IsStream} slots {net.Stream.Slots} wins {net.Stream.WinsNeeded} locked {net.Stream.Locked} phase {match.Phase} | in game:");
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned)
                    sb.Append($" {p.DisplayName}(t{p.Team}{(p.GetComponent<NetworkPlayer>().IsBot ? ",bot" : "")})");
            sb.Append(" | queue:");
            for (int i = 0; i < net.Queue.Count; i++)
                sb.Append($" {i + 1}.{net.Queue[i].Name}");
            if (!net.IsServer)
                sb.Append($" | me: {(NetworkPlayer.LocalPlayer != null ? "playing" : "waiting #" + net.QueuePosition(net.NetworkManager.LocalClientId))}");
            Log(sb.ToString());
        }

        void Shot(string name)
        {
            if (string.IsNullOrEmpty(shotDir))
                return;
            string path = Path.Combine(shotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log($"screenshot {path}");
        }
    }
}
