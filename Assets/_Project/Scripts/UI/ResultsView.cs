using System;
using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Player;
using RocketWars.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Таблица результатов: две колонки по командам, у каждого игрока — положил врагов / лёг сам / поднял /
    /// шахты / падения / положил своих и значки достижений (описание — при наведении).
    /// В конце матча открывается сама (Final), во время игры — пока зажат Tab (Peek).
    /// </summary>
    public class ResultsView
    {
        public enum Mode
        {
            Hidden,
            Peek,
            Final
        }

        const float RebuildInterval = 0.5f;

        static readonly (string title, string tip)[] Columns =
        {
            ("ВРАГИ", "Положил врагов"),
            ("ЛЁГ", "Сколько раз лёг сам"),
            ("ПОДНЯЛ", "Поднял союзников"),
            ("ШАХТЫ", "Взорвал вражеских шахт"),
            ("ПАДЕН.", "Падал с острова"),
            ("СВОИХ", "Положил своих"),
        };

        readonly VisualElement _screen, _panel, _columns, _buttons, _podium, _tooltip;
        readonly Label _title, _tooltipTitle, _tooltipText;
        readonly Button _newMatch;
        readonly PodiumStage _stage;
        Action _dismiss;
        Mode _mode = Mode.Hidden;
        float _nextRebuild;
        string _signature;

        public ResultsView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "overlay");
            _panel = Ui.El(_screen, "panel", "results-panel");
            _title = Ui.Text(_panel, Loc.T("ИТОГИ МАТЧА"), "title", "shadow");
            _columns = Ui.El(_panel, "results-columns");

            _buttons = Ui.El(_panel, "row");
            _buttons.style.justifyContent = Justify.Center;
            _newMatch = Ui.Btn(_buttons, Loc.T("Новый матч"), () => MatchManager.Instance?.RequestStartMatch(), "btn-primary");
            Ui.Btn(_buttons, Loc.T(TouchInput.Enabled ? "Закрыть" : "Закрыть (Esc)"), () => _dismiss?.Invoke());
            Ui.Btn(_buttons, Loc.T("Выйти из комнаты"), () => NetworkBootstrap.Instance?.ReturnToMenu(), "btn-danger");

            _podium = Ui.El(_screen, "podium");
            _podium.pickingMode = PickingMode.Ignore;
            _stage = new PodiumStage();

            _tooltip = Ui.El(_screen, "tooltip");
            _tooltip.pickingMode = PickingMode.Ignore;
            _tooltipTitle = Ui.Text(_tooltip, "", "tooltip-title");
            _tooltipText = Ui.Text(_tooltip, "");
            Ui.Show(_tooltip, false);
            Ui.Show(_screen, false);
        }

        public void Dispose() => _stage.Dispose();

        public void SetMode(Mode mode, Action dismiss)
        {
            _dismiss = dismiss;
            if (mode == _mode)
                return;
            _mode = mode;
            Ui.Show(_screen, mode != Mode.Hidden);
            Ui.Show(_buttons, mode == Mode.Final);
            Ui.Show(_podium, mode == Mode.Final);
            _stage.SetActive(mode == Mode.Final);
            _screen.EnableInClassList("results-final", mode == Mode.Final);
            _screen.pickingMode = mode == Mode.Final ? PickingMode.Position : PickingMode.Ignore;
            _title.text = Loc.T(mode == Mode.Final ? "ИТОГИ МАТЧА" : "ТЕКУЩИЙ СЧЁТ");
            Ui.Show(_tooltip, false);
            _nextRebuild = 0f;
            _signature = null;
        }

        public void Update()
        {
            if (_mode == Mode.Hidden)
                return;

            var m = MatchManager.Instance;
            Ui.Show(_newMatch, m != null && m.IsSpawned && m.IsServer && m.CanStartMatch);
            _stage.Update();

            if (Time.unscaledTime < _nextRebuild)
                return;
            _nextRebuild = Time.unscaledTime + RebuildInterval;
            Rebuild();
        }

        void Rebuild()
        {
            var stats = StatsNet.Instance;
            if (stats == null || !stats.IsSpawned)
                return;

            // перестраиваем только при изменениях — иначе пропадёт подсказка под курсором
            var entries = new List<PlayerStatsEntry>();
            var sig = new System.Text.StringBuilder();
            foreach (var e in stats.Entries)
            {
                entries.Add(e);
                sig.Append(e.GetHashCode()).Append(e.EnemiesDowned).Append(e.TimesDowned).Append(e.Revives).Append(e.SilosDestroyed)
                    .Append(e.Falls).Append(e.Teamkills).Append(e.Achievements).Append(e.Left).Append(e.Team).Append(";");
            }
            var m = MatchManager.Instance;
            if (m != null && m.IsSpawned)
                sig.Append((int)m.State.Score0).Append((int)m.State.Score1);
            if (sig.ToString() == _signature)
                return;
            _signature = sig.ToString();
            _columns.Clear();
            Ui.Show(_tooltip, false);
            entries.Sort((a, b) => b.EnemiesDowned != a.EnemiesDowned ? b.EnemiesDowned.CompareTo(a.EnemiesDowned) : a.TimesDowned.CompareTo(b.TimesDowned));

            ulong myId = NetworkPlayer.LocalPlayer != null ? NetworkPlayer.LocalPlayer.NetworkObjectId : ulong.MaxValue;
            var match = MatchManager.Instance;
            for (int team = 0; team < 2; team++)
            {
                var col = Ui.El(_columns, "results-team");
                string score = match != null && match.IsSpawned ? $"  {match.State.Score(team)}" : "";
                var title = Ui.Text(col, RuntimeMaterials.TeamName(team) + score, "results-team-title", "shadow", team == 0 ? "team0" : "team1");
                title.pickingMode = PickingMode.Ignore;

                var header = Ui.El(col, "results-row", "results-header");
                Ui.Text(header, Loc.T("ИГРОК"), "col-name");
                foreach (var c in Columns)
                    Hoverable(Ui.Text(header, Loc.T(c.title), "col-num"), Loc.T(c.tip), null);
                Ui.Text(header, Loc.T("НАГРАДЫ"), "col-badges");

                foreach (var e in entries)
                {
                    if (e.Team != team)
                        continue;
                    var row = Ui.El(col, "results-row");
                    if (e.PlayerId == myId)
                        row.AddToClassList("results-me");
                    Ui.Text(row, e.Name.ToString() + (e.Left ? " " + Loc.T("(вышел)") : ""), "col-name");
                    Ui.Text(row, e.EnemiesDowned.ToString(), "col-num");
                    Ui.Text(row, e.TimesDowned.ToString(), "col-num");
                    Ui.Text(row, e.Revives.ToString(), "col-num");
                    Ui.Text(row, e.SilosDestroyed.ToString(), "col-num");
                    Ui.Text(row, e.Falls.ToString(), "col-num");
                    Ui.Text(row, e.Teamkills.ToString(), "col-num");
                    AddBadges(Ui.El(row, "col-badges"), e.Achievements, stats.library);
                }
            }

            if (_mode == Mode.Final)
                BuildPodium(entries, stats.library);
        }

        /// <summary>Пьедестал под таблицей: 2-е, 1-е (в центре, выше) и 3-е место; под победителем — его награды столбиком.</summary>
        void BuildPodium(List<PlayerStatsEntry> sorted, AchievementLibrary library)
        {
            _podium.Clear();
            foreach (int place in new[] { 2, 1, 3 })
            {
                var slot = Ui.El(_podium, "podium-slot", $"podium-slot-{place}");
                slot.pickingMode = PickingMode.Ignore;
                if (place > sorted.Count)
                {
                    _stage.SetFigure(place, -1);
                    continue; // пустое место держит раскладку, чтобы победитель оставался в центре
                }
                var e = sorted[place - 1];

                Ui.Text(slot, Loc.F("{0} МЕСТО", place), "podium-place", "shadow", $"podium-color-{place}");
                Ui.Text(slot, e.Name.ToString() + (e.Left ? " " + Loc.T("(вышел)") : ""), "podium-name", "shadow", e.Team == 0 ? "team0" : "team1");
                Ui.Text(slot, $"{Loc.T("ВРАГИ")} {e.EnemiesDowned}  {Loc.T("ЛЁГ")} {e.TimesDowned}", "podium-stats");
                _stage.SetFigure(place, e.Team);
                // моделька крупнее своего места в раскладке: начинается под строкой статистики и свисает
                // за пьедестал (и ачивки у победителя); рисуется под всем: пьедесталом и текстом
                var space = Ui.El(slot, "podium-figure-space", $"podium-figure-space-{place}");
                var block = Ui.El(slot, "podium-block", $"podium-block-{place}");
                var figure = Ui.El(slot, "podium-figure", $"podium-figure-{place}");
                figure.pickingMode = PickingMode.Ignore;
                figure.SendToBack();
                figure.style.backgroundImage = Background.FromRenderTexture((RenderTexture)_stage.TextureOf(place));
                space.RegisterCallback<GeometryChangedEvent>(_ => figure.style.top = space.layout.y);
                var number = Ui.Text(slot, place.ToString(), "podium-number", "shadow");
                number.pickingMode = PickingMode.Ignore;
                block.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var r = block.layout;
                    number.style.left = r.x;
                    number.style.top = r.y;
                    number.style.width = r.width;
                    number.style.height = r.height;
                });

                if (place != 1)
                    continue;
                var list = Ui.El(slot, "podium-achievements");
                bool any = false;
                if (library != null)
                    for (int i = 0; i < library.achievements.Count && i < 32; i++)
                    {
                        if ((e.Achievements & (1u << i)) == 0)
                            continue;
                        any = true;
                        var a = library.achievements[i];
                        var row = Ui.El(list, "podium-achievement");
                        var badge = Ui.Text(row, a.Badge, "badge");
                        badge.style.backgroundColor = a.color;
                        Ui.Text(row, a.Title, "podium-achievement-title", "shadow");
                        Hoverable(row, a.Description, a.Title);
                    }
                if (!any)
                    Ui.Text(list, Loc.T("без наград"), "podium-stats");
            }
        }

        void AddBadges(VisualElement parent, uint mask, AchievementLibrary library)
        {
            if (library == null)
                return;
            for (int i = 0; i < library.achievements.Count && i < 32; i++)
            {
                if ((mask & (1u << i)) == 0)
                    continue;
                var a = library.achievements[i];
                var badge = Ui.Text(parent, a.Badge, "badge");
                badge.style.backgroundColor = a.color;
                Hoverable(badge, a.Description, a.Title);
            }
        }

        /// <summary>Подсказка при наведении курсора.</summary>
        void Hoverable(VisualElement e, string text, string title)
        {
            e.RegisterCallback<PointerEnterEvent>(ev =>
            {
                _tooltipTitle.text = title ?? "";
                Ui.Show(_tooltipTitle, !string.IsNullOrEmpty(title));
                _tooltipText.text = text;
                Ui.Show(_tooltip, true);
                MoveTooltip(ev.position);
            });
            e.RegisterCallback<PointerMoveEvent>(ev => MoveTooltip(ev.position));
            e.RegisterCallback<PointerLeaveEvent>(_ => Ui.Show(_tooltip, false));
        }

        void MoveTooltip(Vector2 panelPos) => Ui.Place(_tooltip, panelPos + new Vector2(18f, 18f));
    }
}
