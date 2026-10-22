using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// QR-код без сторонних библиотек (ссылка на комнату для зрителей стрима): байтовый режим, коррекция M,
    /// версии 1–10 (до ~200 байт), маска — с наименьшим штрафом по стандарту. Текстура — по пикселю на модуль
    /// с рамкой «тихой зоны», точечная фильтрация (её растягивает интерфейс, края остаются чёткими).
    /// </summary>
    public static class QrCode
    {
        const int MaxVersion = 10;
        const int QuietZone = 2; // по стандарту 4, но вокруг и так светлая подложка панели

        // коррекция M: ECC-кодовых слов в блоке и число блоков по версиям (индекс 0 не используется)
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] NumBlocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
        const int FormatEccBits = 0; // M

        /// <summary>Модули кода (true — тёмный); null — текст не влезает в версию 10.</summary>
        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            for (int version = 1; version <= MaxVersion; version++)
            {
                int capacityBits = DataCodewords(version) * 8;
                int countBits = version < 10 ? 8 : 16;
                if (4 + countBits + data.Length * 8 <= capacityBits)
                    return Build(version, data, countBits);
            }
            return null;
        }

        /// <summary>Текстура кода: чёрные модули на белом, по пикселю на модуль, с тихой зоной.</summary>
        public static Texture2D ToTexture(bool[,] modules)
        {
            int n = modules.GetLength(0), size = n + QuietZone * 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "QR"
            };
            var pixels = new Color32[size * size];
            var dark = new Color32(0, 0, 0, 255);
            var light = new Color32(255, 255, 255, 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int mx = x - QuietZone, my = y - QuietZone;
                    bool on = mx >= 0 && my >= 0 && mx < n && my < n && modules[my, mx];
                    // строки текстуры идут снизу вверх
                    pixels[(size - 1 - y) * size + x] = on ? dark : light;
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // ---------- сборка ----------

        static bool[,] Build(int version, byte[] data, int countBits)
        {
            int size = version * 4 + 17;
            var modules = new bool[size, size];
            var function = new bool[size, size];
            DrawFunctionPatterns(version, modules, function);

            byte[] codewords = AddEccAndInterleave(version, EncodeData(version, data, countBits));
            DrawCodewords(codewords, modules, function);

            // маска с наименьшим штрафом (сканеры читают такие коды увереннее)
            int bestMask = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask, modules, function);
                DrawFormatBits(mask, modules, function);
                int penalty = Penalty(modules);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                }
                ApplyMask(mask, modules, function); // XOR — повторное наложение снимает маску
            }
            ApplyMask(bestMask, modules, function);
            DrawFormatBits(bestMask, modules, function);
            return modules;
        }

        static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int align = version / 7 + 2;
                result -= (25 * align - 10) * align - 55;
                if (version >= 7)
                    result -= 36;
            }
            return result;
        }

        static int DataCodewords(int version) => RawDataModules(version) / 8 - EccPerBlock[version] * NumBlocks[version];

        static byte[] EncodeData(int version, byte[] data, int countBits)
        {
            var bits = new List<bool>();
            void Append(int value, int length)
            {
                for (int i = length - 1; i >= 0; i--)
                    bits.Add(((value >> i) & 1) != 0);
            }

            Append(0b0100, 4); // байтовый режим
            Append(data.Length, countBits);
            foreach (byte b in data)
                Append(b, 8);

            int capacity = DataCodewords(version) * 8;
            Append(0, Math.Min(4, capacity - bits.Count)); // терминатор
            Append(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
                Append(pad, 8);

            var result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i])
                    result[i >> 3] |= (byte)(0x80 >> (i & 7));
            return result;
        }

        static byte[] AddEccAndInterleave(int version, byte[] data)
        {
            int numBlocks = NumBlocks[version], eccLen = EccPerBlock[version];
            int raw = RawDataModules(version) / 8;
            int numShort = numBlocks - raw % numBlocks;
            int shortLen = raw / numBlocks;

            byte[] divisor = ReedSolomonDivisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                byte[] ecc = ReedSolomonRemainder(dat, divisor);
                // короткие блоки — с пустым местом, чтобы перемежать по одному индексу
                var block = new byte[shortLen + 1];
                Array.Copy(dat, 0, block, 0, datLen);
                Array.Copy(ecc, 0, block, shortLen + 1 - eccLen, eccLen);
                blocks.Add(block);
            }

            var result = new List<byte>(raw);
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort)
                        result.Add(blocks[j][i]);
            return result.ToArray();
        }

        static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree)
                        result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++)
                    result[i] ^= Multiply(divisor[i], factor);
            }
            return result;
        }

        static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        // ---------- служебные узоры ----------

        static void Set(bool[,] modules, bool[,] function, int x, int y, bool dark)
        {
            modules[y, x] = dark;
            function[y, x] = true;
        }

        static void DrawFunctionPatterns(int version, bool[,] modules, bool[,] function)
        {
            int size = modules.GetLength(0);
            for (int i = 0; i < size; i++)
            {
                Set(modules, function, 6, i, i % 2 == 0);
                Set(modules, function, i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3, modules, function);
            DrawFinder(size - 4, 3, modules, function);
            DrawFinder(3, size - 4, modules, function);

            int[] align = AlignmentPositions(version, size);
            int n = align.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)
                        continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(modules, function, align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }

            DrawFormatBits(0, modules, function); // резервирует место; настоящие биты — после выбора маски
            DrawVersion(version, modules, function);
        }

        static void DrawFinder(int cx, int cy, bool[,] modules, bool[,] function)
        {
            int size = modules.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size)
                        continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(modules, function, x, y, dist != 2 && dist != 4);
                }
        }

        static int[] AlignmentPositions(int version, int size)
        {
            if (version == 1)
                return Array.Empty<int>();
            int count = version / 7 + 2;
            int step = (version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = size - 7; i >= 1; i--, pos -= step)
                result[i] = pos;
            return result;
        }

        static void DrawFormatBits(int mask, bool[,] modules, bool[,] function)
        {
            int size = modules.GetLength(0);
            int data = FormatEccBits << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;

            for (int i = 0; i <= 5; i++)
                Set(modules, function, 8, i, Bit(i));
            Set(modules, function, 8, 7, Bit(6));
            Set(modules, function, 8, 8, Bit(7));
            Set(modules, function, 7, 8, Bit(8));
            for (int i = 9; i < 15; i++)
                Set(modules, function, 14 - i, 8, Bit(i));

            for (int i = 0; i < 8; i++)
                Set(modules, function, size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++)
                Set(modules, function, 8, size - 15 + i, Bit(i));
            Set(modules, function, 8, size - 8, true); // всегда тёмный модуль
        }

        static void DrawVersion(int version, bool[,] modules, bool[,] function)
        {
            if (version < 7)
                return;
            int size = modules.GetLength(0);
            int rem = version;
            for (int i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            long bits = (long)version << 12 | (long)rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(modules, function, a, b, bit);
                Set(modules, function, b, a, bit);
            }
        }

        // ---------- данные и маска ----------

        static void DrawCodewords(byte[] data, bool[,] modules, bool[,] function)
        {
            int size = modules.GetLength(0);
            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right = 5; // колонка таймингового узора
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (function[y, x] || i >= data.Length * 8)
                            continue;
                        modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
            }
        }

        static void ApplyMask(int mask, bool[,] modules, bool[,] function)
        {
            int size = modules.GetLength(0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert && !function[y, x])
                        modules[y, x] = !modules[y, x];
                }
        }

        /// <summary>Штраф маски по стандарту: длинные полосы, квадраты 2×2, ложные «глазки», перекос тёмного/светлого.</summary>
        static int Penalty(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int penalty = 0, dark = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 0;
                    bool prev = false;
                    int pattern = 0; // последние 11 модулей линии
                    for (int b = 0; b < size; b++)
                    {
                        bool m = pass == 0 ? modules[a, b] : modules[b, a];
                        if (b > 0 && m == prev)
                        {
                            run++;
                            if (run == 5) penalty += 3;
                            else if (run > 5) penalty++;
                        }
                        else
                        {
                            run = 1;
                        }
                        prev = m;
                        pattern = ((pattern << 1) | (m ? 1 : 0)) & 0x7FF;
                        if (b >= 10 && (pattern == 0b10111010000 || pattern == 0b00001011101))
                            penalty += 40;
                    }
                }

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (modules[y, x])
                        dark++;
                    if (x < size - 1 && y < size - 1)
                    {
                        bool c = modules[y, x];
                        if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1])
                            penalty += 3;
                    }
                }

            int total = size * size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return penalty + Math.Max(0, k) * 10;
        }
    }
}
