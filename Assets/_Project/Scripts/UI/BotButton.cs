using RocketWars.Core;
using RocketWars.Online;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Главное меню (Telegram): «Подписаться на бота» — анонсы стримов и итоги после стрима. Нажатие спрашивает у Telegram
    /// разрешение боту писать, сервер подписывает и шлёт в чат подтверждение. Подписан — зелёная плашка «Подписан на бота»;
    /// нажатие на неё — «Отписаться?», второе нажатие отписывает. Гостю и вне Telegram кнопки нет.
    /// </summary>
    public class BotButton
    {
        const float ConfirmSeconds = 4f;
        const float NoteSeconds = 6f;
        const float RefreshInterval = 20f;

        readonly Button _button;
        readonly VisualElement _icon;
        readonly Label _title, _note;
        bool _busy;
        float _confirmUntil, _noteUntil, _nextRefresh;
        string _noteText;
        static Texture2D _bell, _check;

        public BotButton(VisualElement parent)
        {
            _button = new Button(OnClick);
            _button.AddToClassList("bot-btn");
            parent.Add(_button);
            _icon = Ui.El(_button, "bot-icon");
            var text = Ui.El(_button, "bot-text");
            _title = Ui.Text(text, "", "bot-title");
            _note = Ui.Text(text, "", "bot-note");
            Ui.Show(_button, false);
            GamePlatform.Resumed += OnResumed;
            Refresh();
        }

        /// <summary>Меню снова на экране: подписку могли сменить в чате с ботом (/stop, «Старт»).</summary>
        public void OnShown() => _nextRefresh = 0f;

        public void Update()
        {
            bool available = GamePlatform.Current.HasBot && RatingApi.Verified;
            Ui.Show(_button, available);
            if (!available)
                return;
            if (!_busy && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                _ = RatingApi.RefreshBot();
            }
            Refresh();
        }

        void Refresh()
        {
            bool on = RatingApi.BotSubscribed;
            bool confirm = on && Time.unscaledTime < _confirmUntil;
            _button.EnableInClassList("bot-btn--on", on && !confirm);
            _button.EnableInClassList("bot-btn--confirm", confirm);
            _button.SetEnabled(!_busy);
            _icon.style.backgroundImage = on ? Check : Bell;
            _title.text = _busy ? "…" : confirm ? Loc.T("Отписаться?") : on ? Loc.T("Подписан на бота") : Loc.T("Подписаться на бота");
            _note.text = Time.unscaledTime < _noteUntil ? _noteText
                : confirm ? Loc.T("Нажми ещё раз")
                : on ? Loc.T("Анонсы стримов включены")
                : Loc.T("Анонсы стримов и награды");
            _note.EnableInClassList("bot-note--error", Time.unscaledTime < _noteUntil);
        }

        void OnClick()
        {
            if (_busy)
                return;
            Sfx.Play2D(Sfx.UiClick, 0.6f);
            _noteUntil = 0f;
            if (RatingApi.BotSubscribed)
            {
                if (Time.unscaledTime >= _confirmUntil)
                {
                    _confirmUntil = Time.unscaledTime + ConfirmSeconds;
                    return;
                }
                _confirmUntil = 0f;
                Send(false);
                return;
            }
            _busy = true;
            GamePlatform.Current.RequestBotMessages(allowed =>
            {
                if (allowed == true)
                {
                    Send(true);
                    return;
                }
                _busy = false;
                // null — старый Telegram: открылся чат с ботом, подписка — его «Старт»; проверим, когда игрок вернётся
                if (allowed == false)
                    Note(Loc.T("Telegram не разрешил боту писать"));
            });
        }

        async void Send(bool on)
        {
            _busy = true;
            string error = await RatingApi.SetBot(on);
            _busy = false;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            if (error == "no_access")
                Note(Loc.T("Telegram не разрешил боту писать"));
            else if (error != null)
                Note(Loc.T("Нет связи с сервером"));
        }

        void Note(string text)
        {
            _noteText = text;
            _noteUntil = Time.unscaledTime + NoteSeconds;
        }

        void OnResumed()
        {
            if (_button.panel == null)
            {
                GamePlatform.Resumed -= OnResumed; // меню пересобрали (смена языка)
                return;
            }
            _nextRefresh = 0f;
        }

        // ---------- пиксельные иконки 12×12 ----------

        static readonly string[] BellArt =
        {
            ".....oo.....",
            "....oYYo....",
            "...oYYYYo...",
            "..oYWYYYYo..",
            "..oYWYYYYo..",
            "..oYYYYYYo..",
            "..oYYYYYYo..",
            ".oYYYYYYYYo.",
            "oYYYYYYYYYYo",
            "oooooooooooo",
            "....oDDo....",
            ".....oo.....",
        };

        static readonly string[] CheckArt =
        {
            "............",
            "..........oo",
            ".........oWo",
            "........oWWo",
            ".oo....oWWo.",
            "oWWo..oWWo..",
            "oWWWooWWo...",
            ".oWWWWWo....",
            "..oWWWo.....",
            "...oWo......",
            "....o.......",
            "............",
        };

        static Texture2D Bell => _bell ??= Draw(BellArt);
        static Texture2D Check => _check ??= Draw(CheckArt);

        static Texture2D Draw(string[] art)
        {
            int h = art.Length, w = art[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, art[h - 1 - y][x] switch
                {
                    'Y' => new Color(1f, 0.82f, 0.25f),
                    'W' => Color.white,
                    'D' => new Color(0.75f, 0.5f, 0.12f),
                    'o' => new Color(0.06f, 0.08f, 0.12f),
                    _ => Color.clear,
                });
            tex.Apply(false, true);
            return tex;
        }
    }
}
