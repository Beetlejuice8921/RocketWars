using System.Text;

namespace RocketWars.Core
{
    /// <summary>
    /// Фильтр ников: мат и оскорбления не должны попасть на экран (на стриме за такой ник банят стримера).
    /// Ник приводится к двум видам — кириллице и латинице (похожие буквы, цифры-подмены, без пробелов, точек
    /// и повторов) — и ищутся корни из списков. Сервер заменяет плохой ник на нейтральный (NetworkBootstrap.Approve).
    /// </summary>
    public static class NameFilter
    {
        // корни после нормализации (ё → е, повторы схлопнуты); короткие корни подобраны без ложных срабатываний
        static readonly string[] Cyrillic =
        {
            "хуй", "хуе", "хуя", "хую", "хуи", "пизд", "пезд", "ебат", "ебан", "ебал", "ебло", "ебну", "ебуч", "заеб",
            "уеб", "выеб", "съеб", "долбое", "бля", "сука", "суки", "сучк", "мудак", "мудил", "пидор", "пидар", "педик",
            "педер", "гандон", "гондон", "залуп", "шлюх", "дроч", "манду", "жопа", "говн", "срак", "нахер", "похер",
            "жиды", "жидо", "жидя", "чурк", "нигер", "негр", "хохол", "москал", "гитлер", "нацис", "фашис", "зига",
        };

        static readonly string[] Latin =
        {
            "fuck", "fck", "shit", "cunt", "bitch", "whore", "slut", "dick", "pussy", "penis", "vagina",
            "asshole", "nigg", "niga", "faggot", "fagot", "retard", "hitler", "nazi", "heil", "porn",
            "khui", "pizd", "pisd", "ebat", "eban", "blya", "blia", "suka", "pidor", "pidar",
            "gandon", "zalup", "mudak", "jopa", "zhopa", "chmo",
        };

        // ник сравнивается без повторов подряд — корни тоже («nigg» → «nig», «asshole» → «ashole»)
        static readonly string[] CyrillicRoots = System.Array.ConvertAll(Cyrillic, Collapse);
        static readonly string[] LatinRoots = System.Array.ConvertAll(Latin, Collapse);

        static string Collapse(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (sb.Length == 0 || sb[sb.Length - 1] != c)
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>В нике нашлось запрещённое слово.</summary>
        public static bool IsBad(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            string cyr = Normalize(name, true), lat = Normalize(name, false);
            foreach (var root in CyrillicRoots)
                if (cyr.Contains(root))
                    return true;
            foreach (var root in LatinRoots)
                if (lat.Contains(root))
                    return true;
            // числа не нормализуются (схлопывание повторов испортило бы «88»)
            string digits = new string(System.Array.FindAll(name.ToCharArray(), char.IsDigit));
            return digits.Contains("1488");
        }

        /// <summary>
        /// Только буквы (и цифры для латиницы), строчные, без повторов подряд. toCyrillic — похожие латинские
        /// буквы и цифры становятся кириллицей («xyй», «3ал», «6ля»), иначе кириллица — латиницей («сука» → «cyka»).
        /// </summary>
        static string Normalize(string s, bool toCyrillic)
        {
            var sb = new StringBuilder(s.Length);
            char last = '\0';
            foreach (char raw in s.ToLowerInvariant())
            {
                char c = toCyrillic ? ToCyrillic(raw) : ToLatin(raw);
                if (c == '\0' || c == last)
                    continue;
                sb.Append(c);
                last = c;
            }
            return sb.ToString();
        }

        static char ToCyrillic(char c)
        {
            switch (c)
            {
                case 'a': case '@': case '4': return 'а';
                case 'b': case '6': return 'б';
                case 'c': return 'с';
                case 'e': case 'ё': return 'е';
                case '3': return 'з';
                case 'k': return 'к';
                case 'm': return 'м';
                case 'h': return 'н';
                case 'o': case '0': return 'о';
                case 'p': return 'р';
                case 't': return 'т';
                case 'x': return 'х';
                case 'y': case 'u': return 'у';
                case 'i': case '1': case '!': return 'и';
                case 'n': return 'п';
                case 'r': return 'г';
                case 'z': return 'з';
                case 'd': return 'д';
                case 's': case '$': return 'с';
                case 'l': return 'л';
                case 'j': return 'й';
                case 'g': return 'г';
                case 'v': return 'в';
                case 'w': return 'ш';
                case 'f': return 'ф';
            }
            return c >= 'а' && c <= 'я' ? c : '\0';
        }

        static char ToLatin(char c)
        {
            switch (c)
            {
                case 'а': case '@': return 'a';
                case 'б': return 'b';
                case 'в': return 'v';
                case 'г': return 'g';
                case 'д': return 'd';
                case 'е': case 'ё': case 'э': return 'e';
                case 'ж': return 'j';
                case 'з': return 'z';
                case 'и': case 'ы': case '!': return 'i';
                case 'й': return 'y';
                case 'к': return 'k';
                case 'л': return 'l';
                case 'м': return 'm';
                case 'н': return 'n';
                case 'о': return 'o';
                case 'п': return 'p';
                case 'р': return 'r';
                case 'с': case '$': return 's';
                case 'т': return 't';
                case 'у': return 'u';
                case 'ф': return 'f';
                case 'х': return 'h';
                case 'ц': return 'c';
                case 'ч': return 'c';
                case 'ш': case 'щ': return 's';
                case 'ь': case 'ъ': return '\0';
                case 'ю': return 'u';
                case 'я': return 'a';
                case '0': return 'o';
                case '3': return 'e';
                case '5': return 's';
                case '7': return 't';
            }
            return c >= 'a' && c <= 'z' || c >= '1' && c <= '9' ? c : '\0';
        }
    }
}
