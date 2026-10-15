using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>Языки игры (порядок — как в меню выбора; колонки таблицы сопоставляются по кодам из её заголовка).</summary>
    public enum Lang
    {
        En, Ru, ZhHans, Es, PtBr, De, Fr, Ja, Ko, Tr, Pl, Hi, Uk, It, Id, Be, Kk, Uz
    }

    /// <summary>
    /// Локализация. Ключ строки — её русский текст (так код остаётся читаемым, а русский язык не требует таблицы).
    /// Переводы — Resources/Loc/strings.txt (TSV в .txt — так Unity импортирует его как TextAsset): первая строка — коды языков, дальше «ключ\tперевод\t…»; \n в ячейке — перенос.
    /// Нет перевода — английский, нет и его — сам ключ. Язык по умолчанию даёт площадка (GamePlatform.DetectLanguage), выбор хранится в PlayerPrefs.
    /// </summary>
    public static class Loc
    {
        const string PrefKey = "language";

        static readonly string[] Codes = { "en", "ru", "zh-Hans", "es", "pt-BR", "de", "fr", "ja", "ko", "tr", "pl", "hi", "uk", "it", "id", "be", "kk", "uz" };

        /// <summary>Название языка на нём самом — для меню выбора.</summary>
        public static readonly string[] NativeNames =
        {
            "English", "Русский", "简体中文", "Español", "Português (Brasil)", "Deutsch", "Français", "日本語", "한국어",
            "Türkçe", "Polski", "हिन्दी", "Українська", "Italiano", "Bahasa Indonesia", "Беларуская", "Қазақша", "O‘zbekcha"
        };

        public static int Count => Codes.Length;

        /// <summary>Хинди без системного шрифта Nirmala UI (браузер) не отрисовать — там его нет в списке.</summary>
        public static bool IsAvailable(Lang lang) => lang != Lang.Hi || GamePlatform.Current.HasSystemFonts;

        /// <summary>Игрок сам выбирал язык в меню.</summary>
        public static bool HasSavedChoice => PlayerPrefs.GetInt(PrefKey, -1) >= 0;

        /// <summary>Язык по умолчанию от площадки (не сохраняется: пока игрок не выберет свой, каждый запуск берётся заново).</summary>
        public static void SetDefault(Lang lang)
        {
            if (!IsAvailable(lang) || _current == lang)
                return;
            _current = lang;
            Changed?.Invoke();
        }

        /// <summary>Перечитать сохранённый выбор (PlayerPrefs пришли из облака площадки).</summary>
        public static void ReloadSaved() => _current = null;

        /// <summary>Язык сменили (интерфейс пересобирается).</summary>
        public static event Action Changed;

        static Dictionary<string, string[]> _table;
        static Lang? _current;

        public static Lang Current
        {
            get
            {
                if (_current == null)
                {
                    int saved = PlayerPrefs.GetInt(PrefKey, -1);
                    _current = saved >= 0 && saved < Codes.Length ? (Lang)saved : GamePlatform.Current.DetectLanguage();
                    if (!IsAvailable(_current.Value))
                        _current = Lang.En;
                }
                return _current.Value;
            }
            set
            {
                if (_current == value)
                    return;
                _current = value;
                PlayerPrefs.SetInt(PrefKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static string T(string key) => Translate(key, Current);

        public static string F(string key, params object[] args) => string.Format(T(key), args);

        /// <summary>
        /// Для пульта (uGUI Text): он не умеет склеивать буквы деванагари, поэтому при хинди там английский.
        /// </summary>
        public static string TConsole(string key) => Translate(key, Current == Lang.Hi ? Lang.En : Current);

        public static string TConsoleF(string key, params object[] args) => string.Format(TConsole(key), args);

        /// <summary>Перевод с подстановкой текущих клавиш вместо токенов {use}, {jump}, … (GameInput).</summary>
        static string Translate(string key, Lang lang) => GameInput.ReplaceTokens(Lookup(key, lang));

        static string Lookup(string key, Lang lang)
        {
            if (string.IsNullOrEmpty(key) || lang == Lang.Ru)
                return key;
            Load();
            if (!_table.TryGetValue(key, out var row))
                return key;
            string s = row[(int)lang];
            if (string.IsNullOrEmpty(s))
                s = row[(int)Lang.En];
            return string.IsNullOrEmpty(s) ? key : s;
        }

        static void Load()
        {
            if (_table != null)
                return;
            _table = new Dictionary<string, string[]>();
            var asset = Resources.Load<TextAsset>("Loc/strings");
            if (asset == null)
            {
                Debug.LogError("[RocketWars] Resources/Loc/strings.txt not found");
                return;
            }

            string[] lines = asset.text.Replace("\r", "").Split('\n');
            // колонка файла → язык: порядок колонок в таблице может отличаться от enum
            string[] header = lines[0].Split('\t');
            var columnLang = new int[header.Length];
            for (int c = 0; c < header.Length; c++)
                columnLang[c] = Array.IndexOf(Codes, header[c].Trim());

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0 || lines[i][0] == '#')
                    continue;
                string[] cells = lines[i].Split('\t');
                var row = new string[Codes.Length];
                for (int c = 1; c < cells.Length && c < header.Length; c++)
                    if (columnLang[c] >= 0)
                        row[columnLang[c]] = cells[c].Replace("\\n", "\n");
                string key = cells[0].Replace("\\n", "\n");
                if (_table.ContainsKey(key))
                    Debug.LogWarning($"[RocketWars] duplicate loc key: {key}");
                _table[key] = row;
            }
        }
    }
}
