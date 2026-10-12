using System;
using System.Collections.Generic;
using RocketWars.Core;
using UnityEngine;

namespace RocketWars.Stats
{
    public enum FeedKind : byte
    {
        Bullet,
        Rocket,
        Fall,
        Revive,
        Silo,
        Flag
    }

    /// <summary>Одна строка ленты событий: «кто [иконка] кого».</summary>
    public struct FeedEntry
    {
        public FeedKind Kind;
        public string Actor;       // пусто — событие без виновника (упал сам, свой взрыв)
        public int ActorTeam;
        public ulong ActorId;
        public string Target;
        public int TargetTeam;
        public ulong TargetId;
    }

    /// <summary>
    /// Лента событий матча (как в Counter-Strike): сервер рассылает факты через StatsNet, здесь — событие для HUD
    /// и пиксельные иконки событий (генерируются один раз).
    /// </summary>
    public static class KillFeed
    {
        /// <summary>У всех: пришло событие для ленты.</summary>
        public static event Action<FeedEntry> Received;

        public static void Raise(in FeedEntry entry) => Received?.Invoke(entry);

        /// <summary>
        /// Подпись справа от иконки на языке этого клиента. У шахты и флага сервер присылает не текст,
        /// а команду цели и глагол-ключ («забрал» / «уронил» / «доставил», пусто — флаг вернулся на базу).
        /// </summary>
        public static string TargetText(in FeedEntry e)
        {
            string team = RuntimeMaterials.TeamNameGenitive(e.TargetTeam);
            switch (e.Kind)
            {
                case FeedKind.Silo:
                    return Loc.F("шахта {0}", team);
                case FeedKind.Flag:
                    return e.Target switch
                    {
                        "забрал" => Loc.F("забрал флаг {0}", team),
                        "уронил" => Loc.F("уронил флаг {0}", team),
                        "доставил" => Loc.F("доставил флаг {0}", team),
                        _ => Loc.F("флаг {0} на базе", team),
                    };
                default:
                    return e.Target;
            }
        }

        static readonly Dictionary<FeedKind, Texture2D> Icons = new Dictionary<FeedKind, Texture2D>();

        public static Texture2D Icon(FeedKind kind)
        {
            if (Icons.TryGetValue(kind, out var tex) && tex != null)
                return tex;
            tex = kind switch
            {
                FeedKind.Bullet => Draw(PistolArt, Color.white),
                FeedKind.Rocket => Draw(RocketArt, Color.white),
                FeedKind.Fall => Draw(FallArt, new Color(0.6f, 0.85f, 1f)),
                FeedKind.Revive => Draw(ReviveArt, new Color(0.45f, 1f, 0.5f)),
                FeedKind.Flag => Draw(FlagArt, new Color(1f, 0.85f, 0.3f)),
                _ => Draw(BlastArt, new Color(1f, 0.75f, 0.3f)),
            };
            Icons[kind] = tex;
            return tex;
        }

        /// <summary>Пиксель-арт из строк: «#» — цвет иконки, «o» — тёмная обводка, остальное прозрачно.</summary>
        static Texture2D Draw(string[] art, Color color)
        {
            int h = art.Length, w = art[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var outline = new Color(0f, 0f, 0f, 0.6f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char ch = art[h - 1 - y][x];
                tex.SetPixel(x, y, ch == '#' ? color : ch == 'o' ? outline : Color.clear);
            }
            tex.Apply(false, true);
            return tex;
        }

        static readonly string[] PistolArt =
        {
            "................",
            ".###############",
            ".###############",
            ".######.........",
            ".#####.##.......",
            ".#####..#.......",
            ".#####..........",
            "..####..........",
            "..####..........",
            "...###..........",
        };

        static readonly string[] RocketArt =
        {
            "................",
            ".##.............",
            ".###........#...",
            "..##############",
            "..##############",
            ".###........#...",
            ".##.............",
            "................",
        };

        static readonly string[] FallArt =
        {
            "....####....",
            "....####....",
            "....####....",
            "....####....",
            "....####....",
            ".##########.",
            "..########..",
            "...######...",
            "....####....",
            ".....##.....",
        };

        static readonly string[] ReviveArt =
        {
            "....####....",
            "....####....",
            "....####....",
            "############",
            "############",
            "############",
            "....####....",
            "....####....",
            "....####....",
            "............",
        };

        static readonly string[] FlagArt =
        {
            "##..........",
            "##########..",
            "##########..",
            "##########..",
            "##########..",
            "##..........",
            "##..........",
            "##..........",
            "##..........",
            "##..........",
        };

        static readonly string[] BlastArt =
        {
            "#....##....#",
            ".#...##...#.",
            "..#.####.#..",
            "...######...",
            "############",
            "############",
            "...######...",
            "..#.####.#..",
            ".#...##...#.",
            "#....##....#",
        };
    }
}
