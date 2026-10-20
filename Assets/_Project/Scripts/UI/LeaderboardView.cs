using RocketWars.Core;
using RocketWars.Online;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Окно рейтинга из главного меню: вкладки «Неделя» (сброс в понедельник) и «Всё время», топ-50 и своя строка.
    /// Таблица приходит с сервера рейтинга (Online/RatingApi) при каждом открытии и переключении вкладки.
    /// </summary>
    public class LeaderboardView
    {
        readonly VisualElement _screen;
        readonly Button _week, _all;
        readonly ScrollView _list;
        readonly Label _status, _me, _reset;
        bool _allTime;
        int _request; // ответ на старый запрос (другая вкладка) не показываем

        public bool IsOpen => Ui.IsShown(_screen);

        public LeaderboardView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel", "leaderboard-panel");
            Ui.Text(panel, Loc.T("РЕЙТИНГ"), "title", "shadow");

            var tabs = Ui.El(panel, "row", "row-center");
            _week = Ui.Btn(tabs, Loc.T("Неделя"), () => Select(false), "btn-small", "leaderboard-tab");
            _all = Ui.Btn(tabs, Loc.T("Всё время"), () => Select(true), "btn-small", "leaderboard-tab");
            _reset = Ui.Text(panel, "", "label-dim", "leaderboard-reset");

            var header = Ui.El(panel, "leaderboard-row", "leaderboard-header");
            Ui.Text(header, "#", "lb-rank");
            Ui.Text(header, Loc.T("ИГРОК"), "lb-name");
            Ui.Text(header, Loc.T("ОЧКИ"), "lb-num");
            Ui.Text(header, Loc.T("МАТЧИ"), "lb-num");
            Ui.Text(header, Loc.T("ПОБЕДЫ"), "lb-num");

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("list");
            _list.AddToClassList("leaderboard-list");
            panel.Add(_list);

            _me = Ui.Text(panel, "", "leaderboard-me");
            _status = Ui.Text(panel, "", "status");
            Ui.Text(panel, Loc.T("Очки: 10 за матч, +20 за победу, +2 за врага и за подъём союзника, +3 за шахту, +5 за награду"), "label-dim", "leaderboard-hint");
            Ui.Btn(panel, Loc.T(TouchInput.Enabled ? "Закрыть" : "Закрыть (Esc)"), Close, "btn-primary");
            Ui.Show(_screen, false);
        }

        public void Open()
        {
            Ui.Show(_screen, true);
            Select(_allTime);
        }

        public void Close() => Ui.Show(_screen, false);

        void Select(bool allTime)
        {
            _allTime = allTime;
            _week.EnableInClassList("btn-primary", !allTime);
            _all.EnableInClassList("btn-primary", allTime);
            Load();
        }

        async void Load()
        {
            int request = ++_request;
            _list.Clear();
            _me.text = "";
            _reset.text = "";
            _status.text = Loc.T("Загружаем…");
            var board = await RatingApi.Leaderboard(_allTime);
            if (request != _request || !IsOpen)
                return;
            if (board == null)
            {
                _status.text = Loc.T("Нет связи с сервером рейтинга");
                return;
            }
            _status.text = board.top == null || board.top.Length == 0 ? Loc.T("Пока пусто — сыграйте матч!") : "";
            if (board.resetsIn > 0)
                _reset.text = Loc.F("Сброс через {0} д {1} ч", board.resetsIn / 86400, board.resetsIn % 86400 / 3600);

            bool meInTop = false;
            if (board.top != null)
                foreach (var r in board.top)
                {
                    meInTop |= r.me;
                    AddRow(r.rank.ToString(), r.name, r.points, r.matches, r.wins, r.me);
                }

            if (!board.hasMe)
                return;
            var me = board.me;
            if (!me.ranked)
                _me.text = Loc.F("Вы (гость): {0} очков", me.points) + " · " + Loc.T("Гостевой вход: в общую таблицу попадают игроки Telegram");
            else if (me.rank > 0 && !meInTop)
                _me.text = Loc.F("Вы: {0} очков", me.points) + $" · #{me.rank}";
        }

        void AddRow(string rank, string name, int points, int matches, int wins, bool me)
        {
            var row = Ui.El(_list, "leaderboard-row");
            if (me)
                row.AddToClassList("results-me");
            Ui.Text(row, rank, "lb-rank");
            Ui.Text(row, string.IsNullOrEmpty(name) ? "?" : name, "lb-name");
            Ui.Text(row, points.ToString(), "lb-num");
            Ui.Text(row, matches.ToString(), "lb-num");
            Ui.Text(row, wins.ToString(), "lb-num");
        }
    }
}
