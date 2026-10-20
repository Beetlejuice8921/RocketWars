using RocketWars.Core;
using RocketWars.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Корень интерфейса (UI Toolkit): главное меню, HUD, пауза (Esc), таблица результатов (конец матча / Tab).
    /// Решает, свободен ли курсор: пока открыто меню, пауза или итоги — мышь управляет интерфейсом, а не камерой.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UiRoot : MonoBehaviour
    {
        public const string VolumeKey = "volume";

        /// <summary>Телефон: интерфейс крупнее (экран маленький, а нажимают пальцем).</summary>
        const float TouchUiScale = 1.4f;

        public static UiRoot Instance { get; private set; }
        /// <summary>Курсор нужен интерфейсу (FpsController его не захватывает).</summary>
        public static bool CursorFree { get; private set; } = true;

        public StyleSheet styleSheet;

        MainMenuView _menu;
        HudView _hud;
        PauseView _pause;
        ResultsView _results;
        OptionsView _options;
        LeaderboardView _leaderboard;
        TouchControls _touch;
        bool _pauseOpen;
        float _pauseOpenedAt;
        bool _resultsDismissed;
        bool _wasResultsModal, _playing;

        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        void Awake()
        {
            Instance = this;
            AudioListener.volume = Volume;
            _touch = gameObject.AddComponent<TouchControls>();
            if (TouchInput.Enabled)
            {
                // копия: в редакторе правка самого ассета пережила бы выход из Play
                var doc = GetComponent<UIDocument>();
                doc.panelSettings = Instantiate(doc.panelSettings);
                doc.panelSettings.scale *= TouchUiScale;
            }
        }

        void OnEnable()
        {
            Build();
            Loc.Changed += Build;
            GamePlatform.Initialized += Build; // имя игрока из аккаунта площадки
        }

        void OnDisable()
        {
            Loc.Changed -= Build;
            GamePlatform.Initialized -= Build;
        }

        /// <summary>Собрать интерфейс заново — при старте и при смене языка (все тексты и шрифт берутся заново).</summary>
        void Build()
        {
            bool optionsOpen = _options != null && _options.IsOpen; // язык меняют из опций — окно остаётся открытым
            bool leaderboardOpen = _leaderboard != null && _leaderboard.IsOpen;
            AudioListener.volume = Volume; // громкость могла прийти из облака площадки
            _hud?.Dispose();
            _results?.Dispose();

            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
                root.styleSheets.Add(styleSheet);
            root.AddToClassList("root");
            root.EnableInClassList("touch-mode", TouchInput.Enabled); // телефон: свои отступы (RocketWars.uss)
            root.style.unityFontDefinition = GameFonts.Ui;
            root.style.unityTextGenerator = GameFonts.GeneratorFor(Loc.Current);

            _hud = new HudView(root);
            _touch.Build(root);
            _results = new ResultsView(root);
            _pause = new PauseView(root, () => SetPause(false));
            _menu = new MainMenuView(root);
            _leaderboard = new LeaderboardView(root);
            _options = new OptionsView(root);
            if (optionsOpen)
                _options.Open();
            // dev-сборка: «-openLeaderboard» — сразу окно рейтинга (проверка без мыши)
            if (Debug.isDebugBuild && !_leaderboardArgDone && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-openLeaderboard") >= 0)
            {
                _leaderboardArgDone = true;
                leaderboardOpen = true;
            }
            if (leaderboardOpen)
                _leaderboard.Open();
        }

        static bool _leaderboardArgDone;

        public void OpenOptions() => _options.Open();

        public void OpenLeaderboard() => _leaderboard.Open();

        void OnDestroy()
        {
            _hud?.Dispose();
            _results?.Dispose();
            if (Instance == this)
                Instance = null;
        }

        public void SetPause(bool open)
        {
            if (open && !_pauseOpen)
                _pauseOpenedAt = Time.unscaledTime;
            _pauseOpen = open;
        }

        /// <summary>
        /// Браузер сам снял захват мыши (Esc в браузере) — открываем паузу; захват вернётся кликом по «Продолжить».
        /// </summary>
        public void OnPointerLockLost()
        {
            var net = NetworkBootstrap.Instance;
            if (net != null && net.IsRunning)
                SetPause(true);
        }

        void Update()
        {
            var net = NetworkBootstrap.Instance;
            bool inGame = net != null && net.IsRunning;
            var match = MatchManager.Instance;
            bool matchEnd = inGame && match != null && match.IsSpawned && match.Phase == MatchPhase.MatchEnd;
            if (!matchEnd)
                _resultsDismissed = false;

            HandleKeys(inGame, match);

            _menu.SetVisible(!inGame);
            if (inGame && _leaderboard.IsOpen)
                _leaderboard.Close(); // вошли в комнату по приглашению, пока смотрели рейтинг
            var kb = Keyboard.current;
            bool tabHeld = (kb != null && kb.tabKey.isPressed || TouchInput.ScoreHeld) && inGame;
            bool resultsModal = matchEnd && !_resultsDismissed;

            // на экране итогов HUD (здоровье, счёт, подсказки) не нужен — не просвечивает сквозь пьедестал
            _hud.SetVisible(inGame && !resultsModal);
            _pause.SetVisible(inGame && _pauseOpen);
            _touch.SetVisible(inGame && !resultsModal && !_pauseOpen && !_options.IsOpen);
            _results.SetMode(resultsModal ? ResultsView.Mode.Final : tabHeld ? ResultsView.Mode.Peek : ResultsView.Mode.Hidden,
                () => _resultsDismissed = true);

            _hud.SetBannersHidden(_pauseOpen || resultsModal || tabHeld);

            if (!inGame) _menu.Update();
            else _hud.Update();
            if (_pauseOpen) _pause.Update();
            _results.Update();
            if (_options.IsOpen) _options.Update();

            CursorFree = !inGame || _pauseOpen || resultsModal || _options.IsOpen;
            UpdatePlatform(inGame, resultsModal);
        }

        /// <summary>
        /// События площадки: «играет / не играет» и межстраничная реклама — только вне раунда
        /// (после итогов матча, правила — AdPolicy), чтобы не мешать сетевой игре.
        /// </summary>
        void UpdatePlatform(bool inGame, bool resultsModal)
        {
            var platform = GamePlatform.Current;
            bool playing = inGame && !resultsModal && !_pauseOpen;
            if (playing != _playing)
            {
                _playing = playing;
                if (playing) platform.GameplayStarted();
                else platform.GameplayStopped();
            }

            if (resultsModal && !_wasResultsModal)
                AdPolicy.OnMatchFinished();
            _wasResultsModal = resultsModal;
        }

        void HandleKeys(bool inGame, MatchManager match)
        {
            var kb = Keyboard.current;
            if (!inGame)
                _pauseOpen = false;
            if (kb == null)
                return;

            // открытые опции забирают Esc себе (отмена назначения клавиши, закрытие окна)
            if (_options.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                    _options.OnEscape();
                return;
            }
            if (_leaderboard.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                    _leaderboard.Close();
                return;
            }
            if (!inGame)
                return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (match != null && match.IsSpawned && match.Phase == MatchPhase.MatchEnd && !_resultsDismissed)
                    _resultsDismissed = true;
                // в браузере тот же Esc сначала снимает захват мыши, и пауза уже открыта (OnPointerLockLost) — не закрываем её сразу
                else if (!_pauseOpen || Time.unscaledTime - _pauseOpenedAt > 0.3f)
                    SetPause(!_pauseOpen);
            }

            // Enter у хоста — начать матч
            if (kb.enterKey.wasPressedThisFrame && !_pauseOpen && match != null && match.IsServer && match.CanStartMatch)
                match.RequestStartMatch();
        }
    }
}
