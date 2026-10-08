using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Net;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Главное меню — одно окно: имя, создать комнату (LAN / Internet), поиск комнаты (LAN + Internet, вход по IP или коду),
    /// громкость, выход.
    /// </summary>
    public class MainMenuView
    {
        const float ListRefreshInterval = 1f;

        readonly VisualElement _screen;
        readonly TextField _name, _ip, _code;
        readonly ScrollView _list;
        readonly Label _status;
        readonly Toggle _bots;
        readonly List<Button> _actions = new List<Button>();
        IList<ISessionInfo> _sessions;
        bool? _visible; // null — ещё ни разу не показывали/не прятали
        bool _busy;
        float _nextListRefresh;
        string _listSignature;

        static NetworkBootstrap Net => NetworkBootstrap.Instance;
        static LanDiscovery Lan => Net != null ? Net.GetComponent<LanDiscovery>() : null;

        public MainMenuView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "menu-screen");
            var panel = Ui.El(_screen, "panel", "menu-panel");
            Ui.Text(panel, "ROCKET WARS", "title", "shadow");

            var nameRow = Ui.El(panel, "row");
            Ui.Text(nameRow, "Имя", "label-dim");
            _name = Ui.Field(nameRow, Net != null ? Net.PlayerName : "Player", 24, "grow");
            _name.RegisterValueChangedCallback(e => { if (Net != null) Net.PlayerName = e.newValue; });

            Ui.Text(panel, "СОЗДАТЬ КОМНАТУ", "section");
            var createRow = Ui.El(panel, "row");
            _actions.Add(Ui.Btn(createRow, "В локальной сети", CreateLan, "btn-primary", "grow"));
            _actions.Add(Ui.Btn(createRow, "В интернете", CreateInternet, "btn-primary", "grow"));
            _bots = new Toggle(" Тестовые боты") { value = MatchNet.SpawnTestBots };
            _bots.AddToClassList("label-dim");
            _bots.RegisterValueChangedCallback(e => MatchNet.SpawnTestBots = e.newValue);
            panel.Add(_bots);
            Ui.Show(_bots, Debug.isDebugBuild); // тестовые боты — только для разработки

            Ui.Text(panel, "ПОИСК КОМНАТЫ", "section");
            _list = new ScrollView();
            _list.AddToClassList("list");
            panel.Add(_list);
            var refreshRow = Ui.El(panel, "row");
            _actions.Add(Ui.Btn(refreshRow, "Обновить интернет-комнаты", RefreshSessions, "btn-small"));

            var ipRow = Ui.El(panel, "row");
            Ui.Text(ipRow, "IP", "label-dim");
            _ip = Ui.Field(ipRow, "127.0.0.1", 64, "grow");
            _actions.Add(Ui.Btn(ipRow, "Подключиться", JoinTypedAddress, "btn-small"));

            var codeRow = Ui.El(panel, "row");
            Ui.Text(codeRow, "Код", "label-dim");
            _code = Ui.Field(codeRow, "", 12, "grow");
            _actions.Add(Ui.Btn(codeRow, "Войти по коду", () => JoinCode(_code.value), "btn-small"));

            Ui.Text(panel, "ГРОМКОСТЬ", "section");
            var volume = new Slider(0f, 1f) { value = UiRoot.Volume };
            volume.RegisterValueChangedCallback(e => UiRoot.Volume = e.newValue);
            panel.Add(volume);

            _status = Ui.Text(panel, NetworkBootstrap.LastMessage ?? "", "status");
            Ui.Btn(panel, "Выйти из игры", Quit, "btn-danger");
        }

        public void SetVisible(bool visible)
        {
            if (visible == _visible)
                return;
            _visible = visible;
            Ui.Show(_screen, visible);
            if (visible)
            {
                Lan?.StartListening();
                _status.text = NetworkBootstrap.LastMessage ?? "";
                if (Net != null)
                    _name.SetValueWithoutNotify(Net.PlayerName);
                var cam = LocalPlayerRig.GetCamera();
                LocalPlayerRig.ShowOverview(cam);
            }
        }

        public void Update()
        {
            foreach (var b in _actions)
                b.SetEnabled(!_busy);

            if (Time.unscaledTime < _nextListRefresh)
                return;
            _nextListRefresh = Time.unscaledTime + ListRefreshInterval;
            RebuildList();
        }

        void RebuildList()
        {
            var lan = Lan;
            var sig = new System.Text.StringBuilder();
            if (lan != null)
                foreach (var h in lan.Hosts)
                    sig.Append(h.Address).Append(h.Port).Append(h.Players).Append(h.Name);
            if (_sessions != null)
                foreach (var s in _sessions)
                    sig.Append(s.Id).Append(s.AvailableSlots);
            if (sig.ToString() == _listSignature)
                return;
            _listSignature = sig.ToString();

            _list.Clear();
            _actions.RemoveAll(b => b.panel == null);
            if (lan != null)
                foreach (var h in lan.Hosts)
                {
                    var host = h;
                    AddListItem($"LAN · {host.Name} · {host.Address} · игроков: {host.Players}", () => JoinLan(host.Address, host.Port));
                }
            if (_sessions != null)
                foreach (var s in _sessions)
                {
                    var id = s.Id;
                    AddListItem($"Интернет · {s.Name} · {s.MaxPlayers - s.AvailableSlots}/{s.MaxPlayers}", () => JoinSession(id));
                }

            if (_list.childCount == 0)
                Ui.Text(_list, lan != null && lan.IsListening ? "  Ищем комнаты в сети…" : $"  Поиск в сети недоступен: {lan?.Error}", "label-dim");
        }

        void AddListItem(string text, System.Action join)
        {
            var item = Ui.El(_list, "list-item");
            Ui.Text(item, text);
            _actions.Add(Ui.Btn(item, "Войти", join, "btn-small", "btn-primary"));
        }

        // ---------- действия ----------

        void CreateLan()
        {
            Lan?.StopListening();
            if (!Net.StartLanHost())
                Fail(NetworkBootstrap.LastMessage);
        }

        /// <summary>Поле IP: «адрес» (порт по умолчанию) или «адрес:порт».</summary>
        void JoinTypedAddress()
        {
            string address = _ip.value.Trim();
            ushort port = Net.port;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon && ushort.TryParse(address.Substring(colon + 1), out var typedPort))
            {
                port = typedPort;
                address = address.Substring(0, colon);
            }
            JoinLan(address, port);
        }

        void JoinLan(string address, ushort port)
        {
            if (string.IsNullOrEmpty(address))
                return;
            Lan?.StopListening();
            _status.text = $"Подключаемся к {address}…";
            if (!Net.JoinLan(address, port))
                Fail(NetworkBootstrap.LastMessage);
        }

        void CreateInternet()
        {
            SetBusy("Создаём комнату…");
            Net.CreateInternetSession(OnAsyncDone);
        }

        void JoinCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            SetBusy("Подключаемся…");
            Net.JoinInternetSession(code, true, OnAsyncDone);
        }

        void JoinSession(string id)
        {
            SetBusy("Подключаемся…");
            Net.JoinInternetSession(id, false, OnAsyncDone);
        }

        async void RefreshSessions()
        {
            SetBusy("Загружаем список комнат…");
            try
            {
                _sessions = await SessionService.QueryAsync();
                _status.text = _sessions.Count == 0 ? "Интернет-комнат нет" : "";
            }
            catch (System.Exception e)
            {
                _status.text = SessionService.Describe(e);
            }
            _busy = false;
            _nextListRefresh = 0f;
        }

        void SetBusy(string message)
        {
            _busy = true;
            _status.text = message;
        }

        void OnAsyncDone(string error)
        {
            _busy = false;
            _status.text = error ?? "";
            if (error == null)
                Lan?.StopListening();
        }

        void Fail(string message)
        {
            _status.text = message;
            Lan?.StartListening();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
