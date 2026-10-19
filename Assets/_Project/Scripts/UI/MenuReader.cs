using RocketWars.Core;
using UnityEngine;

namespace RocketWars.UI
{
    /// <summary>
    /// Солдат на переднем плане сцены меню: сидит на траве, ноги вытянуты вперёд, держит раскрытую книгу у груди
    /// и время от времени перелистывает страницу. Бой за спиной его не касается.
    /// Рядом, прислонившись головой к его боку, валяется красный солдат (MenuLounger).
    /// </summary>
    public class MenuReader
    {
        const int Team = 1;
        /// <summary>Чуть отвёрнут от камеры к середине экрана (градусы).</summary>
        public const float Turn = -22f;
        const float PageTurnTime = 0.7f;
        const float PageIntervalMin = 4f, PageIntervalMax = 7f;
        /// <summary>Разворот книги: половинки раскрыты на ±HalfAngle от плоскости, страница листается между ними.</summary>
        const float HalfAngle = 22f;

        static readonly Color Cover = new Color32(122, 36, 32, 255);
        static readonly Color CoverBand = new Color32(214, 170, 64, 255);
        static readonly Color Paper = new Color32(238, 230, 206, 255);
        static readonly Color PaperEdge = new Color32(214, 204, 178, 255);

        /// <summary>Цветы у ног (в координатах солдата, +Z — вперёд, к камере): место, высота стебля, цвет лепестков.</summary>
        static readonly (Vector3 pos, float height, Color petals)[] Flowers =
        {
            (new Vector3(-0.22f, 0f, 1.3f), 0.36f, new Color32(226, 58, 52, 255)),
            (new Vector3(0.12f, 0f, 1.5f), 0.28f, new Color32(246, 206, 60, 255)),
            (new Vector3(0.45f, 0f, 1.25f), 0.4f, new Color32(150, 112, 230, 255)),
        };
        static readonly Color Stem = new Color32(70, 140, 52, 255);
        static readonly Color FlowerHeart = new Color32(250, 236, 140, 255);

        readonly Transform _root, _body, _helmet, _page;
        readonly Transform[] _flowers = new Transform[Flowers.Length];
        readonly MenuLounger _lounger;
        float _pageTimer = 2f, _pageT = -1f, _time;

        public MenuReader(Transform parent)
        {
            _root = new GameObject("Reader").transform;
            _root.SetParent(parent, false);
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(_root, false);
            _body = MenuSoldier.Build(pivot, Team, out var armL, out var armR, out var legL, out var legR, out _helmet);

            // сидит: таз на земле, ноги вытянуты вперёд и чуть врозь, корпус склонился к книге
            _body.localPosition = new Vector3(0f, -0.7f, 0f);
            _body.localRotation = Quaternion.Euler(8f, 0f, 0f);
            legL.localRotation = Quaternion.Euler(-82f, -16f, 0f);
            legR.localRotation = Quaternion.Euler(-82f, 16f, 0f);
            armL.localRotation = Quaternion.Euler(-58f, 24f, 0f);
            armR.localRotation = Quaternion.Euler(-58f, -24f, 0f);
            _helmet.localRotation = Quaternion.Euler(12f, 0f, 0f);

            // книга в руках, страницами к лицу
            var book = new GameObject("Book").transform;
            book.SetParent(_body, false);
            book.localPosition = new Vector3(0f, 1.1f, 0.52f);
            book.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.55f, -0.45f), Vector3.up);
            BuildHalf(book, -1f);
            BuildHalf(book, 1f);
            var (pageMesh, pageMats) = new BoxModel()
                .Box(new Vector3(0.095f, 0f, 0.002f), new Vector3(0.19f, 0.25f, 0.004f), Paper)
                .Build("menu_book_page");
            _page = new GameObject("PageHinge").transform;
            _page.SetParent(book, false);
            _page.localPosition = new Vector3(0f, 0f, 0.03f);
            BoxModel.Spawn(pageMesh, pageMats, _page, Vector3.zero, "Page");
            _page.gameObject.SetActive(false);

            for (int i = 0; i < Flowers.Length; i++)
            {
                var (pos, height, petals) = Flowers[i];
                var (mesh, mats) = Flower(height, petals);
                _flowers[i] = BoxModel.Spawn(mesh, mats, _root, pos, "Flower").transform;
                _flowers[i].localRotation = Quaternion.Euler(0f, i * 37f, 0f);
            }

