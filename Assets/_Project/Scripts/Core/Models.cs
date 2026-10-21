using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>Процедурные воксельные модели игры (по мотивам battleVision*.jpg).</summary>
    public static class Models
    {
        static readonly Color Concrete = new Color(0.62f, 0.62f, 0.63f);
        static readonly Color Steel = new Color(0.82f, 0.82f, 0.84f);
        static readonly Color Hole = new Color(0.12f, 0.12f, 0.13f);
        static readonly Color RocketWhite = new Color(0.94f, 0.94f, 0.94f);
        static readonly Color RocketRed = new Color(0.85f, 0.1f, 0.1f);
        static readonly Color Dark = new Color(0.16f, 0.16f, 0.18f);
        static readonly Color Skin = new Color(0.95f, 0.78f, 0.62f);

        // ---------- ракетная шахта: «пиксельный» цилиндр ----------

        /// <summary>Радиус башни шахты и сколько «пикселей» (рядов) укладывается в её диаметр.</summary>
        public const float SiloRadius = 1.15f;
        const int SiloPixels = 12;
        /// <summary>Высота верха крыши над плитой.</summary>
        public const float SiloRoofTop = 2.25f;

        /// <summary>
        /// Ступенчатый круг (как круги в Minecraft): ряды-параллелепипеды по оси Z, ширина ряда — по окружности,
        /// округлённая до «пикселя». Масштабированные копии с тем же числом рядов не имеют общих граней —
        /// кольца и пояса поверх башни не мерцают.
        /// </summary>
        static void VoxelDisc(BoxModel m, float y0, float y1, float radius, int pixels, Color color)
        {
            float pixel = radius * 2f / pixels;
            for (int i = 0; i < pixels; i++)
            {
                float z = -radius + (i + 0.5f) * pixel;
                float half = Mathf.Round(Mathf.Sqrt(Mathf.Max(0f, radius * radius - z * z)) / pixel + 0.25f) * pixel;
                if (half <= 0f)
                    continue;
                m.Box(new Vector3(0f, (y0 + y1) * 0.5f, z), new Vector3(half * 2f, y1 - y0, pixel), color);
            }
        }

        /// <summary>
        /// Ракетная шахта: бетонная плита, круглая башня в цвет команды с тёмными поясами и стальными ободами,
        /// круглая стальная крыша с люком, лампа сирены. Кольцо готовности — отдельный меш (SiloIndicator).
        /// </summary>
        public static (Mesh mesh, Material[] materials) Silo(int team)
        {
            var teamColor = RuntimeMaterials.TeamColor(team);
            var teamDark = Color.Lerp(teamColor, Color.black, 0.35f);
            var m = new BoxModel()
                .Box(new Vector3(0, 0.15f, 0), new Vector3(3f, 0.3f, 3f), Concrete);
            VoxelDisc(m, 0.3f, 2.05f, SiloRadius, SiloPixels, teamColor);                  // башня
            VoxelDisc(m, 0.3f, 0.45f, SiloRadius * 1.06f, SiloPixels, Steel);             // нижний обод
            VoxelDisc(m, 0.85f, 0.97f, SiloRadius * 1.03f, SiloPixels, teamDark);         // тёмные пояса
            VoxelDisc(m, 1.3f, 1.42f, SiloRadius * 1.03f, SiloPixels, teamDark);
            // тёмные ободки над и под кольцом готовности: кольцо видно и на зелёной башне
            VoxelDisc(m, 1.66f, 1.72f, SiloRadius * 1.09f, SiloPixels, Dark);
            VoxelDisc(m, 1.92f, 1.98f, SiloRadius * 1.09f, SiloPixels, Dark);
            VoxelDisc(m, 2.05f, SiloRoofTop, SiloRadius * 1.13f, SiloPixels, Steel);      // крыша
            VoxelDisc(m, SiloRoofTop - 0.005f, SiloRoofTop + 0.015f, 0.62f, 6, Hole);    // люк
            // лампа сирены на крыше
            m.Box(new Vector3(0.75f, 2.4f, 0.75f), new Vector3(0.3f, 0.3f, 0.3f), RocketRed);
            return m.Build($"silo_{team}");
        }

        /// <summary>Светящееся кольцо готовности в верхней части башни (цвет задаёт шахта).</summary>
        public static Mesh SiloIndicator()
        {
            var m = new BoxModel();
            VoxelDisc(m, 1.72f, 1.92f, SiloRadius * 1.07f, SiloPixels, Color.white);
            return m.Build("silo_indicator").mesh;
        }

        /// <summary>Во сколько раз модель ракеты крупнее исходной (длина ≈ 4.7 м, диаметр ≈ 0.84 м).</summary>
        public const float RocketScale = 2f;
        /// <summary>Сопло и кончик носа ракеты вдоль её оси (+Z) с учётом масштаба.</summary>
        public const float RocketTail = 1.02f * RocketScale, RocketNose = 1.34f * RocketScale;
        /// <summary>Радиус корпуса ракеты с учётом масштаба.</summary>
        public const float RocketRadius = 0.21f * RocketScale;

        /// <summary>«Пиксель» сечения ракеты (до масштаба): все круглые части нарезаны рядами такой высоты.</summary>
        const float RocketPixel = 0.07f;

        /// <summary>
        /// Ступенчатый круг в сечении, вытянутый вдоль оси Z (от z0 до z1): ряды-параллелепипеды по оси Y,
        /// ширина ряда — по окружности, округлённая до «пикселя» (как VoxelDisc, только вдоль другой оси).
        /// </summary>
        static void VoxelTube(BoxModel m, float z0, float z1, float radius, Color color) =>
            VoxelTube(m, z0, z1, radius, color, RocketPixel, 0f);

        /// <summary>Труба вдоль Z с осью на высоте centerY, ступенчато-круглая из кубиков размером ~pixelSize.</summary>
        static void VoxelTube(BoxModel m, float z0, float z1, float radius, Color color, float pixelSize, float centerY)
        {
            int pixels = Mathf.Max(2, Mathf.RoundToInt(radius * 2f / pixelSize));
            float pixel = radius * 2f / pixels;
            for (int i = 0; i < pixels; i++)
            {
                float y = -radius + (i + 0.5f) * pixel;
                float half = Mathf.Round(Mathf.Sqrt(Mathf.Max(0f, radius * radius - y * y)) / pixel + 0.25f) * pixel;
                if (half <= 0f)
                    continue;
                m.Box(new Vector3(0f, centerY + y, (z0 + z1) * 0.5f), new Vector3(half * 2f, pixel, z1 - z0), color);
            }
        }

        /// <summary>
        /// Ракета вдоль +Z, «пиксельно-круглая» в сечении: белый корпус, чёрный пояс и сопло,
        /// ступенчатый конус носа и стабилизаторы — в цвет команды (масштабируется RocketScale).
        /// </summary>
        public static (Mesh mesh, Material[] materials) Rocket(int team)
        {
            var teamColor = RuntimeMaterials.TeamColor(team);
            var m = new BoxModel();
            VoxelTube(m, -0.85f, 0.85f, 0.21f, RocketWhite);       // корпус
            VoxelTube(m, 0.39f, 0.51f, 0.21f * 1.07f, Dark);       // пояс
            VoxelTube(m, -1.02f, -0.85f, 0.15f, Dark);             // сопло
            // нос — ступенчатый конус из сужающихся колец до кончика на 1.34
            VoxelTube(m, 0.85f, 0.97f, 0.18f, teamColor);
            VoxelTube(m, 0.97f, 1.09f, 0.145f, teamColor);
            VoxelTube(m, 1.09f, 1.2f, 0.11f, teamColor);
            VoxelTube(m, 1.2f, 1.29f, 0.07f, teamColor);
            VoxelTube(m, 1.29f, 1.34f, 0.035f, teamColor);
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, 0f, i * 90f);
                m.Box(rot * new Vector3(0f, 0.32f, -0.6f), new Vector3(0.06f, 0.3f, 0.5f), teamColor, rot); // стабилизатор
            }
            return m.Build($"rocket_{team}");
        }

        // ---------- персонаж: солдат в «лесном» камуфляже ----------
        // Pivot — в ногах, лицо смотрит в +Z, рост 2 м. Конечности — отдельные части с шарниром в плече/бедре.
        // Камуфляж у обеих сторон одинаковый, цвет команды — полоса на каске, нарукавные повязки, нашивки на груди и спине.

        const float CamoTile = 0.06f;

        static readonly Color CamoOlive = new Color(0.33f, 0.38f, 0.2f);
        static readonly Color CamoGreen = new Color(0.17f, 0.24f, 0.12f);
        static readonly Color CamoBrown = new Color(0.36f, 0.27f, 0.17f);
        static readonly Color CamoKhaki = new Color(0.56f, 0.52f, 0.36f);
        static readonly Color HelmetOlive = new Color(0.29f, 0.33f, 0.19f);
        static readonly Color Leather = new Color(0.26f, 0.18f, 0.11f);
        static readonly Color Buckle = new Color(0.62f, 0.6f, 0.52f);
        static readonly Color BootBlack = new Color(0.09f, 0.085f, 0.08f);
        static readonly Color Glove = new Color(0.16f, 0.14f, 0.12f);

        /// <summary>«Лесной» камуфляж: пятна из двух слоёв шума, offset разносит узор между частями тела.</summary>
        static Color Camo(Vector3 p, float offset)
        {
            const float scale = 3.2f;
            float a = Mathf.PerlinNoise(p.x * scale + p.z * scale * 0.7f + offset, p.y * scale + 11.3f);
            float b = Mathf.PerlinNoise(p.z * scale + p.x * scale * 0.5f + 37.1f, p.y * scale * 1.3f + offset * 0.5f);
            if (a < 0.36f) return CamoGreen;
            if (a > 0.66f) return CamoBrown;
            return b > 0.6f ? CamoKhaki : CamoOlive;
        }

        /// <summary>Шея: где голова крепится к телу (низ головы).</summary>
        public const float NeckY = 1.55f;

        /// <summary>Тело солдата с головой; withHead = false — без неё, голову тогда даёт CharacterHead (сцена меню).</summary>
        public static (Mesh mesh, Material[] materials) CharacterBody(int team, bool withHead = true)
        {
            var teamColor = RuntimeMaterials.TeamColor(team);
            var m = new BoxModel()
                // куртка в камуфляже
                .PatternBox(new Vector3(0, 1.2f, 0), new Vector3(0.6f, 0.7f, 0.34f), CamoTile, p => Camo(p, 0f))
                // нашивки цвета команды на груди и спине — крупные, чтобы читались на любом скине
                .Box(new Vector3(0.13f, 1.37f, 0.175f), new Vector3(0.2f, 0.15f, 0.014f), teamColor)
                .Box(new Vector3(0, 1.28f, -0.175f), new Vector3(0.38f, 0.28f, 0.014f), teamColor)
                // ремень с пряжкой и подсумками
                .Box(new Vector3(0, 0.89f, 0), new Vector3(0.63f, 0.09f, 0.37f), Leather)
                .Box(new Vector3(0, 0.89f, 0.19f), new Vector3(0.1f, 0.07f, 0.02f), Buckle)
                .Box(new Vector3(-0.2f, 0.92f, 0.2f), new Vector3(0.12f, 0.13f, 0.06f), HelmetOlive)
                .Box(new Vector3(0.2f, 0.92f, 0.2f), new Vector3(0.12f, 0.13f, 0.06f), HelmetOlive)
                .Box(new Vector3(0, 0.95f, -0.2f), new Vector3(0.22f, 0.15f, 0.07f), HelmetOlive);
            if (withHead)
                AddHead(m, Vector3.zero);
            return m.Build(withHead ? $"char_body_{team}" : $"char_torso_{team}");
        }

        /// <summary>Отдельная голова: pivot в шее (NeckY), в остальном как на CharacterBody.</summary>
        public static (Mesh mesh, Material[] materials) CharacterHead()
        {
            var m = new BoxModel();
            AddHead(m, new Vector3(0f, -NeckY, 0f));
            return m.Build("char_head");
        }

        static void AddHead(BoxModel m, Vector3 o)
        {
            // голова и лицо
            m.Box(o + new Vector3(0, 1.75f, 0), new Vector3(0.4f, 0.4f, 0.4f), Skin)
             .Box(o + new Vector3(-0.1f, 1.78f, 0.205f), new Vector3(0.08f, 0.06f, 0.02f), Dark)
             .Box(o + new Vector3(0.1f, 1.78f, 0.205f), new Vector3(0.08f, 0.06f, 0.02f), Dark)
             // подбородочный ремень каски
             .Box(o + new Vector3(-0.205f, 1.62f, -0.06f), new Vector3(0.015f, 0.12f, 0.035f), Leather)
             .Box(o + new Vector3(0.205f, 1.62f, -0.06f), new Vector3(0.015f, 0.12f, 0.035f), Leather)
             .Box(o + new Vector3(0f, 1.555f, -0.06f), new Vector3(0.42f, 0.02f, 0.035f), Leather);
        }

        /// <summary>
        /// Парашют: прямоугольный купол из панелей в камуфляжных тонах (края опущены), стропы к плечам.
        /// Pivot — в ногах парашютиста.
        /// </summary>
        public static (Mesh mesh, Material[] materials) Parachute()
        {
            var m = new BoxModel();
            const int n = 5;
            const float panel = 0.68f, top = 4.3f;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float x = (i - (n - 1) * 0.5f) * panel, z = (j - (n - 1) * 0.5f) * panel * 0.8f;
                // купол: к краям ниже
                float drop = (x * x + z * z) * 0.13f;
                var color = (i + j) % 2 == 0 ? CamoOlive : CamoKhaki;
                m.Box(new Vector3(x, top - drop, z), new Vector3(panel, 0.08f, panel * 0.8f), color);
            }

            // стропы от углов купола к плечам
            var line = new Color(0.12f, 0.12f, 0.1f);
            var shoulders = new[] { new Vector3(-0.35f, 1.55f, 0f), new Vector3(0.35f, 1.55f, 0f) };
            float hx = (n - 1) * 0.5f * panel, hz = (n - 1) * 0.5f * panel * 0.8f;
            foreach (var corner in new[] { new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz) })
            {
                var c = new Vector3(corner.x, top - (corner.x * corner.x + corner.z * corner.z) * 0.13f, corner.z);
                var s = corner.x < 0 ? shoulders[0] : shoulders[1];
                Vector3 mid = (c + s) * 0.5f, dir = c - s;
                m.Box(mid, new Vector3(0.025f, dir.magnitude, 0.025f), line, Quaternion.FromToRotation(Vector3.up, dir.normalized));
            }
            return m.Build("parachute");
        }

        /// <summary>Расстояние от pivot пистолета до дула (вдоль +Z).</summary>
        public const float PistolMuzzle = 0.13f;

        /// <summary>Пистолет: pivot у рукояти, ствол смотрит в +Z.</summary>
        public static (Mesh mesh, Material[] materials) Pistol()
        {
            var steel = new Color(0.2f, 0.21f, 0.22f);
            var dark = new Color(0.1f, 0.1f, 0.1f);
            var grip = new Color(0.24f, 0.17f, 0.11f);
            return new BoxModel()
                .Box(new Vector3(0f, 0.045f, 0.03f), new Vector3(0.042f, 0.045f, 0.2f), steel)      // затвор
                .Box(new Vector3(0f, 0.015f, 0.025f), new Vector3(0.036f, 0.025f, 0.15f), dark)     // рамка
                .Box(new Vector3(0f, -0.04f, -0.035f), new Vector3(0.036f, 0.1f, 0.05f), grip, Quaternion.Euler(-15f, 0f, 0f)) // рукоять
                .Box(new Vector3(0f, -0.005f, 0.02f), new Vector3(0.01f, 0.03f, 0.045f), dark)      // скоба спуска
                .Box(new Vector3(0f, 0.072f, 0.115f), new Vector3(0.008f, 0.012f, 0.01f), dark)     // мушка
                .Box(new Vector3(0f, 0.072f, -0.055f), new Vector3(0.02f, 0.012f, 0.01f), dark)     // целик
                .Build("pistol");
        }

        /// <summary>Кисть в перчатке, обхватившая рукоять, и манжета камуфляжного рукава — для вида от первого лица.</summary>
        public static (Mesh mesh, Material[] materials) ViewModelHand(int team)
        {
            return new BoxModel()
                .Box(new Vector3(0f, -0.005f, 0f), new Vector3(0.06f, 0.075f, 0.075f), Glove)                // кулак на рукояти
                .Box(new Vector3(-0.028f, 0.03f, 0.03f), new Vector3(0.018f, 0.02f, 0.05f), Glove)         // большой палец
                .PatternBox(new Vector3(0f, -0.03f, -0.1f), new Vector3(0.085f, 0.08f, 0.13f), 0.02f, p => Camo(p, 21f)) // манжета
                .Build($"vm_hand_{team}");
        }

        /// <summary>Бинокль: две трубы с окулярами и линзами, pivot в центре, объективы смотрят в +Z.</summary>
        public static (Mesh mesh, Material[] materials) Binoculars()
        {
            var body = new Color(0.14f, 0.15f, 0.12f);
            var rubber = new Color(0.07f, 0.07f, 0.07f);
            var lens = new Color(0.35f, 0.5f, 0.62f);
            var m = new BoxModel()
                .Box(new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0.05f, 0.08f), body); // мостик
            foreach (float x in new[] { -0.075f, 0.075f })
            {
                m.Box(new Vector3(x, 0f, 0.02f), new Vector3(0.085f, 0.085f, 0.2f), body)
                 .Box(new Vector3(x, 0f, -0.1f), new Vector3(0.07f, 0.07f, 0.05f), rubber)
                 .Box(new Vector3(x, 0f, 0.125f), new Vector3(0.065f, 0.065f, 0.012f), lens);
            }
            return m.Build("binoculars");
        }

        /// <summary>Где сидит каска на теле (низ каски) и насколько она съезжает на голову в приседе.</summary>
        public const float HelmetBaseY = 1.88f;
        public const float HelmetCrouchDrop = 0.1f;

        /// <summary>
        /// Армейская каска (на 25% крупнее головы): круглый ступенчатый купол, поля и полоса цвета команды.
        /// Отдельная часть с pivot в нижней точке — двигается при приседе, покачивается при ходьбе, слетает при падении.
        /// </summary>
        public static (Mesh mesh, Material[] materials) CharacterHelmet(int team)
        {
            const float k = 1.25f;
            var m = new BoxModel();
            VoxelDisc(m, -0.01f * k, 0.02f * k, 0.3f * k, 10, HelmetOlive);               // поля
            VoxelDisc(m, 0f, 0.12f * k, 0.26f * k, 8, HelmetOlive);                      // купол — ярусами к макушке
            VoxelDisc(m, 0.12f * k, 0.18f * k, 0.22f * k, 7, HelmetOlive);
            VoxelDisc(m, 0.18f * k, 0.23f * k, 0.16f * k, 5, HelmetOlive);
            VoxelDisc(m, 0.23f * k, 0.26f * k, 0.09f * k, 3, HelmetOlive);
            VoxelDisc(m, 0.045f * k, 0.12f * k, 0.26f * k * 1.06f, 8, RuntimeMaterials.TeamColor(team)); // полоса команды (широкая)
            return m.Build($"char_helmet_{team}");
        }

        /// <summary>Насколько броне-каска приподнята над обычной: из-под неё видны низ купола и полоса команды.</summary>
        public const float ArmorHelmetLift = 0.12f;

        /// <summary>
        /// Вторая каска цвета бронежилета — надевается поверх обычной, пока есть броня (чуть крупнее и выше неё).
        /// Pivot в нижней точке; дочерняя к обычной каске, поэтому повторяет её покачивание и присед.
        /// </summary>
        public static (Mesh mesh, Material[] materials) ArmorHelmet()
        {
            const float k = 1.25f * 1.1f;
            var m = new BoxModel();
            VoxelDisc(m, -0.01f * k, 0.02f * k, 0.3f * k, 10, VestNavy);                 // поля
            VoxelDisc(m, 0f, 0.12f * k, 0.26f * k, 8, VestNavy);
            VoxelDisc(m, 0.12f * k, 0.18f * k, 0.22f * k, 7, VestNavy);
            VoxelDisc(m, 0.18f * k, 0.23f * k, 0.16f * k, 5, VestNavy);
            VoxelDisc(m, 0.23f * k, 0.26f * k, 0.09f * k, 3, VestNavy);
            VoxelDisc(m, 0.05f * k, 0.09f * k, 0.26f * k * 1.04f, 8, VestNavyDark);     // тёмный ремешок, как карман жилета
            return m.Build("armor_helmet");
        }

        /// <summary>Рука: шарнир в плече (0,0,0), висит вниз. Рукав в камуфляже с повязкой цвета команды, перчатка.</summary>
        public static (Mesh mesh, Material[] materials) CharacterArm(int team)
        {
            return new BoxModel()
                .PatternBox(new Vector3(0, -0.28f, 0), new Vector3(0.18f, 0.56f, 0.22f), CamoTile, p => Camo(p, 5.7f))
                .Box(new Vector3(0, -0.15f, 0), new Vector3(0.195f, 0.14f, 0.235f), RuntimeMaterials.TeamColor(team))
                .Box(new Vector3(0, -0.62f, 0), new Vector3(0.16f, 0.14f, 0.18f), Glove)
                .Build($"char_arm_{team}");
        }

        /// <summary>Нога: шарнир в бедре (0,0,0). Штаны в камуфляже, высокие сапоги.</summary>
        public static (Mesh mesh, Material[] materials) CharacterLeg()
        {
            return new BoxModel()
                .PatternBox(new Vector3(0, -0.27f, 0), new Vector3(0.24f, 0.54f, 0.28f), CamoTile, p => Camo(p, 13.1f))
                // голенище и стопа сапога
                .Box(new Vector3(0, -0.66f, 0f), new Vector3(0.26f, 0.32f, 0.3f), BootBlack)
                .Box(new Vector3(0, -0.785f, 0.05f), new Vector3(0.27f, 0.07f, 0.38f), BootBlack)
                .Box(new Vector3(0, -0.52f, 0f), new Vector3(0.27f, 0.04f, 0.31f), Leather)
                .Build("char_leg");
        }
            // ---------- ящики с припасами ----------

        static readonly Color CrateWood = new Color(0.55f, 0.38f, 0.2f);
        static readonly Color CrateWoodDark = new Color(0.4f, 0.27f, 0.13f);
        static readonly Color CrateIron = new Color(0.22f, 0.22f, 0.23f);
        static readonly Color CrateStraw = new Color(0.78f, 0.66f, 0.33f);
        static readonly Color Olive = new Color(0.27f, 0.31f, 0.17f);
        static readonly Color VestNavy = new Color(0.12f, 0.2f, 0.42f);
        static readonly Color VestNavyDark = new Color(0.07f, 0.12f, 0.27f);

        public const float CrateWall = 0.08f;
        public const float CrateBodyHeight = 0.86f;
        /// <summary>Верх соломенной подстилки: на ней лежит содержимое, чтобы его было видно из открытого ящика.</summary>
        public const float CrateFill = 0.46f;

        /// <summary>Ящик 1×1×1 без крышки: pivot в центре дна. Стенки из досок с распорками, железные уголки, внутри солома.</summary>
        public static (Mesh mesh, Material[] materials) CrateBody()
        {
            const float h = CrateBodyHeight, w = CrateWall;
            var m = new BoxModel()
                .Box(new Vector3(0f, w * 0.5f, 0f), new Vector3(1f, w, 1f), CrateWoodDark)                         // дно
                .Box(new Vector3(0f, (w + CrateFill) * 0.5f, 0f), new Vector3(1f - 2f * w, CrateFill - w, 1f - 2f * w), CrateStraw); // солома
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, i * 90f, 0f);
                m.Box(rot * new Vector3(0f, h * 0.5f, 0.5f - w * 0.5f), new Vector3(1f, h, w), CrateWood, rot);
                // щели между досками и диагональная распорка снаружи
                foreach (float y in new[] { h * 0.36f, h * 0.68f })
                    m.Box(rot * new Vector3(0f, y, 0.5f + 0.003f), new Vector3(0.9f, 0.025f, 0.01f), CrateWoodDark, rot);
                m.Box(rot * new Vector3(0f, h * 0.5f, 0.5f + 0.012f), new Vector3(0.1f, h * 1.15f, 0.02f), CrateWoodDark,
                    rot * Quaternion.Euler(0f, 0f, 42f));
                // железный уголок на вертикальном ребре
                m.Box(rot * new Vector3(0.5f, h * 0.5f, 0.5f), new Vector3(0.1f, h + 0.02f, 0.1f), CrateIron, rot);
            }
            return m.Build("crate_body");
        }

        /// <summary>Крышка: pivot на заднем верхнем ребре (петли), сама крышка лежит вперёд, по +Z.</summary>
        public static (Mesh mesh, Material[] materials) CrateLid()
        {
            return new BoxModel()
                .Box(new Vector3(0f, 0.05f, 0.5f), new Vector3(1.02f, 0.1f, 1.02f), CrateWood)
                .Box(new Vector3(0f, 0.105f, 0.27f), new Vector3(0.92f, 0.012f, 0.025f), CrateWoodDark)
                .Box(new Vector3(0f, 0.105f, 0.5f), new Vector3(0.92f, 0.012f, 0.025f), CrateWoodDark)
                .Box(new Vector3(0f, 0.105f, 0.73f), new Vector3(0.92f, 0.012f, 0.025f), CrateWoodDark)
                .Box(new Vector3(0f, 0.05f, 1.0f), new Vector3(1.04f, 0.12f, 0.06f), CrateIron)   // окантовка спереди
                .Box(new Vector3(0f, -0.01f, 1.03f), new Vector3(0.14f, 0.16f, 0.04f), CrateIron) // защёлка
                .Box(new Vector3(0.3f, 0.05f, 0f), new Vector3(0.12f, 0.12f, 0.06f), CrateIron)   // петли
                .Box(new Vector3(-0.3f, 0.05f, 0f), new Vector3(0.12f, 0.12f, 0.06f), CrateIron)
                .Build("crate_lid");
        }

        /// <summary>Аптечка: белый футляр с красными крестами (pivot по центру дна).</summary>
        public static (Mesh mesh, Material[] materials) Medkit()
        {
            var white = new Color(0.93f, 0.93f, 0.9f);
            var red = new Color(0.85f, 0.08f, 0.08f);
            return new BoxModel()
                .Box(new Vector3(0f, 0.09f, 0f), new Vector3(0.34f, 0.18f, 0.24f), white)
                .Box(new Vector3(0f, 0.182f, 0f), new Vector3(0.16f, 0.01f, 0.05f), red)
                .Box(new Vector3(0f, 0.182f, 0f), new Vector3(0.05f, 0.01f, 0.16f), red)
                .Box(new Vector3(0f, 0.09f, 0.122f), new Vector3(0.12f, 0.04f, 0.01f), red)
                .Box(new Vector3(0f, 0.09f, 0.122f), new Vector3(0.04f, 0.12f, 0.01f), red)
                .Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.12f, 0.03f, 0.03f), new Color(0.3f, 0.3f, 0.3f)) // ручка
                .Build("medkit");
        }

        /// <summary>Конец ствола пулемёта по +Z от рукояти.</summary>
        public const float MachineGunMuzzle = 0.64f;

        /// <summary>Ручной пулемёт: pivot у пистолетной рукояти, ствол в +Z, короб с лентой снизу.</summary>
        public static (Mesh mesh, Material[] materials) MachineGun() => MachineGun(false);

        /// <summary>Пулемёт; gold — золотой вариант для режима берсерка (та же модель, золото вместо стали и олив).</summary>
        public static (Mesh mesh, Material[] materials) MachineGun(bool gold)
        {
            var steel = gold ? GoldBright : new Color(0.2f, 0.21f, 0.22f);
            var dark = gold ? GoldDark : new Color(0.1f, 0.1f, 0.1f);
            var furniture = gold ? Gold : Olive;
            return new BoxModel()
                .Box(new Vector3(0f, 0.05f, 0.08f), new Vector3(0.07f, 0.09f, 0.34f), steel)            // ствольная коробка
                .Box(new Vector3(0f, 0.065f, 0.43f), new Vector3(0.035f, 0.035f, 0.38f), dark)           // ствол
                .Box(new Vector3(0f, 0.065f, 0.33f), new Vector3(0.06f, 0.06f, 0.16f), steel)            // кожух
                .Box(new Vector3(0f, 0.065f, MachineGunMuzzle - 0.02f), new Vector3(0.05f, 0.05f, 0.04f), dark) // пламегаситель
                .Box(new Vector3(0f, 0.105f, 0.1f), new Vector3(0.045f, 0.03f, 0.18f), dark)             // крышка коробки
                .Box(new Vector3(0f, -0.04f, -0.01f), new Vector3(0.04f, 0.1f, 0.05f), furniture, Quaternion.Euler(-15f, 0f, 0f)) // рукоять
                .Box(new Vector3(0f, 0.03f, -0.2f), new Vector3(0.05f, 0.08f, 0.24f), furniture)             // приклад
                .Box(new Vector3(0f, -0.035f, 0.14f), new Vector3(0.09f, 0.1f, 0.1f), furniture)             // короб с лентой
                .Box(new Vector3(0.025f, 0.02f, 0.52f), new Vector3(0.015f, 0.1f, 0.015f), dark, Quaternion.Euler(0f, 0f, 25f)) // сошки
                .Box(new Vector3(-0.025f, 0.02f, 0.52f), new Vector3(0.015f, 0.1f, 0.015f), dark, Quaternion.Euler(0f, 0f, -25f))
                .Build(gold ? "machinegun_gold" : "machinegun");
        }

        /// <summary>Маяк: корпус-рация с антенной и красной лампой (pivot по центру дна).</summary>
        public static (Mesh mesh, Material[] materials) Beacon()
        {
            var body = new Color(0.18f, 0.19f, 0.17f);
            return new BoxModel()
                .Box(new Vector3(0f, 0.09f, 0f), new Vector3(0.2f, 0.18f, 0.14f), body)
                .Box(new Vector3(0f, 0.13f, 0.072f), new Vector3(0.14f, 0.06f, 0.01f), new Color(0.25f, 0.55f, 0.3f)) // экранчик
                .Box(new Vector3(0.05f, 0.33f, 0f), new Vector3(0.02f, 0.3f, 0.02f), Dark)                            // антенна
                .Box(new Vector3(-0.04f, 0.2f, 0f), new Vector3(0.07f, 0.05f, 0.07f), new Color(1f, 0.15f, 0.1f))     // лампа
                .Build("beacon");
        }

        /// <summary>Бронежилет тёмно-синий (как полоса брони в HUD), лежит плашмя: pivot по центру дна.</summary>
        public static (Mesh mesh, Material[] materials) ArmorVest()
        {
            return new BoxModel()
                .Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.58f), VestNavy)                // плита
                .Box(new Vector3(0f, 0.09f, 0.05f), new Vector3(0.36f, 0.03f, 0.3f), VestNavyDark)        // карман бронепластины
                .Box(new Vector3(0.17f, 0.06f, -0.3f), new Vector3(0.1f, 0.04f, 0.14f), VestNavyDark)     // плечевые лямки
                .Box(new Vector3(-0.17f, 0.06f, -0.3f), new Vector3(0.1f, 0.04f, 0.14f), VestNavyDark)
                .Box(new Vector3(0f, 0.09f, -0.16f), new Vector3(0.3f, 0.03f, 0.06f), new Color(0.85f, 0.85f, 0.85f)) // нашивка
                .Build("armor_vest");
        }

        /// <summary>
        /// Бронежилет на игроке поверх куртки — в координатах тела (как CharacterBody).
        /// Закрывает нашивки куртки, поэтому несёт свои: на кармане спереди и во всю спину.
        /// </summary>
        public static (Mesh mesh, Material[] materials) WornVest(int team)
        {
            var teamColor = RuntimeMaterials.TeamColor(team);
            return new BoxModel()
                .Box(new Vector3(0f, WornVestY, 0f), new Vector3(0.64f, 0.5f, 0.4f), VestNavy)
                .Box(new Vector3(0f, WornVestY - 0.02f, 0.205f), new Vector3(0.44f, 0.3f, 0.02f), VestNavyDark)
                .Box(new Vector3(0.2f, WornVestY + 0.27f, 0f), new Vector3(0.12f, 0.06f, 0.38f), VestNavy)
                .Box(new Vector3(-0.2f, WornVestY + 0.27f, 0f), new Vector3(0.12f, 0.06f, 0.38f), VestNavy)
                // нашивки цвета команды
                .Box(new Vector3(0.12f, WornVestY + 0.06f, 0.221f), new Vector3(0.18f, 0.13f, 0.012f), teamColor)
                .Box(new Vector3(0f, WornVestY, -0.207f), new Vector3(0.36f, 0.26f, 0.014f), teamColor)
                .Build($"worn_vest_{team}");
        }

        public const float WornVestY = 1.25f;
            // ---------- флаг команды ----------

        /// <summary>Высота флага (шест от основания до навершия).</summary>
        public const float FlagHeight = 2.5f;
        /// <summary>Полотнище: длина (от шеста по +X), высота, нижний край по высоте шеста и число вертикальных полос.</summary>
        public const float FlagClothLength = 1.2f, FlagClothHeight = 0.75f, FlagClothBottom = 1.6f;
        public const int FlagClothStrips = 6;

        /// <summary>Шест флага: pivot у основания, вверх по +Y; тёмное дерево, золотое навершие, подставка.</summary>
        public static (Mesh mesh, Material[] materials) FlagPole()
        {
            var wood = new Color(0.3f, 0.21f, 0.13f);
            var gold = new Color(0.95f, 0.78f, 0.25f);
            return new BoxModel()
                .Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.36f, 0.08f, 0.36f), Dark)                   // подставка
                .Box(new Vector3(0f, FlagHeight * 0.5f, 0f), new Vector3(0.09f, FlagHeight, 0.09f), wood)  // шест
                .Box(new Vector3(0f, FlagHeight + 0.06f, 0f), new Vector3(0.16f, 0.12f, 0.16f), gold)      // навершие
                .Build("flag_pole");
        }

        /// <summary>
        /// Одна вертикальная полоса полотнища (pivot у её левого края на высоте низа полотнища): цвет команды
        /// с тёмной полосой внизу и светлой вверху — пиксельный узор. Флаг собирается из FlagClothStrips полос,
        /// каждая колышется отдельно.
        /// </summary>
        public static (Mesh mesh, Material[] materials) FlagStrip(int team)
        {
            var teamColor = RuntimeMaterials.TeamColor(team);
            var dark = Color.Lerp(teamColor, Color.black, 0.35f);
            var light = Color.Lerp(teamColor, Color.white, 0.35f);
            float w = FlagClothLength / FlagClothStrips, h = FlagClothHeight, band = h * 0.18f;
            return new BoxModel()
                .Box(new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w, h - 2f * band, 0.04f), teamColor)
                .Box(new Vector3(w * 0.5f, band * 0.5f, 0f), new Vector3(w, band, 0.04f), dark)
                .Box(new Vector3(w * 0.5f, h - band * 0.5f, 0f), new Vector3(w, band, 0.04f), light)
                .Build($"flag_strip_{team}");
        }
            // ---------- берсерк: «накачанная» версия солдата ----------

        static readonly Color Gold = new Color(0.95f, 0.74f, 0.18f);
        static readonly Color GoldBright = new Color(1f, 0.86f, 0.35f);
        static readonly Color GoldDark = new Color(0.66f, 0.46f, 0.1f);

        /// <summary>Огромная грудная клетка поверх куртки (в координатах тела): камуфляж, рельеф грудных мышц, пряжка ремня.</summary>
        public static (Mesh mesh, Material[] materials) BuffChest(int team)
        {
            return new BoxModel()
                .PatternBox(new Vector3(0f, 1.27f, 0.02f), new Vector3(0.96f, 0.58f, 0.52f), CamoTile, p => Camo(p, 3.3f))
                .PatternBox(new Vector3(0f, 1.0f, 0f), new Vector3(0.74f, 0.2f, 0.42f), CamoTile, p => Camo(p, 7.9f))   // пресс
                .Box(new Vector3(-0.2f, 1.33f, 0.285f), new Vector3(0.34f, 0.22f, 0.04f), CamoGreen)                  // грудные мышцы
                .Box(new Vector3(0.2f, 1.33f, 0.285f), new Vector3(0.34f, 0.22f, 0.04f), CamoGreen)
                .Box(new Vector3(0f, 1.3f, 0.29f), new Vector3(0.025f, 0.3f, 0.03f), CamoBrown)                      // ложбинка
                .Build($"buff_chest_{team}");
        }

        /// <summary>Огромная фуражка (pivot — низ, как у каски): околыш цвета команды, широкая тулья, козырёк, золотая кокарда.</summary>
        public static (Mesh mesh, Material[] materials) OfficerCap(int team)
        {
            var cloth = new Color(0.22f, 0.27f, 0.16f);
            var m = new BoxModel();
            VoxelDisc(m, 0f, 0.13f, 0.27f, 8, RuntimeMaterials.TeamColor(team));        // околыш
            VoxelDisc(m, 0.13f, 0.2f, 0.33f, 10, cloth);                                 // тулья расширяется
            VoxelDisc(m, 0.2f, 0.29f, 0.42f, 12, cloth);
            VoxelDisc(m, 0.29f, 0.33f, 0.4f, 12, Color.Lerp(cloth, Color.black, 0.25f)); // верх
            m.Box(new Vector3(0f, 0.02f, 0.33f), new Vector3(0.46f, 0.04f, 0.2f), Dark, Quaternion.Euler(14f, 0f, 0f)); // козырёк
            m.Box(new Vector3(0f, 0.16f, 0.33f), new Vector3(0.12f, 0.1f, 0.03f), Gold);                                // кокарда
            m.Box(new Vector3(0f, 0.11f, 0.285f), new Vector3(0.4f, 0.025f, 0.02f), Gold);                              // шнур
            return m.Build($"officer_cap_{team}");
        }

        /// <summary>Тёмные очки на лице (в координатах тела).</summary>
        public static (Mesh mesh, Material[] materials) Sunglasses()
        {
            var lens = new Color(0.05f, 0.05f, 0.08f);
            return new BoxModel()
                .Box(new Vector3(-0.1f, 1.78f, 0.215f), new Vector3(0.14f, 0.08f, 0.03f), lens)
                .Box(new Vector3(0.1f, 1.78f, 0.215f), new Vector3(0.14f, 0.08f, 0.03f), lens)
                .Box(new Vector3(0f, 1.8f, 0.215f), new Vector3(0.42f, 0.025f, 0.03f), lens)   // оправа
                .Box(new Vector3(-0.07f, 1.795f, 0.235f), new Vector3(0.04f, 0.015f, 0.01f), Color.white) // блик
                .Build("sunglasses");
        }

        /// <summary>Сигара в углу рта (в координатах тела); тлеющий кончик — отдельная точка для дыма: CigarTip.</summary>
        public static readonly Vector3 CigarTip = new Vector3(0.17f, 1.6f, 0.4f);

        public static (Mesh mesh, Material[] materials) Cigar()
        {
            var leaf = new Color(0.42f, 0.26f, 0.12f);
            var ring = new Color(0.8f, 0.15f, 0.12f);
            var rot = Quaternion.Euler(12f, 25f, 0f);
            Vector3 mid = new Vector3(0.12f, 1.62f, 0.3f);
            return new BoxModel()
                .Box(mid, new Vector3(0.045f, 0.045f, 0.22f), leaf, rot)
                .Box(mid + rot * new Vector3(0f, 0f, -0.06f), new Vector3(0.05f, 0.05f, 0.03f), ring, rot)
                .Box(mid + rot * new Vector3(0f, 0f, 0.11f), new Vector3(0.05f, 0.05f, 0.025f), new Color(1f, 0.45f, 0.1f), rot) // уголёк
                .Build("cigar");
        }

        /// <summary>Погоны с золотыми звёздами на плечах (в координатах тела).</summary>
        public static (Mesh mesh, Material[] materials) Epaulettes()
        {
            var m = new BoxModel();
            foreach (float x in new[] { -0.5f, 0.5f })
            {
                // толстая погонная планка над накачанным плечом, видна и спереди
                m.Box(new Vector3(x, 1.62f, 0f), new Vector3(0.32f, 0.12f, 0.32f), GoldDark);
                m.Box(new Vector3(x, 1.69f, 0f), new Vector3(0.28f, 0.03f, 0.27f), Gold);
                m.Box(new Vector3(x, 1.6f, 0.165f), new Vector3(0.3f, 0.04f, 0.02f), GoldBright); // бахрома спереди
                // звёзды — пиксельные «плюсики»
                foreach (float z in new[] { -0.07f, 0.07f })
                {
                    m.Box(new Vector3(x, 1.71f, z), new Vector3(0.08f, 0.015f, 0.03f), GoldBright);
                    m.Box(new Vector3(x, 1.71f, z), new Vector3(0.03f, 0.015f, 0.08f), GoldBright);
                }
            }
            return m.Build("epaulettes");
        }
            // ---------- снайперская винтовка ----------

        /// <summary>Конец ствола снайперской винтовки по +Z от рукояти.</summary>
        public const float SniperMuzzle = 0.95f;

        /// <summary>Снайперская винтовка: pivot у пистолетной рукояти, ствол в +Z, оптический прицел сверху.</summary>
        public static (Mesh mesh, Material[] materials) SniperRifle()
        {
            var body = new Color(0.22f, 0.25f, 0.16f);   // тёмно-оливковая ложа
            var steel = new Color(0.15f, 0.15f, 0.16f);
            var lens = new Color(0.35f, 0.55f, 0.75f);
            return new BoxModel()
                .Box(new Vector3(0f, 0.03f, -0.02f), new Vector3(0.06f, 0.08f, 0.5f), body)                  // ложа
                .Box(new Vector3(0f, 0.01f, -0.36f), new Vector3(0.055f, 0.13f, 0.22f), body)                // приклад
                .Box(new Vector3(0f, 0.08f, -0.45f), new Vector3(0.05f, 0.04f, 0.1f), steel)                 // затыльник щеки
                .Box(new Vector3(0f, -0.045f, -0.01f), new Vector3(0.04f, 0.1f, 0.05f), body, Quaternion.Euler(-15f, 0f, 0f)) // рукоять
                .Box(new Vector3(0f, 0.065f, 0.5f), new Vector3(0.03f, 0.03f, 0.9f), steel)                  // длинный ствол
                .Box(new Vector3(0f, 0.065f, SniperMuzzle - 0.02f), new Vector3(0.045f, 0.045f, 0.05f), steel) // дульный тормоз
                .Box(new Vector3(0f, 0.08f, 0.05f), new Vector3(0.05f, 0.05f, 0.16f), steel)                 // затвор
                .Box(new Vector3(0.04f, 0.09f, 0.0f), new Vector3(0.05f, 0.015f, 0.015f), steel)             // рукоять затвора
                .Box(new Vector3(0f, 0.15f, 0.06f), new Vector3(0.05f, 0.05f, 0.3f), steel)                  // прицел
                .Box(new Vector3(0f, 0.15f, 0.22f), new Vector3(0.065f, 0.065f, 0.05f), steel)               // объектив
                .Box(new Vector3(0f, 0.15f, 0.246f), new Vector3(0.05f, 0.05f, 0.005f), lens)                // линза
                .Box(new Vector3(0f, 0.115f, 0.06f), new Vector3(0.025f, 0.03f, 0.05f), steel)               // крепление
                .Build("sniper_rifle");
        }

        /// <summary>Центр головы игрока (для блика оптики): в координатах тела.</summary>
        public static readonly Vector3 SniperGlintPoint = new Vector3(0.05f, 1.75f, 0.1f);
            // ---------- базука ----------

        /// <summary>Конец трубы базуки по +Z от рукояти; ось трубы — на высоте BazookaAxisY.</summary>
        public const float BazookaMuzzle = 0.6f, BazookaAxisY = 0.19f;
        const float BazookaPixel = 0.035f;

        /// <summary>Базука: оливковая труба с раструбами, pivot у пистолетной рукояти под трубой, прицельная рамка слева.</summary>
        public static (Mesh mesh, Material[] materials) Bazooka()
        {
            var olive = new Color(0.3f, 0.36f, 0.2f);
            var oliveDark = new Color(0.22f, 0.27f, 0.15f);
            var steel = new Color(0.15f, 0.15f, 0.16f);
            var warn = new Color(0.85f, 0.7f, 0.15f);
            var m = new BoxModel();
            // толстая труба (радиус 0,16): ось выше рукояти, чтобы рукоять торчала снизу
            VoxelTube(m, -0.55f, BazookaMuzzle, 0.16f, olive, BazookaPixel, BazookaAxisY);                          // труба
            VoxelTube(m, BazookaMuzzle - 0.08f, BazookaMuzzle, 0.19f, steel, BazookaPixel, BazookaAxisY);          // раструб спереди
            VoxelTube(m, -0.55f, -0.45f, 0.2f, steel, BazookaPixel, BazookaAxisY);                                 // раструб сзади
            VoxelTube(m, 0.3f, 0.34f, 0.17f, oliveDark, BazookaPixel, BazookaAxisY);                               // хомут
            VoxelTube(m, -0.2f, -0.16f, 0.17f, warn, BazookaPixel, BazookaAxisY);                                  // жёлтая полоса
            m.Box(new Vector3(0f, -0.03f, 0.0f), new Vector3(0.04f, 0.12f, 0.05f), steel, Quaternion.Euler(-15f, 0f, 0f)); // рукоять
            m.Box(new Vector3(0f, 0.03f, 0.04f), new Vector3(0.035f, 0.05f, 0.07f), steel);                       // спусковая коробка
            m.Box(new Vector3(0f, 0.0f, 0.26f), new Vector3(0.035f, 0.1f, 0.04f), steel, Quaternion.Euler(-10f, 0f, 0f)); // передняя ручка
            m.Box(new Vector3(0f, 0.03f, -0.26f), new Vector3(0.08f, 0.05f, 0.18f), oliveDark);                   // наплечник
            m.Box(new Vector3(-0.18f, 0.3f, 0.16f), new Vector3(0.012f, 0.07f, 0.012f), steel);                    // прицельная рамка
            m.Box(new Vector3(-0.18f, 0.34f, 0.16f), new Vector3(0.012f, 0.012f, 0.05f), steel);
            m.Box(new Vector3(-0.15f, 0.26f, 0.16f), new Vector3(0.05f, 0.012f, 0.012f), steel);
            return m.Build("bazooka");
        }

        /// <summary>Снаряд базуки вдоль +Z: оливковый корпус, ступенчатая боеголовка, четыре стабилизатора.</summary>
        public static (Mesh mesh, Material[] materials) BazookaShell()
        {
            var olive = new Color(0.3f, 0.36f, 0.2f);
            var warn = new Color(0.85f, 0.7f, 0.15f);
            var m = new BoxModel();
            VoxelTube(m, -0.22f, 0.08f, 0.06f, olive, BazookaPixel, 0f);
            VoxelTube(m, 0.08f, 0.16f, 0.075f, olive, BazookaPixel, 0f);
            VoxelTube(m, 0.16f, 0.22f, 0.055f, warn, BazookaPixel, 0f);
            VoxelTube(m, 0.22f, 0.27f, 0.035f, Dark, BazookaPixel, 0f);
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, 0f, i * 90f + 45f);
                m.Box(rot * new Vector3(0f, 0.07f, -0.18f), new Vector3(0.012f, 0.07f, 0.09f), Dark, rot);
            }
            return m.Build("bazooka_shell");
        }

        // ---------- самолёт сброса припасов ----------

        /// <summary>Где на крыле стоят винты (для Propeller): ±X, высота, вынос вперёд по +Z.</summary>
        public static readonly Vector3[] PlanePropellers = { new Vector3(-3.4f, 0.6f, 2.6f), new Vector3(3.4f, 0.6f, 2.6f) };

        /// <summary>
        /// Транспортный самолёт вдоль +Z, общий для обеих команд: серо-оливковый фюзеляж, высокое крыло,
        /// два мотора, хвост с килем. Винты — отдельный меш (Propeller), их крутит AirDropPlane.
        /// </summary>
        public static (Mesh mesh, Material[] materials) CargoPlane()
        {
            var body = new Color(0.45f, 0.48f, 0.42f);
            var bodyDark = new Color(0.34f, 0.37f, 0.32f);
            var glass = new Color(0.2f, 0.32f, 0.4f);
            var m = new BoxModel();
            m.Box(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 9f), body);                         // фюзеляж
            m.Box(new Vector3(0f, -0.1f, 5.1f), new Vector3(1.7f, 1.7f, 1.2f), body);              // нос
            m.Box(new Vector3(0f, -0.2f, 6f), new Vector3(1.2f, 1.2f, 0.6f), bodyDark);            // кончик носа
            m.Box(new Vector3(0f, 0.55f, 5.55f), new Vector3(1.3f, 0.45f, 0.4f), glass);           // кабина
            m.Box(new Vector3(0f, 0.2f, -5.3f), new Vector3(1.5f, 1.5f, 1.6f), body);              // хвостовая часть
            m.Box(new Vector3(0f, 0.5f, -6.7f), new Vector3(0.9f, 0.9f, 1.4f), body);
            m.Box(new Vector3(0f, -1.05f, -2.5f), new Vector3(1.6f, 0.1f, 3f), bodyDark);          // грузовой люк
            m.Box(new Vector3(0f, 1.1f, 0.8f), new Vector3(15f, 0.3f, 2.2f), body);                // крыло
            m.Box(new Vector3(0f, 1.1f, 0.8f), new Vector3(15.1f, 0.18f, 0.4f), bodyDark);         // полоса по крылу
            m.Box(new Vector3(0f, 0.8f, -7f), new Vector3(5.6f, 0.2f, 1.3f), body);                // стабилизатор
            m.Box(new Vector3(0f, 2f, -6.9f), new Vector3(0.2f, 2.4f, 1.5f), body);                // киль
            m.Box(new Vector3(0f, 2.9f, -6.9f), new Vector3(0.24f, 0.5f, 1.5f), bodyDark);
            foreach (var p in PlanePropellers)
            {
                m.Box(new Vector3(p.x, p.y, 1.1f), new Vector3(0.9f, 0.9f, 2.6f), bodyDark); // мотор под крылом
                m.Box(new Vector3(p.x, p.y, p.z - 0.15f), new Vector3(0.4f, 0.4f, 0.3f), Dark); // кок винта
            }
            return m.Build("cargo_plane");
        }

        /// <summary>Двухлопастный винт в плоскости XY, ось — Z.</summary>
        public static (Mesh mesh, Material[] materials) Propeller()
        {
            var m = new BoxModel();
            m.Box(Vector3.zero, new Vector3(2.2f, 0.22f, 0.08f), Dark);
            return m.Build("propeller");
        }
    }
}
