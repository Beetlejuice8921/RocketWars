using System;
using System.Collections.Generic;
using RocketWars.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RocketWars.Net
{
    /// <summary>
    /// Запуск сети: хост/клиент по LAN (IP) или через Unity Sessions (Internet), одобрение подключений,
    /// имена игроков, обработка разрыва (выход хоста → клиенты возвращаются в меню).
    /// Аргументы командной строки: -host, -join &lt;ip&gt;, -joincode &lt;код&gt;, -name &lt;имя&gt;, -bots.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public class NetworkBootstrap : MonoBehaviour
    {
        public const int MaxPlayers = 16;
        public const ushort DefaultPort = 7777;
        /// <summary>Если порт занят (второй экземпляр игры, «зависший» сокет редактора) — пробуем следующие.</summary>
        const int HostPortAttempts = 10;
        public const int DisconnectTimeoutMs = 10000;

        public static NetworkBootstrap Instance { get; private set; }
        /// <summary>Причина последнего возврата в меню (переживает перезагрузку сцены).</summary>
        public static string LastMessage { get; set; }

        public GameObject playerPrefab;
        public ushort port = DefaultPort;
        /// <summary>Порт, на котором реально поднята LAN-комната (может отличаться от port, если тот был занят).</summary>
        public ushort HostPort { get; private set; }
        bool _probingPorts;

        /// <summary>Случайное имя по умолчанию — одно на запуск (иначе в меню, в названии комнаты и в игре выходили разные).</summary>
        static string _randomName;

        static readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
        static bool _commandLineHandled;

        public NetworkManager Manager { get; private set; }
        public bool IsRunning => Manager != null && Manager.IsListening;

        public string PlayerName
        {
            // своё имя из меню, иначе — из аккаунта площадки (Яндекс, VK)
            get => PlayerPrefs.GetString("playerName", GamePlatform.Current.AccountName ?? (_randomName ??= $"Player{UnityEngine.Random.Range(100, 999)}"));
            set => PlayerPrefs.SetString("playerName", string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim());
        }

        UnityTransport _transport;
        bool _leaving;

        void Awake()
        {
            Instance = this;
            Manager = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();
            Manager.NetworkConfig.NetworkTransport = _transport;
            // клиент без ответа (вылет, выдернули кабель) считается отключённым через 10 с, а не через 30
            _transport.DisconnectTimeoutMS = DisconnectTimeoutMs;
            // браузер умеет только WebSocket (Relay по wss)
            _transport.UseWebSockets = GamePlatform.Current.IsWeb;
            Manager.NetworkConfig.ConnectionApproval = true;
            Manager.NetworkConfig.PlayerPrefab = null;
            // префаб игрока регистрируется через DefaultNetworkPrefabs (NGO собирает список автоматически)
        }

        void Start()
        {
            Manager.ConnectionApprovalCallback = Approve;
            Manager.OnClientDisconnectCallback += OnClientDisconnect;
            Manager.OnTransportFailure += OnTransportFailure;
            HandleCommandLine();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (Manager != null)
            {
                Manager.OnClientDisconnectCallback -= OnClientDisconnect;
                Manager.OnTransportFailure -= OnTransportFailure;
            }
        }

        public static string GetPlayerName(ulong clientId) =>
            Names.TryGetValue(clientId, out var name) ? name : $"Player{clientId}";

        void SetPayload() => Manager.NetworkConfig.ConnectionData = NetProtocol.BuildPayload(PlayerName);

        // ---------- LAN ----------

        public bool StartLanHost()
        {
            SetPayload();
            // при неудаче StartHost сам всё освобождает, так что можно сразу пробовать следующий порт;
            // клиенты узнают реальный порт из LAN-рассылки (или вводят «IP:порт»)
            for (int i = 0; i < HostPortAttempts; i++)
            {
                ushort p = (ushort)(port + i);
                _transport.SetConnectionData("127.0.0.1", p, "0.0.0.0");
                _probingPorts = true; // занятый порт — не «сетевая ошибка», а повод попробовать следующий
                bool started;
                try { started = Manager.StartHost(); }
                finally { _probingPorts = false; }
                if (!started)
                    continue;
                HostPort = p;
                if (p != port)
                    Debug.LogWarning($"[RocketWars] port {port} is busy, LAN room is on port {p}");
                if (TryGetComponent(out LanDiscovery lan))
                    lan.StartBroadcast(p);
                return true;
            }
            return Fail(Loc.F("Не удалось создать комнату: порты {0}–{1} заняты", port, port + HostPortAttempts - 1));
        }

        public bool JoinLan(string address, ushort hostPort)
        {
            SetPayload();
            _transport.SetConnectionData(address, hostPort);
            return Manager.StartClient() || Fail(Loc.T("Не удалось начать подключение"));
        }

        // ---------- Internet (Unity Sessions) ----------

        public async void CreateInternetSession(Action<string> onDone)
        {
            try
            {
                SetPayload();
                var session = await SessionService.CreateAsync($"{PlayerName}'s room", MaxPlayers);
                onDone?.Invoke(null);
                Debug.Log($"[RocketWars] Session created, code {session.Code}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                onDone?.Invoke(SessionService.Describe(e));
            }
        }

        public async void JoinInternetSession(string idOrCode, bool isCode, Action<string> onDone)
        {
            try
            {
                SetPayload();
                if (isCode) await SessionService.JoinByCodeAsync(idOrCode);
                else await SessionService.JoinByIdAsync(idOrCode);
                onDone?.Invoke(null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                onDone?.Invoke(SessionService.Describe(e));
            }
        }

        // ---------- подключения ----------

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            NetProtocol.ParsePayload(request.Payload, out int version, out string name);
            // свой (локальный) клиент хоста — всегда той же версии
            if (request.ClientNetworkId != NetworkManager.ServerClientId && version != NetProtocol.Version)
            {
                Debug.LogWarning($"[RocketWars] client {request.ClientNetworkId} rejected: protocol {version}, ours {NetProtocol.Version}");
                response.Approved = false;
                response.Reason = NetProtocol.MismatchReason;
                return;
            }

            response.Approved = Manager.ConnectedClientsIds.Count < MaxPlayers;
            if (!response.Approved)
            {
                response.Reason = "Комната заполнена";
                return;
            }

            if (string.IsNullOrEmpty(name))
                name = $"Player{request.ClientNetworkId}";
            Names[request.ClientNetworkId] = name.Length > 24 ? name.Substring(0, 24) : name;
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (Manager.IsServer)
            {
                Names.Remove(clientId);
                return;
            }

            // мы — клиент, и отключили нас (или хост ушёл)
            // своя причина (отказ при одобрении) приходит русским ключом Loc и переводится здесь; служебный текст NGO не показываем
            string reason = Manager.DisconnectReason;
            bool ours = !string.IsNullOrEmpty(reason) && !reason.StartsWith("[");
            ReturnToMenu(ours ? Loc.T(reason) : Loc.T("Хост закрыл комнату или соединение потеряно"));
        }

        void OnTransportFailure()
        {
            if (!_probingPorts)
                ReturnToMenu(Loc.T("Сетевая ошибка"));
        }

        /// <summary>Выйти из комнаты и вернуться в меню (полная перезагрузка сцены).</summary>
        public async void ReturnToMenu(string message = null)
        {
            if (_leaving)
                return;
            _leaving = true;
            LastMessage = message;

            try
            {
                await SessionService.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RocketWars] Session leave failed: {e.Message}");
            }

            if (Manager != null && Manager.IsListening)
                Manager.Shutdown();
            Names.Clear();

            // NetworkManager живёт в DontDestroyOnLoad — уничтожаем, чтобы сцена создала свежий
            var scene = SceneManager.GetActiveScene().name;
            Destroy(gameObject);
            SceneManager.LoadScene(scene);
        }

        bool Fail(string message)
        {
            LastMessage = message;
            return false;
        }

        void HandleCommandLine()
        {
            if (_commandLineHandled)
                return;
            _commandLineHandled = true;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-name" when i + 1 < args.Length:
                        PlayerName = args[++i];
                        break;
                    case "-port" when i + 1 < args.Length && ushort.TryParse(args[i + 1], out var p):
                        port = p;
                        i++;
                        break;
                    // отладочные режимы — только в dev-сборках и редакторе
                    case "-bots" when Debug.isDebugBuild:
                        MatchNet.SpawnTestBots = true;
                        break;
                    case "-autotest" when Debug.isDebugBuild:
                        var test = gameObject.AddComponent<AutoTest>();
                        int e = Array.IndexOf(args, "-expect");
                        if (e >= 0 && e + 1 < args.Length && int.TryParse(args[e + 1], out var expect))
                            test.expectClients = expect;
                        break;
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-host")
                    StartLanHost();
                else if (args[i] == "-join" && i + 1 < args.Length)
                    JoinLan(args[i + 1], port);
                else if (args[i] == "-joincode" && i + 1 < args.Length)
                    JoinInternetSession(args[i + 1], true, err => Debug.Log($"[RocketWars] join by code: {err ?? "OK"}"));
            }
        }
    }
}