            _lounger = new MenuLounger(_root);
        }

        /// <summary>Воксельный цветок: стебель с листиком, четыре лепестка крестом вокруг жёлтой серединки (pivot — у земли).</summary>
        public static (Mesh mesh, Material[] materials) Flower(float height, Color petals)
        {
            const float p = 0.06f; // лепесток
            Vector3 top = new Vector3(0f, height, 0f);
            return new BoxModel()
                .Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(0.025f, height, 0.025f), Stem)
                .Box(new Vector3(0.04f, height * 0.4f, 0f), new Vector3(0.06f, 0.015f, 0.03f), Stem, Quaternion.Euler(0f, 0f, 25f))
                .Box(top, new Vector3(0.045f, 0.035f, 0.045f), FlowerHeart)
                .Box(top + new Vector3(p, 0f, 0f), new Vector3(p, 0.025f, p), petals)
                .Box(top + new Vector3(-p, 0f, 0f), new Vector3(p, 0.025f, p), petals)
                .Box(top + new Vector3(0f, 0f, p), new Vector3(p, 0.025f, p), petals)
                .Box(top + new Vector3(0f, 0f, -p), new Vector3(p, 0.025f, p), petals)
                .Build($"menu_flower_{height}_{ColorUtility.ToHtmlStringRGB(petals)}");
        }

        /// <summary>Половинка разворота: обложка и стопка страниц, side −1 — левая, +1 — правая.</summary>
        static void BuildHalf(Transform book, float side)
        {
            var (mesh, mats) = new BoxModel()
                .Box(new Vector3(0.1f, 0f, -0.01f), new Vector3(0.2f, 0.28f, 0.02f), Cover)
                .Box(new Vector3(0.1f, 0.09f, -0.021f), new Vector3(0.16f, 0.025f, 0.002f), CoverBand)
                .Box(new Vector3(0.096f, 0f, 0.015f), new Vector3(0.19f, 0.26f, 0.03f), PaperEdge)
                .Box(new Vector3(0.096f, 0f, 0.0305f), new Vector3(0.188f, 0.255f, 0.001f), Paper)
                .Build("menu_book_half");
            var hinge = new GameObject(side < 0 ? "Left" : "Right").transform;
            hinge.SetParent(book, false);
            // половинка строится вправо от корешка; левая — та же, перевёрнутая на 180° вокруг нормали страниц
            hinge.localRotation = Quaternion.Euler(0f, -side * HalfAngle, 0f) *
                                  (side < 0 ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity);
            BoxModel.Spawn(mesh, mats, hinge, Vector3.zero, "Half");
        }

        /// <summary>Посадить на точку земли ground, лицом к камере (с поворотом Turn).</summary>
        public void Place(Vector3 ground, Vector3 cameraPos)
        {
            _root.position = ground;
            Vector3 to = cameraPos - ground;
            to.y = 0f;
            _root.rotation = Quaternion.LookRotation(to) * Quaternion.Euler(0f, Turn, 0f);
        }

        public void Update(float dt)
        {
            _time += dt;
            // дышит
            _body.localPosition = new Vector3(0f, -0.7f + Mathf.Sin(_time * 1.6f) * 0.008f, 0f);
            _lounger.Update(dt);
            // цветы покачиваются на ветерке
            for (int i = 0; i < _flowers.Length; i++)
                _flowers[i].localRotation = Quaternion.Euler(Mathf.Sin(_time * 1.3f + i * 2.1f) * 6f, i * 37f, Mathf.Cos(_time * 1.1f + i) * 5f);

            if (_pageT < 0f)
            {
                _pageTimer -= dt;
                if (_pageTimer > 0f)
                    return;
                _pageT = 0f;
                _page.gameObject.SetActive(true);
            }
            _pageT += dt / PageTurnTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pageT));
            _page.localRotation = Quaternion.Euler(0f, Mathf.Lerp(-HalfAngle, -180f + HalfAngle, k), 0f);
            if (_pageT >= 1f)
            {
                _pageT = -1f;
                _pageTimer = Random.Range(PageIntervalMin, PageIntervalMax);
                _page.gameObject.SetActive(false);
            }
        }
    }
}
