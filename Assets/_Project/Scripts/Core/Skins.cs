using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>Цвета одного скина солдата. Геометрия у всех скинов одна; метки цвета команды, лицо и бинокль не меняются.</summary>
    public sealed class SkinPalette
    {
        public string NameKey;
        /// <summary>Камуфляж: основной тон, тёмные пятна, рыжие пятна, светлые пятна.</summary>
        public Color CamoBase, CamoDark, CamoWarm, CamoLight;
        /// <summary>Каска и подсумки.</summary>
        public Color Helmet;
        public Color Leather, Buckle, Boot, Glove;
    }

    /// <summary>
    /// Скины солдата — только перекраска (в сборку добавляют лишь эти цвета).
    /// Номер скина синхронизируется по сети (PlayerHealth.Skin), выбор игрока хранится в PlayerPrefs.
    /// Новые скины добавлять только в КОНЕЦ списка: номер уходит в сеть и в сохранения.
    /// Скины-награды (<see cref="StreamViewer"/>) открывает сервер (Backend: player_skins) — список приходит при входе.
    /// </summary>
    public static class Skins
    {
        const string PrefKey = "skin";
        const string UnlockedKey = "unlockedSkins";

        /// <summary>Награда за игру на стриме у стримера-партнёра (Backend/src/bot.js STREAM_SKIN).</summary>
        public const int StreamViewer = 3;

        static readonly SkinPalette[] All =
        {
            // 0 — «лесной» камуфляж, как было изначально
            new SkinPalette
            {
                NameKey = "Стандарт",
                CamoBase = new Color(0.33f, 0.38f, 0.2f),
                CamoDark = new Color(0.17f, 0.24f, 0.12f),
                CamoWarm = new Color(0.36f, 0.27f, 0.17f),
                CamoLight = new Color(0.56f, 0.52f, 0.36f),
                Helmet = new Color(0.29f, 0.33f, 0.19f),
                Leather = new Color(0.26f, 0.18f, 0.11f),
                Buckle = new Color(0.62f, 0.6f, 0.52f),
                Boot = new Color(0.09f, 0.085f, 0.08f),
                Glove = new Color(0.16f, 0.14f, 0.12f),
            },
            // 1 — песочный камуфляж
            new SkinPalette
            {
                NameKey = "Пустыня",
                CamoBase = Hex(0xC2A878),
                CamoDark = Hex(0x5E4A30),
                CamoWarm = Hex(0x8C6E46),
                CamoLight = Hex(0xDCC9A0),
                Helmet = Hex(0xA08C64),
                Leather = Hex(0x4A3A28),
                Buckle = Hex(0xB0A68C),
                Boot = Hex(0x3A2E22),
                Glove = Hex(0x5A4632),
            },
            // 2 — чёрно-серая городская форма
            new SkinPalette
            {
                NameKey = "Спецназ",
                CamoBase = Hex(0x2E3036),
                CamoDark = Hex(0x1E1F23),
                CamoWarm = Hex(0x3E4148),
                CamoLight = Hex(0x55585F),
                Helmet = Hex(0x3A3C42),
                Leather = Hex(0x1A1A1C),
                Buckle = Hex(0x8A8C90),
                Boot = Hex(0x111113),
                Glove = Hex(0x1A1A1E),
            },
            // 3 — награда за стрим: фиолетовый камуфляж «как на Twitch»
            new SkinPalette
            {
                NameKey = "Зритель стрима",
                CamoBase = Hex(0x5B3A9A),
                CamoDark = Hex(0x2E1D52),
                CamoWarm = Hex(0x7A52C7),
                CamoLight = Hex(0xA98BE0),
                Helmet = Hex(0x4A2F80),
                Leather = Hex(0x2A1F3A),
                Buckle = Hex(0xC8B8E8),
                Boot = Hex(0x18121F),
                Glove = Hex(0x2A2038),
            },
        };

        static HashSet<int> _unlocked;

        /// <summary>Сервер прислал другой список открытых наград — окно скинов обновляется.</summary>
        public static event Action UnlockedChanged;

        /// <summary>Награда (не открыта всем сразу).</summary>
        public static bool IsReward(int skin) => skin == StreamViewer;

        public static bool IsUnlocked(int skin) => !IsReward(skin) || Unlocked.Contains(skin);

        // список хранится и на устройстве: без связи с сервером награда не пропадает
        static HashSet<int> Unlocked
        {
            get
            {
                if (_unlocked != null)
                    return _unlocked;
                _unlocked = new HashSet<int>();
                foreach (var part in PlayerPrefs.GetString(UnlockedKey, "").Split(','))
                    if (int.TryParse(part, out int skin))
                        _unlocked.Add(skin);
                return _unlocked;
            }
        }

        /// <summary>Открытые награды аккаунта — ответ сервера при входе.</summary>
        public static void SetUnlocked(int[] skins)
        {
            var set = new HashSet<int>(skins ?? Array.Empty<int>());
            if (set.SetEquals(Unlocked))
                return;
            _unlocked = set;
            PlayerPrefs.SetString(UnlockedKey, string.Join(",", set));
            PlayerPrefs.Save();
            UnlockedChanged?.Invoke();
        }

        /// <summary>Случайный скин для бота — из открытых всем (награды только у людей).</summary>
        public static int RandomForBot()
        {
            int skin;
            do skin = UnityEngine.Random.Range(0, All.Length);
            while (IsReward(skin));
            return skin;
        }

        public static int Count => All.Length;

        /// <summary>Палитра скина; неизвестный номер (чужая версия, порча данных) — стандартный.</summary>
        public static SkinPalette Get(int skin) => skin >= 0 && skin < All.Length ? All[skin] : All[0];

        /// <summary>Скин, выбранный игроком в меню.</summary>
        public static int Selected
        {
            get
            {
                int skin = PlayerPrefs.GetInt(PrefKey, 0);
                return skin >= 0 && skin < All.Length && IsUnlocked(skin) ? skin : 0;
            }
            set => PlayerPrefs.SetInt(PrefKey, Mathf.Clamp(value, 0, All.Length - 1));
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
