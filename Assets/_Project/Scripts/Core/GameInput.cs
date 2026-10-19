using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RocketWars.Core
{
    /// <summary>Игровые действия с переназначаемой клавишей (порядок — строки в окне опций).</summary>
    public enum GameAction
    {
        Forward, Back, Left, Right, Jump, Sprint, Crouch, Use, Trick, Fire, Aim
    }

    /// <summary>
    /// Управление: на каждое действие одна клавиша или кнопка мыши (хранится в PlayerPrefs), плюс чувствительность мыши.
    /// Код привязки: ≥ 0 — <see cref="Key"/> клавиатуры, &lt; 0 — кнопка мыши.
    /// В текстах подсказок клавиша пишется токеном {use}, {jump}, … — Loc подставляет текущую.
    /// </summary>
    public static class GameInput
    {
        public const int MouseLeft = -1, MouseRight = -2, MouseMiddle = -3, MouseBack = -4, MouseForward = -5;

        const string BindPrefix = "bind.";
        const string SensitivityKey = "mouseSensitivity";
        public const float MinSensitivity = 0.1f, MaxSensitivity = 4f;

        static readonly int[] Defaults =
        {
            (int)Key.W, (int)Key.S, (int)Key.A, (int)Key.D, (int)Key.Space, (int)Key.LeftShift,
            GamePlatform.Current.IsWeb ? (int)Key.C : (int)Key.LeftCtrl, // в браузере Ctrl+W закрывает вкладку
            (int)Key.E, (int)Key.Q, MouseLeft, MouseRight
        };

        /// <summary>Названия действий — ключи Loc.</summary>
        static readonly string[] Names =
        {
            "Вперёд", "Назад", "Влево", "Вправо", "Прыжок / отстегнуть парашют", "Бег", "Присесть",
            "Действие (поднять, ящик, ракета, флаг)", "Трюк на ракете", "Стрельба / кнопки пульта", "Бинокль / прицел"
        };

        static readonly string[] Tokens = Array.ConvertAll(Enum.GetNames(typeof(GameAction)), n => "{" + n.ToLowerInvariant() + "}");

        static int[] _binds;
        static float? _sensitivity;

        public static int ActionCount => Defaults.Length;

        static int[] Binds
        {
            get
            {
                if (_binds == null)
                {
                    _binds = new int[Defaults.Length];
                    for (int i = 0; i < _binds.Length; i++)
                        _binds[i] = PlayerPrefs.GetInt(BindPrefix + (GameAction)i, Defaults[i]);
                }
                return _binds;
            }
        }

        /// <summary>Множитель чувствительности мыши (1 — как было задумано).</summary>
        public static float Sensitivity
        {
            get => _sensitivity ??= PlayerPrefs.GetFloat(SensitivityKey, 1f);
            set
            {
                _sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
                PlayerPrefs.SetFloat(SensitivityKey, _sensitivity.Value);
            }
        }

        public static bool IsPressed(GameAction action) =>
            Control(Binds[(int)action]) is ButtonControl c && c.isPressed || TouchInput.IsHeld(action) || SimulatedHeld(action);

        public static bool WasPressed(GameAction action) =>
            Control(Binds[(int)action]) is ButtonControl c && c.wasPressedThisFrame || TouchInput.WasDown(action) || SimulatedDown(action);

        /// <summary>Движение: x — вправо, y — вперёд, длина до 1. Клавиши дают 0/1, экранный джойстик — плавно.</summary>
        public static Vector2 MoveAxis
        {
            get
            {
                float h = (IsPressed(GameAction.Right) ? 1 : 0) - (IsPressed(GameAction.Left) ? 1 : 0);
                float v = (IsPressed(GameAction.Forward) ? 1 : 0) - (IsPressed(GameAction.Back) ? 1 : 0);
                return Vector2.ClampMagnitude(new Vector2(h, v) + TouchInput.Move, 1f);
            }
        }

#if UNITY_EDITOR
        // Редактор: «зажатые» из кода действия — запись ролика и проверки, когда окно Unity не в фокусе (ввод не доходит)
        static readonly System.Collections.Generic.Dictionary<GameAction, int> Simulated = new System.Collections.Generic.Dictionary<GameAction, int>();

        public static void Simulate(GameAction action, bool held)
        {
            if (!held) Simulated.Remove(action);
            else if (!Simulated.ContainsKey(action)) Simulated[action] = Time.frameCount;
        }

        public static void ClearSimulated() => Simulated.Clear();

        static bool SimulatedHeld(GameAction action) => Simulated.ContainsKey(action);

        static bool SimulatedDown(GameAction action) => Simulated.TryGetValue(action, out int frame) && Time.frameCount - frame <= 1;
#else
        static bool SimulatedHeld(GameAction action) => false;

        static bool SimulatedDown(GameAction action) => false;
#endif

        public static int Get(GameAction action) => Binds[(int)action];

        /// <summary>Назначить клавишу; если она уже занята другим действием — меняем их местами.</summary>
        public static void Set(GameAction action, int code)
        {
            var binds = Binds;
            int old = binds[(int)action];
            for (int i = 0; i < binds.Length; i++)
                if (i != (int)action && binds[i] == code)
                    Save(i, old);
            Save((int)action, code);
            PlayerPrefs.Save();
        }

        /// <summary>Перечитать клавиши и чувствительность (PlayerPrefs пришли из облака площадки).</summary>
        public static void ReloadSaved()
        {
            _binds = null;
            _sensitivity = null;
        }

        public static void ResetAll()
        {
            for (int i = 0; i < Defaults.Length; i++)
                Save(i, Defaults[i]);
            Sensitivity = 1f;
            PlayerPrefs.Save();
        }

        static void Save(int index, int code)
        {
            Binds[index] = code;
            PlayerPrefs.SetInt(BindPrefix + (GameAction)index, code);
        }

        /// <summary>Esc, Tab и Enter заняты интерфейсом (пауза, итоги, старт матча).</summary>
        public static bool IsReserved(Key key) => key == Key.Escape || key == Key.Tab || key == Key.Enter || key == Key.NumpadEnter
            // в браузере сочетания с Ctrl/Cmd (Ctrl+W, Ctrl+R…) перехватывает сам браузер
            || GamePlatform.Current.IsWeb && (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftMeta || key == Key.RightMeta);

        /// <summary>Первая нажатая в этом кадре клавиша или кнопка мыши (для переназначения).</summary>
        public static bool TryCapture(out int code)
        {
            code = 0;
            var kb = Keyboard.current;
            if (kb != null)
                foreach (var k in kb.allKeys)
                    if (k != null && k.wasPressedThisFrame && !IsReserved(k.keyCode))
                    {
                        code = (int)k.keyCode;
                        return true;
                    }
            var m = Mouse.current;
            if (m == null)
                return false;
            for (int c = MouseLeft; c >= MouseForward; c--)
                if (Control(c) is ButtonControl b && b.wasPressedThisFrame)
                {
                    code = c;
                    return true;
                }
            return false;
        }

        static ButtonControl Control(int code)
        {
            if (code >= 0)
            {
                var kb = Keyboard.current;
                return kb != null && code != (int)Key.None ? kb[(Key)code] : null;
            }
            var m = Mouse.current;
            if (m == null)
                return null;
            return code switch
            {
                MouseLeft => m.leftButton,
                MouseRight => m.rightButton,
                MouseMiddle => m.middleButton,
                MouseBack => m.backButton,
                MouseForward => m.forwardButton,
                _ => null,
            };
        }

        public static string ActionName(GameAction action) => Loc.T(Names[(int)action]);

        public static string KeyName(GameAction action) => CodeName(Binds[(int)action]);

        /// <summary>
        /// Подпись клавиши. Названия берутся из enum, а не из раскладки: при русской раскладке
        /// displayName у клавиши E — «У», а в подсказке нужна буква с клавиатуры.
        /// </summary>
        public static string CodeName(int code)
        {
            switch (code)
            {
                case MouseLeft: return Loc.T("ЛКМ");
                case MouseRight: return Loc.T("ПКМ");
                case MouseMiddle: return Loc.T("СКМ");
                case MouseBack: return Loc.T("Мышь 4");
                case MouseForward: return Loc.T("Мышь 5");
            }
            var key = (Key)code;
            if (key >= Key.Digit1 && key <= Key.Digit0)
                return key == Key.Digit0 ? "0" : ((int)(key - Key.Digit1) + 1).ToString();
            if (key >= Key.Numpad0 && key <= Key.Numpad9)
                return "Num " + (key - Key.Numpad0);
            return key switch
            {
                Key.Space => Loc.T("Пробел"),
                Key.LeftShift => "Shift",
                Key.RightShift => "R-Shift",
                Key.LeftCtrl => "Ctrl",
                Key.RightCtrl => "R-Ctrl",
                Key.LeftAlt => "Alt",
                Key.RightAlt => "R-Alt",
                Key.Backquote => "`",
                Key.Minus => "-",
                Key.Equals => "=",
                Key.LeftBracket => "[",
                Key.RightBracket => "]",
                Key.Semicolon => ";",
                Key.Quote => "'",
                Key.Comma => ",",
                Key.Period => ".",
                Key.Slash => "/",
                Key.Backslash => "\\",
                Key.UpArrow => "Up",
                Key.DownArrow => "Down",
                Key.LeftArrow => "Left",
                Key.RightArrow => "Right",
                _ => key.ToString(),
            };
        }

        /// <summary>
        /// Подставить в текст текущие клавиши вместо {use}, {jump}, {trick}, {fire}, {aim}, …;
        /// на телефоне — название экранной кнопки: «[ДЕЙСТВИЕ] - взять».
        /// </summary>
        public static string ReplaceTokens(string s)
        {
            if (s == null || s.IndexOf('{') < 0)
                return s;
            for (int i = 0; i < Tokens.Length; i++)
                if (s.Contains(Tokens[i]))
                    s = s.Replace(Tokens[i], TouchInput.Enabled && TouchLabel((GameAction)i) is string label
                        ? "[" + Loc.T(label) + "]"
                        : CodeName(Binds[i]));
            return s;
        }

        /// <summary>Надпись экранной кнопки действия (ключ Loc); null — отдельной кнопки нет (ходьба — джойстиком).</summary>
        public static string TouchLabel(GameAction action) => action switch
        {
            GameAction.Fire => "ОГОНЬ",
            GameAction.Jump => "ПРЫЖОК",
            GameAction.Crouch => "ПРИСЕСТЬ",
            GameAction.Aim => "ПРИЦЕЛ",
            GameAction.Use => "ДЕЙСТВИЕ",
            GameAction.Trick => "ТРЮК",
            _ => null,
        };
    }
}
