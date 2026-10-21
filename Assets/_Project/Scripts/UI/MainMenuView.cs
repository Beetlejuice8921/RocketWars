using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Net;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Главное меню: в центре — имя, создать комнату (LAN / Internet), поиск комнаты (LAN + Internet, вход по IP или коду);
    /// всё — колонкой у левого края; под панелью — «Опции» (язык, громкость, мышь, клавиши) и «Выйти из игры».
    /// </summary>
    public class MainMenuView
    {
        const float ListRefreshInterval = 1f;

        /// <summary>Длина кода комнаты (код сессии Unity Multiplayer — 6 символов).</summary>
        const int CodeLength = 6;

        readonly VisualElement _screen;
        readonly TextField _name, _ip, _code;
        readonly ScrollView _list;
        readonly Label _status;
        readonly Button _joinCode;
        Label[] _codeCells;
        bool _codeFocused, _codeError, _joiningByCode;
        readonly List<Button> _actions = new List<Button>();
        IList<ISessionInfo> _sessions;
        bool? _visible; // null — ещё ни разу не показывали/не прятали
        bool _busy;
        float _nextListRefresh;
        string _listSignature;
        static bool _readyReported;

        static NetworkBootstrap Net => NetworkBootstrap.Instance;
        static LanDiscovery Lan => Net != null && GamePlatform.Current.SupportsLan ? Net.GetComponent<LanDiscovery>() : null;

        public MainMenuView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "menu-screen");
            // название — крупно, по центру вверху экрана
            var title = Ui.Text(_screen, "ROCKET WARS", "menu-title", "shadow");

            // колонка у левого края: панель, под ней — «Опции» / «Поддержка» / «Выйти из игры» друг под другом
            var column = Ui.El(_screen, "menu-column");
            var panel = Ui.El(column, "panel", "menu-panel");
            Ui.LangFont(title, Lang.En); // название игры — всегда Press Start 2P

            var side = Ui.El(column, "menu-side");
            Ui.Btn(side, Loc.T("Рейтинг"), () => UiRoot.Instance?.OpenLeaderboard(), "btn-primary", "menu-side-btn");
            Ui.Btn(side, Loc.T("Опции"), () => UiRoot.Instance?.OpenOptions(), "menu-side-btn");
            if (GamePlatform.Current.SupportUrl != null)
                Ui.Btn(side, Loc.T("Поддержка"), () => GamePlatform.Current.OpenSupport(), "menu-side-btn");
            if (GamePlatform.Current.CanQuit)
                Ui.Btn(side, Loc.T("Выйти из игры"), () => GamePlatform.Current.Quit(), "btn-danger", "menu-side-btn");

            var nameRow = Ui.El(panel, "row");
            Ui.Text(nameRow, Loc.T("Имя"), "label-dim");
            _name = Ui.Field(nameRow, Net != null ? Net.PlayerName : "Player", 24, "grow");
            _name.RegisterValueChangedCallback(e => { if (Net != null) Net.PlayerName = e.newValue; });
            Ui.Btn(nameRow, Loc.T("Скин"), () => UiRoot.Instance?.OpenSkins(), "btn-small", "menu-skin-btn");

            // с LAN — заголовок и две кнопки (сеть / интернет); без LAN (браузер) — одна кнопка «Создать комнату»
            bool lan = GamePlatform.Current.SupportsLan;
            if (lan)
                Ui.Text(panel, Loc.T("СОЗДАТЬ КОМНАТУ"), "section");
            var createRow = Ui.El(panel, "row");
            if (lan)
                _actions.Add(Ui.Btn(createRow, Loc.T("В локальной сети"), CreateLan, "btn-primary", "grow"));
            _actions.Add(Ui.Btn(createRow, Loc.T(lan ? "В интернете" : "Создать комнату"), CreateInternet, "btn-primary", "grow"));

            Ui.Text(panel, Loc.T("ПОИСК КОМНАТЫ"), "section");
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("list");
            panel.Add(_list);
            var refreshRow = Ui.El(panel, "row", "row-center");
            _actions.Add(Ui.Btn(refreshRow, Loc.T("Обновить интернет-комнаты"), RefreshSessions, "btn-small"));

            var ipRow = Ui.El(panel, "row");
            Ui.Text(ipRow, "IP", "label-dim");
            _ip = Ui.Field(ipRow, "127.0.0.1", 64, "grow");
            _actions.Add(Ui.Btn(ipRow, Loc.T("Подключиться"), JoinTypedAddress, "btn-small"));
            Ui.Show(ipRow, GamePlatform.Current.SupportsLan);

            // клетки — точно по центру панели: подпись и кнопка в равных боковых колонках, прижаты к клеткам
            var codeRow = Ui.El(panel, "row", "code-row");
            Ui.Text(Ui.El(codeRow, "code-side", "code-side-left"), Loc.T("Код"), "label-dim");
            _code = BuildCodeInput(codeRow);
            _joinCode = Ui.Btn(Ui.El(codeRow, "code-side"), Loc.T("Войти по коду"), () => JoinCode(_code.value), "btn-small");
            Ui.Show(codeRow, !GamePlatform.Current.CanInvite); // в Telegram в комнату заходят по ссылке-приглашению

            _status = Ui.Text(panel, NetworkBootstrap.LastMessage ?? "", "status");
        }

        public void SetVisible(bool visible)
        {
            if (visible == _visible)
                return;
            _visible = visible;
            Ui.Show(_screen, visible);
            if (visible)
            {
                if (!_readyReported)
                {
                    _readyReported = true;
                    GamePlatform.Current.GameReady();
                }
                Lan?.StartListening();
                _status.text = NetworkBootstrap.LastMessage ?? "";
                if (Net != null)
                    _name.SetValueWithoutNotify(Net.PlayerName);
                MenuDiorama.Show(LocalPlayerRig.GetCamera());
            }
            else
            {
                MenuDiorama.Hide();
            }
        }

        public void Update()
        {
            foreach (var b in _actions)
                b.SetEnabled(!_busy);
            _joinCode.SetEnabled(!_busy && _code.value.Length == CodeLength);

            // игру открыли по приглашению друга — сразу в его комнату (код виден в клетках, при ошибке они покраснеют)
            if (!_busy && Net != null && GamePlatform.Current.IsInitialized && GamePlatform.Current.TakeInviteCode() is string invite)
            {
                _code.value = invite;
                JoinCode(invite);
            }

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
                    AddListItem($"LAN · {host.Name} · {host.Address} · " + Loc.F("игроков: {0}", host.Players), () => JoinLan(host.Address, host.Port));
                }
            if (_sessions != null)
                foreach (var s in _sessions)
                {
                    var id = s.Id;
                    // без LAN все комнаты интернетные — приставка «Интернет ·» не нужна
                    string players = $"{s.MaxPlayers - s.AvailableSlots}/{s.MaxPlayers}";
                    AddListItem(lan != null ? $"{Loc.T("Интернет")} · {s.Name} · {players}" : $"{s.Name} · {players}", () => JoinSession(id));
                }

            if (_list.childCount == 0 && GamePlatform.Current.SupportsLan)
                Ui.Text(_list, lan != null && lan.IsListening ? "  " + Loc.T("Ищем комнаты в сети…") : "  " + Loc.F("Поиск в сети недоступен: {0}", lan?.Error), "label-dim");
        }

        void AddListItem(string text, System.Action join)
        {
            var item = Ui.El(_list, "list-item");
            Ui.Text(item, text);
            _actions.Add(Ui.Btn(item, Loc.T("Войти"), join, "btn-small", "btn-primary"));
        }

        // ---------- поле кода ----------

        /// <summary>
        /// Код комнаты — клетка на каждый символ. Ввод принимает невидимый TextField поверх клеток,
        /// поэтому клик, вставка из буфера и Enter работают как в обычном поле.
        /// </summary>
        TextField BuildCodeInput(VisualElement parent)
        {
            var box = Ui.El(parent, "code-box");
            var cells = Ui.El(box, "code-cells");
            _codeCells = new Label[CodeLength];
            for (int i = 0; i < CodeLength; i++)
                _codeCells[i] = Ui.Text(cells, "", "code-cell");

            // запас по длине: вставка « 77lwnq » с пробелами и дефисами не должна обрезаться до очистки
            var field = Ui.Field(cells, "", 64, "code-input");
            field.RegisterValueChangedCallback(e =>
            {
                string clean = CleanCode(e.newValue);
                if (clean.Length > CodeLength)
                {
                    // лишний символ при наборе в полное поле — игнорируем; вставили целый код поверх набранного — берём вставленный
                    string prev = CleanCode(e.previousValue);
                    clean = clean.Length - prev.Length > 1 ? clean.Substring(clean.Length - CodeLength) : prev;
                }
                if (clean != e.newValue)
                {
                    field.SetValueWithoutNotify(clean);
                    field.SelectRange(clean.Length, clean.Length);
                }
                _codeError = false;
                UpdateCodeCells();
            });
            field.RegisterCallback<FocusInEvent>(_ => { _codeFocused = true; UpdateCodeCells(); });
            field.RegisterCallback<FocusOutEvent>(_ => { _codeFocused = false; UpdateCodeCells(); });
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _joinCode.enabledSelf)
                    JoinCode(field.value);
            }, TrickleDown.TrickleDown);
            return field;
        }

        /// <summary>Только латинские буквы и цифры, заглавными (длину ограничивает вызывающий).</summary>
        static string CleanCode(string s)
        {
            var sb = new System.Text.StringBuilder(CodeLength);
            foreach (char c in s ?? "")
                if (c < 128 && char.IsLetterOrDigit(c))
                    sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        void UpdateCodeCells()
        {
            string v = _code.value ?? "";
            int active = Mathf.Min(v.Length, CodeLength - 1);
            for (int i = 0; i < CodeLength; i++)
            {
                var cell = _codeCells[i];
                cell.text = i < v.Length ? v[i].ToString() : "";
                cell.EnableInClassList("code-cell--active", _codeFocused && i == active);
                cell.EnableInClassList("code-cell--error", _codeError);
            }
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
            _status.text = Loc.F("Подключаемся к {0}…", address);
            if (!Net.JoinLan(address, port))
                Fail(NetworkBootstrap.LastMessage);
        }

        void CreateInternet()
        {
            _joiningByCode = false;
            SetBusy(Loc.T("Создаём комнату…"));
            Net.CreateInternetSession(OnAsyncDone);
        }

        void JoinCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            _joiningByCode = true;
            SetBusy(Loc.T("Подключаемся…"));
            Net.JoinInternetSession(code, true, OnAsyncDone);
        }

        void JoinSession(string id)
        {
            _joiningByCode = false;
            SetBusy(Loc.T("Подключаемся…"));
            Net.JoinInternetSession(id, false, OnAsyncDone);
        }

        async void RefreshSessions()
        {
            SetBusy(Loc.T("Загружаем список комнат…"));
            try
            {
                _sessions = await SessionService.QueryAsync();
                _status.text = _sessions.Count == 0 ? Loc.T("Интернет-комнат нет") : "";
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
            else if (_joiningByCode)
            {
                _codeError = true; // неверный код — клетки красные, пока код не поправят
                UpdateCodeCells();
            }
        }

        void Fail(string message)
        {
            _status.text = message;
            Lan?.StartListening();
        }
    }
}
