using RocketWars.Core;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Окно «Как играть?» из главного меню: правила и механики короткими разделами (текст — Resources/Loc/strings.txt,
    /// клавиши в нём — токены {use}, {fire}, … и подставляются по текущим настройкам управления).
    /// </summary>
    public class HowToPlayView
    {
        static readonly string[,] Sections =
        {
            { "ЦЕЛЬ", "Две команды сражаются на двух островах. Раунд выигрывает команда, которая уложит всех врагов. Матч идёт до 4 побед, каждый раунд — новые острова." },
            { "РАКЕТНЫЕ ШАХТЫ", "Подойдите к пульту у своей шахты и нажимайте {fire} по кнопкам на экране: X останавливает одну линию, Y — другую, их пересечение — цель. Затем жмите «ПУСК». Ракета выбивает кубики из острова: враги проваливаются вниз, а шахта без опоры рушится. Перезарядка шахты — 60 с." },
            { "ПОЛЁТ НА РАКЕТЕ", "Пока воет сирена, удерживайте {use} у ракеты, чтобы улететь на ней к врагу (до 4 человек). {trick} — трюк, отпустите {use} — спрыгнуть с парашютом, {jump} — отстегнуть парашют." },
            { "ЯЩИКИ", "Удерживайте {use}, чтобы открыть ящик: пулемёт, снайперка ({aim} — прицел), базука (осторожно, задевает своих), бронежилет, аптечки или маяк, который показывает вражеские шахты на пульте. Через минуту после начала раунда самолёт сбрасывает новые ящики." },
            { "РАНЕНИЯ", "Потеряв всё здоровье, вы не погибаете, а ложитесь. Союзник поднимет вас, удерживая {use} рядом. Падение с острова отнимает здоровье. {aim} — бинокль." },
            { "ФЛАГ", "У дальнего края каждого острова стоит флаг. Заберите вражеский флаг ({use}) и донесите до своего острова — на 30 с станете берсерком с двумя пулемётами. Если несущего уложат, флаг останется лежать там, где он упал." },
        };

        readonly VisualElement _screen;

        public bool IsOpen => Ui.IsShown(_screen);

        public HowToPlayView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel", "howto-panel");
            Ui.Text(panel, Loc.T("КАК ИГРАТЬ"), "title", "shadow");

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("list");
            list.AddToClassList("howto-list");
            panel.Add(list);
            for (int i = 0; i < Sections.GetLength(0); i++)
            {
                Ui.Text(list, Loc.T(Sections[i, 0]), "section", "howto-heading");
                Ui.Text(list, Loc.T(Sections[i, 1]), "howto-text");
            }

            Ui.Btn(panel, Loc.T(TouchInput.Enabled ? "Закрыть" : "Закрыть (Esc)"), Close, "btn-primary");
            Ui.Show(_screen, false);
        }

        public void Open()
        {
            Ui.Show(_screen, true);
            _screen.Q<ScrollView>().scrollOffset = UnityEngine.Vector2.zero;
        }

        public void Close() => Ui.Show(_screen, false);
    }
}
