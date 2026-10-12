using RocketWars.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Окно опций (из главного меню и из паузы): язык, громкость, чувствительность мыши, переназначение клавиш.
    /// Переназначение: клик по клавише действия → ждём следующую клавишу или кнопку мыши (Esc — отмена);
    /// если она уже занята, действия меняются клавишами.
    /// </summary>
    public class OptionsView
    {
        readonly VisualElement _screen, _langPicker;
        readonly Label _sensValue;
        readonly Button[] _bindButtons = new Button[GameInput.ActionCount];
        int _listening = -1;
        int _listenFrame;
        bool _waitRelease; // нажатие, которым назначили кнопку мыши, не должно ещё и кликнуть по кнопке окна

        public bool IsOpen => Ui.IsShown(_screen);

        public OptionsView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel", "options-panel");
            Ui.Text(panel, Loc.T("ОПЦИИ"), "title", "shadow");

            var scroll = new ScrollView();
            scroll.AddToClassList("options-scroll");
            panel.Add(scroll);

            Ui.Text(scroll, Loc.T("ЯЗЫК"), "section");
            var lang = Ui.Btn(scroll, Loc.NativeNames[(int)Loc.Current], () => Ui.Show(_langPicker, true), "btn-small");
            Ui.LangFont(lang, Loc.Current);

            Ui.Text(scroll, Loc.T("ГРОМКОСТЬ"), "section");
            var volume = new Slider(0f, 1f) { value = UiRoot.Volume };
            volume.RegisterValueChangedCallback(e => UiRoot.Volume = e.newValue);
            scroll.Add(volume);

            Ui.Text(scroll, Loc.T("ЧУВСТВИТЕЛЬНОСТЬ МЫШИ"), "section");
            var sensRow = Ui.El(scroll, "row");
            var sens = new Slider(GameInput.MinSensitivity, GameInput.MaxSensitivity) { value = GameInput.Sensitivity };
            sens.AddToClassList("grow");
            sensRow.Add(sens);
            _sensValue = Ui.Text(sensRow, "", "options-value");
            sens.RegisterValueChangedCallback(e =>
            {
                GameInput.Sensitivity = e.newValue;
                _sensValue.text = FormatSensitivity();
            });
            _sensValue.text = FormatSensitivity();

            Ui.Text(scroll, Loc.T("УПРАВЛЕНИЕ"), "section");
            for (int i = 0; i < GameInput.ActionCount; i++)
            {
                int index = i;
                var row = Ui.El(scroll, "row", "bind-row");
                Ui.Text(row, GameInput.ActionName((GameAction)i), "bind-name");
                _bindButtons[i] = Ui.Btn(row, "", () => StartListening(index), "btn-small", "bind-key");
            }
            Ui.Btn(scroll, Loc.T("Сбросить управление"), () =>
            {
                GameInput.ResetAll();
                sens.SetValueWithoutNotify(GameInput.Sensitivity);
                _sensValue.text = FormatSensitivity();
                RefreshBinds();
            }, "btn-small");

            Ui.Btn(panel, Loc.T("Закрыть (Esc)"), Close, "btn-primary");

            _langPicker = BuildLangPicker(root);
            RefreshBinds();
            Ui.Show(_screen, false);
        }

        public void Open()
        {
            RefreshBinds();
            Ui.Show(_screen, true);
        }

        public void Close()
        {
            _listening = -1;
            Ui.Show(_langPicker, false);
            Ui.Show(_screen, false);
        }

        /// <summary>Esc: отменить ожидание клавиши → закрыть выбор языка → закрыть окно.</summary>
        public void OnEscape()
        {
            if (_listening >= 0)
            {
                _listening = -1;
                RefreshBinds();
            }
            else if (Ui.IsShown(_langPicker))
                Ui.Show(_langPicker, false);
            else
                Close();
        }

        public void Update()
        {
            if (_waitRelease && !AnyMouseButton())
                _waitRelease = false;
            if (_listening < 0 || Time.frameCount <= _listenFrame || !GameInput.TryCapture(out int code))
                return;
            GameInput.Set((GameAction)_listening, code);
            _listening = -1;
            _waitRelease = code < 0;
            RefreshBinds();
        }

        void StartListening(int index)
        {
            if (_waitRelease)
                return;
            _listening = index;
            _listenFrame = Time.frameCount;
            RefreshBinds();
        }

        void RefreshBinds()
        {
            for (int i = 0; i < _bindButtons.Length; i++)
            {
                bool listening = i == _listening;
                _bindButtons[i].text = listening ? Loc.T("Нажмите клавишу…") : GameInput.KeyName((GameAction)i);
                _bindButtons[i].EnableInClassList("bind-listening", listening);
            }
        }

        static string FormatSensitivity() => "×" + GameInput.Sensitivity.ToString("0.00");

        static bool AnyMouseButton()
        {
            var m = Mouse.current;
            return m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed
                                 || m.backButton.isPressed || m.forwardButton.isPressed);
        }

        /// <summary>
        /// Выбор языка поверх опций: каждое название написано на своём языке и своим шрифтом.
        /// Выбор сохраняется, интерфейс пересобирается (UiRoot ловит Loc.Changed и снова открывает опции).
        /// </summary>
        static VisualElement BuildLangPicker(VisualElement root)
        {
            var layer = Ui.El(root, "layer", "overlay");
            layer.RegisterCallback<PointerDownEvent>(e => { if (e.target == layer) Ui.Show(layer, false); });
            var panel = Ui.El(layer, "panel", "lang-panel");
            Ui.Text(panel, Loc.T("ЯЗЫК"), "section");
            var grid = Ui.El(panel, "lang-grid");
            for (int i = 0; i < Loc.Count; i++)
            {
                var lang = (Lang)i;
                var b = Ui.Btn(grid, Loc.NativeNames[i], () => Loc.Current = lang, "lang-option");
                if (lang == Loc.Current)
                    b.AddToClassList("btn-primary");
                Ui.LangFont(b, lang);
            }
            Ui.Btn(panel, Loc.T("Закрыть (Esc)"), () => Ui.Show(layer, false), "btn-small");
            Ui.Show(layer, false);
            return layer;
        }
    }
}
