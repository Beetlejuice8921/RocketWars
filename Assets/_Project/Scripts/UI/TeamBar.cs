using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Player;
using RocketWars.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Верхняя панель матча (как в Counter-Strike): слева игроки красных, по центру счёт и раунд, справа зелёные.
    /// Карточка игрока — пиксельная голова в каске, поверх неё число фрагов, ник снизу; лежащие — чёрно-белые.
    /// </summary>
    public class TeamBar
    {
        const int NameChars = 8;

        readonly VisualElement _root;
        readonly VisualElement[] _sides = new VisualElement[2];
        readonly List<Card>[] _cards = { new List<Card>(), new List<Card>() };
        readonly Label _score0, _score1, _round;
        readonly List<PlayerHealth> _team = new List<PlayerHealth>();

        sealed class Card
        {
            public VisualElement Root;
            public Image Head;
            public Label Frags, Name;
            // что уже выведено — строки пересобираем только при изменении
            public int ShownFrags = -1;
            public string ShownName;
        }

        int _shownScore0 = -1, _shownScore1 = -1, _shownRound = -1;

        public TeamBar(VisualElement parent)
        {
            _root = Ui.El(parent, "teambar");
            _sides[0] = Ui.El(_root, "teambar-side", "teambar-left");
            var center = Ui.El(_root, "teambar-center");
            var scores = Ui.El(center, "teambar-scores");
            _score0 = Ui.Text(scores, "0", "teambar-score", "teambar-score0", "shadow");
            _score1 = Ui.Text(scores, "0", "teambar-score", "teambar-score1", "shadow");
            _round = Ui.Text(center, "ROUND 1", "teambar-round", "shadow");
            _sides[1] = Ui.El(_root, "teambar-side", "teambar-right");
        }

        public void Update(bool visible, in MatchState s)
        {
            Ui.Show(_root, visible);
            if (!visible)
                return;
            if (_shownScore0 != s.Score0)
                _score0.text = (_shownScore0 = s.Score0).ToString();
            if (_shownScore1 != s.Score1)
                _score1.text = (_shownScore1 = s.Score1).ToString();
            if (_shownRound != s.Round)
            {
                _shownRound = s.Round;
                _round.text = $"ROUND {s.Round}";
            }
            for (int team = 0; team < 2; team++)
                UpdateSide(team);
        }

        void UpdateSide(int team)
        {
            _team.Clear();
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned && p.Team == team)
                    _team.Add(p);
            _team.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));

            var cards = _cards[team];
            ulong me = NetworkPlayer.LocalPlayer != null ? NetworkPlayer.LocalPlayer.NetworkObjectId : ulong.MaxValue;
            for (int i = 0; i < _team.Count; i++)
            {
                if (i == cards.Count)
                    cards.Add(CreateCard(_sides[team]));
                var p = _team[i];
                var c = cards[i];
                Ui.Show(c.Root, true);
                bool downed = p.IsDowned;
                c.Head.image = HeadIcon(team, downed);
                int frags = Frags(p.NetworkObjectId);
                if (c.ShownFrags != frags)
                    c.Frags.text = (c.ShownFrags = frags).ToString();
                string name = p.DisplayName;
                if (!ReferenceEquals(c.ShownName, name))
                {
                    c.ShownName = name;
                    c.Name.text = name.Length > NameChars ? name.Substring(0, NameChars) : name;
                }
                c.Root.EnableInClassList("teambar-downed", downed);
                c.Root.EnableInClassList("teambar-me", p.NetworkObjectId == me);
                c.Name.EnableInClassList(team == 0 ? "team0" : "team1", !downed);
            }
            for (int i = _team.Count; i < cards.Count; i++)
                Ui.Show(cards[i].Root, false);
        }

        static Card CreateCard(VisualElement side)
        {
            var c = new Card { Root = Ui.El(side, "teambar-card") };
            var frame = Ui.El(c.Root, "teambar-head");
            c.Head = new Image { scaleMode = ScaleMode.ScaleToFit };
            c.Head.AddToClassList("teambar-head-img");
            frame.Add(c.Head);
            c.Frags = Ui.Text(frame, "0", "teambar-frags");
            c.Name = Ui.Text(c.Root, "", "teambar-name", "shadow");
            c.Root.pickingMode = PickingMode.Ignore;
            foreach (var e in c.Root.Query<VisualElement>().ToList())
                e.pickingMode = PickingMode.Ignore;
            return c;
        }

        /// <summary>Сколько врагов положил игрок в этом матче (из таблицы StatsNet).</summary>
        static int Frags(ulong playerId)
        {
            var stats = StatsNet.Instance;
            if (stats == null || !stats.IsSpawned)
                return 0;
            foreach (var e in stats.Entries)
                if (e.PlayerId == playerId)
                    return e.EnemiesDowned;
            return 0;
        }

        // ---------- пиксельная голова в каске ----------

        static readonly Texture2D[] Icons = new Texture2D[4];

        static Texture2D HeadIcon(int team, bool downed)
        {
            int i = team * 2 + (downed ? 1 : 0);
            if (Icons[i] == null)
                Icons[i] = DrawHead(RuntimeMaterials.TeamColor(team), downed);
            return Icons[i];
        }

        // H — каска, B — полоса команды, S — лицо, D — глаза, M — рот, o — обводка
        static readonly string[] HeadArt =
        {
            "................",
            ".....oooooo.....",
            "...ooHHHHHHoo...",
            "..oHHHHHHHHhHo..",
            ".oHHHHHHHHHHhHo.",
            ".oBBBBBBBBBBBBo.",
            "oHHHHHHHHHHHHHHo",
            "oooSSSSSSSSSSooo",
            "..oSSSSSSSSSSo..",
            "..oSDDSSSSDDSo..",
            "..oSDDSSSSDDSo..",
            "..oSSSSSSSSSSo..",
            "..oSSSSMMSSSSo..",
            "...oSSSSSSSSo...",
            "....oooooooo....",
            "................",
        };

        static Texture2D DrawHead(Color team, bool gray)
        {
            int h = HeadArt.Length, w = HeadArt[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var helmet = new Color(0.29f, 0.33f, 0.19f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = HeadArt[h - 1 - y][x] switch
                {
                    'H' => helmet,
                    'h' => Color.Lerp(helmet, Color.white, 0.3f),
                    'B' => team,
                    'S' => new Color(0.95f, 0.78f, 0.62f),
                    'D' => new Color(0.12f, 0.1f, 0.1f),
                    'M' => new Color(0.6f, 0.32f, 0.28f),
                    'o' => new Color(0.05f, 0.05f, 0.05f),
                    _ => Color.clear,
                };
                if (gray && c.a > 0f)
                {
                    float l = c.grayscale * 0.8f;
                    c = new Color(l, l, l, c.a);
                }
                tex.SetPixel(x, y, c);
            }
            tex.Apply(false, true);
            return tex;
        }
    }
}
