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

        public static UiRoot Instance { get; private set; }
        /// <summary>Курсор нужен интерфейсу (FpsController его не захватывает).</summary>
        public static bool CursorFree { get; private set; } = true;

        public StyleSheet styleSheet;

        MainMenuView _menu;
        HudView _hud;
        PauseView _pause;
        ResultsView _results;
        bool _pauseOpen;
        bool _resultsDismissed;

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
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);
            root.AddToClassList("root");
            root.style.unityFontDefinition = GameFonts.Ui;

            _hud = new HudView(root);
            _results = new ResultsView(root);
            _pause = new PauseView(root, () => SetPause(false));
            _menu = new MainMenuView(root);
        }

        void OnDestroy()
        {
            _hud?.Dispose();
            if (Instance == this)
                Instance = null;
        }

        public void SetPause(bool open) => _pauseOpen = open;

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
            _hud.SetVisible(inGame);
            _pause.SetVisible(inGame && _pauseOpen);

            var kb = Keyboard.current;
            bool tabHeld = kb != null && kb.tabKey.isPressed && inGame;
            bool resultsModal = matchEnd && !_resultsDismissed;
            _results.SetMode(resultsModal ? ResultsView.Mode.Final : tabHeld ? ResultsView.Mode.Peek : ResultsView.Mode.Hidden,
                () => _resultsDismissed = true);

            _hud.SetBannersHidden(_pauseOpen || resultsModal || tabHeld);

            if (!inGame) _menu.Update();
            else _hud.Update();
            if (_pauseOpen) _pause.Update();
            _results.Update();

            CursorFree = !inGame || _pauseOpen || resultsModal;
        }

        void HandleKeys(bool inGame, MatchManager match)
        {
            var kb = Keyboard.current;
            if (kb == null || !inGame)
            {
                _pauseOpen = false;
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (match != null && match.IsSpawned && match.Phase == MatchPhase.MatchEnd && !_resultsDismissed)
                    _resultsDismissed = true;
                else
                    _pauseOpen = !_pauseOpen;
            }

            // Enter у хоста — начать матч
            if (kb.enterKey.wasPressedThisFrame && !_pauseOpen && match != null && match.IsServer && match.CanStartMatch)
                match.RequestStartMatch();
        }
    }
}
