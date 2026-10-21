using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RocketWars.Launcher
{
    /// <summary>
    /// Пульт наведения у шахты. X — старт/стоп горизонтальной линии (ходит сверху вниз и обратно),
    /// Y — старт/стоп вертикальной (слева направо и обратно). Когда обе линии остановлены — их пересечение
    /// становится целью и загорается Launch.
    /// Маяк из ящика: у его владельца (и только у него — экран пульта рисуется у каждого свой) на карте мигают вражеские шахты.
    /// </summary>
    public class AimConsole : MonoBehaviour
    {
        /// <summary>Сколько полных проходов по экрану в секунду делает линия.</summary>
        public const float LineSpeed = 0.45f;
        const float PixelSize = 0.0025f;
        const float CanvasW = 400f, CanvasH = 330f;

        static readonly Color ScreenBg = new Color(0.86f, 0.86f, 0.86f);
        static readonly Color ButtonBg = new Color(0.2f, 0.18f, 0.06f);
        static readonly Color XColor = new Color(1f, 0.85f, 0.1f);
        static readonly Color YColor = new Color(0.2f, 0.95f, 0.2f);
        static readonly Color LaunchColor = new Color(0.95f, 0.1f, 0.1f);
        static readonly Color DisabledColor = new Color(0.35f, 0.35f, 0.35f);
        // метки маяка мигают жёлтым/белым в чёрной рамке — заметны и на красном, и на зелёном острове
        static readonly Color BeaconColor = new Color(1f, 0.85f, 0.1f);
        static readonly Color BeaconBlink = Color.white;

        public const ulong NoOperator = ulong.MaxValue;
        /// <summary>Сколько секунд пульт закреплён за последним нажавшим игроком.</summary>
        public const float OperatorLockTime = 8f;

        const byte FlagXRunning = 1, FlagXSet = 2, FlagYRunning = 4, FlagYSet = 8;

        /// <summary>
        /// Насколько в прошлое сервер принимает время нажатия клиента: линия останавливается там, где её видел нажавший
        /// (пинг через Relay), но не раньше — иначе можно было бы «выбирать» точку задним числом.
        /// </summary>
        const double MaxPressRewind = 0.75;

        /// <summary>Сколько клиент ждёт подтверждения своего нажатия, прежде чем вернуться к состоянию сервера (нажатие отклонено).</summary>
        const float PredictionTimeout = 1.5f;

        public RocketSilo Silo { get; private set; }
        public bool XRunning { get; private set; }
        public bool XSet { get; private set; }
        public bool YRunning { get; private set; }
        public bool YSet { get; private set; }
        public ulong Operator { get; private set; } = NoOperator;

        // Фазы линий зафиксированы в момент последнего нажатия (_stamp), дальше считаются по общему времени —
        // одинаково на сервере и всех клиентах без постоянной синхронизации.
        float _xPhase0, _yPhase0;
        double _stamp, _lastPress;

        // клиент: своё нажатие X/Y показано сразу, а синхронизация с сервера не перетирает его, пока не придёт
        // состояние с этим нажатием (LastPress ≥ _predictedPress) или не выйдет время
        double _predictedPress;
        float _predictedUntil;

        float XPhase => XPhaseAt(NetClock.Now);
        float YPhase => YPhaseAt(NetClock.Now);

        float XPhaseAt(double time) => _xPhase0 + (XRunning ? LineSpeed * (float)(time - _stamp) : 0f);
        float YPhaseAt(double time) => _yPhase0 + (YRunning ? LineSpeed * (float)(time - _stamp) : 0f);

        /// <summary>Позиция горизонтальной линии (0 — низ экрана, 1 — верх).</summary>
        public float V => 1f - Mathf.PingPong(XPhase, 1f);
        /// <summary>Позиция вертикальной линии (0 — лево, 1 — право).</summary>
        public float U => Mathf.PingPong(YPhase, 1f);
        public bool HasTarget => XSet && YSet && !XRunning && !YRunning;
        public bool CanLaunch => HasTarget && Silo.State == SiloState.Ready;

        Text _status;
        Image _statusBg;
        RawImage _map;
        RectTransform _mapArea, _mapRt, _hLine, _vLine, _marker;
        GameObject _warning, _beaconBanner;
        Text _beaconText;
        readonly List<Image> _beaconMarks = new List<Image>();
        ConsoleButton[] _buttons;
        Texture _layoutFor;
        static Font _font;

        /// <summary>Пультом сейчас пользуется другой игрок.</summary>
        public bool IsLockedFor(ulong clientId) =>
            Operator != NoOperator && Operator != clientId && Silo.State == SiloState.Ready &&
            (XRunning || XSet || YRunning || YSet) && NetClock.Now - _lastPress < OperatorLockTime;

        public bool IsInteractable(ConsoleButtonKind kind) =>
            MatchManager.ConsolesEnabled && Silo.State == SiloState.Ready && !IsLockedFor(NetClock.LocalClientId) &&
            (kind != ConsoleButtonKind.Launch || CanLaunch);

        /// <summary>
        /// Локальный игрок нажал кнопку — запрос уходит на сервер вместе со временем нажатия.
        /// Клиент X/Y применяет сразу, не дожидаясь ответа (иначе линия замирает только через пинг до хоста).
        /// </summary>
        public void RequestPress(ConsoleButtonKind kind)
        {
            var me = NetworkPlayer.LocalPlayer;
            if (me == null)
                return;
            // X/Y: писк — линия пошла, подтверждение — остановилась; Launch — щелчок (дальше сирена)
            bool running = kind == ConsoleButtonKind.X ? XRunning : YRunning;
            var clip = kind == ConsoleButtonKind.Launch ? Sfx.Click : running ? Sfx.ConsoleConfirm : Sfx.ConsoleStart;
            Sfx.PlayAt(clip, transform.position, 0.6f, 20f);
            double now = NetClock.Now;
            if (!NetClock.IsServer && kind != ConsoleButtonKind.Launch)
            {
                ApplyLinePress(kind, now, NetClock.LocalClientId);
                _predictedPress = now;
                _predictedUntil = Time.unscaledTime + PredictionTimeout;
            }
            me.PressConsole(Silo.NetIndex, kind, now);
        }

        /// <summary>Нажал кнопку, которая сейчас не работает (её IsInteractable = false) — звук ошибки, только себе.</summary>
        public void PlayDenied() => Sfx.PlayAt(Sfx.ConsoleError, transform.position, 0.6f, 20f);

        /// <summary>Сервер: применить нажатие, сделанное клиентом в момент pressTime (по общим часам).</summary>
        public void ServerPress(ConsoleButtonKind kind, NetworkPlayer presser, double pressTime)
        {
            ulong clientId = presser.OwnerClientId;
            if (!MatchManager.ConsolesEnabled || Silo.State != SiloState.Ready || IsLockedFor(clientId))
                return;

            // не раньше последнего изменения пульта, не дальше MaxPressRewind в прошлое и не в будущем
            double now = NetClock.Now;
            pressTime = System.Math.Min(System.Math.Max(pressTime, System.Math.Max(_stamp, now - MaxPressRewind)), now);

            if (kind == ConsoleButtonKind.Launch)
            {
                if (!CanLaunch)
                    return;
                var target = ResolveTarget(out bool hasGround);
                if (Silo.ServerLaunch(target, hasGround, presser.gameObject, presser.Health.Team))
                {
                    ResetAim();
                    return;
                }
                Operator = clientId;
                _lastPress = pressTime;
                return;
            }

            ApplyLinePress(kind, pressTime, clientId);
        }

        /// <summary>X/Y: зафиксировать фазы линий на момент нажатия и переключить одну из них (одинаково на сервере и в предсказании клиента).</summary>
        void ApplyLinePress(ConsoleButtonKind kind, double time, ulong clientId)
        {
            _xPhase0 = XPhaseAt(time);
            _yPhase0 = YPhaseAt(time);
            _stamp = time;

            if (kind == ConsoleButtonKind.X)
            {
                XSet = XRunning;
                XRunning = !XRunning;
            }
            else
            {
                YSet = YRunning;
                YRunning = !YRunning;
            }

            Operator = clientId;
            _lastPress = time;
        }

        void ResetAim()
        {
            _xPhase0 = XPhase;
            _yPhase0 = YPhase;
            _stamp = NetClock.Now;
            XRunning = YRunning = XSet = YSet = false;
            Operator = NoOperator;
        }

        public SiloSync ToSync() => new SiloSync
        {
            ConsoleFlags = (byte)((XRunning ? FlagXRunning : 0) | (XSet ? FlagXSet : 0) | (YRunning ? FlagYRunning : 0) | (YSet ? FlagYSet : 0)),
            XPhase0 = _xPhase0,
            YPhase0 = _yPhase0,
            Stamp = _stamp,
            Operator = Operator,
            LastPress = _lastPress
        };

        public void ApplySync(in SiloSync sync)
        {
            // своё нажатие ещё не дошло до сервера — не откатываем линии к старому состоянию
            if (_predictedUntil > 0f)
            {
                if (sync.LastPress < _predictedPress && Time.unscaledTime < _predictedUntil)
                    return;
                _predictedUntil = 0f;
            }
            XRunning = (sync.ConsoleFlags & FlagXRunning) != 0;
            XSet = (sync.ConsoleFlags & FlagXSet) != 0;
            YRunning = (sync.ConsoleFlags & FlagYRunning) != 0;
            YSet = (sync.ConsoleFlags & FlagYSet) != 0;
            _xPhase0 = sync.XPhase0;
            _yPhase0 = sync.YPhase0;
            _stamp = sync.Stamp;
            Operator = sync.Operator;
            _lastPress = sync.LastPress;
        }

        public Vector3 TargetFlat => TacticalMap.Instance != null
            ? TacticalMap.Instance.ScreenToWorld(Silo.Team, new Vector2(U, V))
            : Silo.transform.position;

        /// <summary>Точка попадания: луч сверху вниз в точке прицела. Нет попадания — цель в пустоте.</summary>
        public Vector3 ResolveTarget(out bool hasGround)
        {
            Vector3 flat = TargetFlat;
            float baseY = Silo.Island != null ? Silo.Island.transform.position.y : 0f;
            var ray = new Ray(new Vector3(flat.x, baseY + 200f, flat.z), Vector3.down);
            // нос стоящей в шахте ракеты не считается целью — ракета бьёт в крышу шахты, как раньше
            hasGround = false;
            float best = float.MaxValue;
            Vector3 point = default;
            foreach (var hit in Physics.RaycastAll(ray, 400f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || RocketSilo.IsLoadedRocket(hit.collider))
                    continue;
                best = hit.distance;
                point = hit.point;
                hasGround = true;
            }
            return hasGround ? point : new Vector3(flat.x, baseY, flat.z);
        }

        public bool TargetOnOwnIsland()
        {
            if (!HasTarget || TacticalMap.Instance == null)
                return false;
            var island = TacticalMap.Instance.IslandAt(TargetFlat, out _);
            return island != null && island.Team == Silo.Team;
        }

        void Update()
        {
            if (Silo.IsAuthority && Silo.State == SiloState.Destroyed && (XRunning || YRunning || XSet || YSet))
                ResetAim();
            RefreshUi();
        }

        // ---------- UI ----------

        void RefreshUi()
        {
            switch (Silo.State)
            {
                case SiloState.Ready when !MatchManager.ConsolesEnabled:
                    SetStatus(Loc.TConsole("ОЖИДАНИЕ"), new Color(0.45f, 0.45f, 0.5f));
                    break;
                case SiloState.Ready when IsLockedFor(NetClock.LocalClientId):
                    SetStatus(Loc.TConsole("ЗАНЯТО"), new Color(0.3f, 0.45f, 0.9f));
                    break;
                case SiloState.Ready:
                    SetStatus(CanLaunch ? Loc.TConsole("ГОТОВО — ПУСК!") : Loc.TConsole("ГОТОВО"), new Color(0.1f, 0.85f, 0.1f));
                    break;
                case SiloState.Launching:
                    SetStatus(Loc.TConsole("ПУСК!"), Mathf.Repeat(Time.time, 0.5f) < 0.25f ? LaunchColor : new Color(0.5f, 0.05f, 0.05f));
                    break;
                case SiloState.Reloading:
                    SetStatus(Loc.TConsoleF("ПЕРЕЗАРЯДКА… {0}", Mathf.CeilToInt(Silo.ReloadLeft)), new Color(0.95f, 0.55f, 0.1f));
                    break;
                default:
                    SetStatus(Loc.TConsole("УНИЧТОЖЕНА"), new Color(0.15f, 0.15f, 0.15f));
                    break;
            }

            var map = TacticalMap.Instance;
            var tex = map != null ? map.GetTexture(Silo.Team, withSilos: false) : null; // на пульте точки шахт не нужны
            if (tex != _layoutFor)
                LayoutMap(tex);

            bool showH = XRunning || XSet, showV = YRunning || YSet;
            _hLine.gameObject.SetActive(showH);
            _vLine.gameObject.SetActive(showV);
            _hLine.anchorMin = new Vector2(0f, V);
            _hLine.anchorMax = new Vector2(1f, V);
            _vLine.anchorMin = new Vector2(U, 0f);
            _vLine.anchorMax = new Vector2(U, 1f);
            _marker.gameObject.SetActive(HasTarget);
            _marker.anchorMin = _marker.anchorMax = new Vector2(U, V);
            _warning.SetActive(TargetOnOwnIsland());
            UpdateBeacon(map);

            var hovered = PlayerInteractor.Local != null ? PlayerInteractor.Local.Hovered : null;
            foreach (var b in _buttons)
            {
                Color c = b.Kind == ConsoleButtonKind.Launch ? (b.IsInteractable ? LaunchColor : DisabledColor) : ButtonBg;
                bool running = (b.Kind == ConsoleButtonKind.X && XRunning) || (b.Kind == ConsoleButtonKind.Y && YRunning);
                if (running) c = Color.Lerp(c, Color.white, 0.25f);
                if (!b.IsInteractable && b.Kind != ConsoleButtonKind.Launch) c = Color.Lerp(c, DisabledColor, 0.6f);
                if (b == hovered && b.IsInteractable) c = Color.Lerp(c, Color.white, 0.35f);
                b.Background.color = c;
            }
        }

        /// <summary>Маяк локального игрока: мигающие метки вражеских шахт и таймер над картой.</summary>
        void UpdateBeacon(TacticalMap map)
        {
            var me = NetworkPlayer.LocalPlayer;
            float left = me != null && me.Health != null && me.Health.Team == Silo.Team ? me.BeaconTimeLeft : 0f;
            int used = 0;
            if (left > 0f && map != null)
            {
                var color = Mathf.Repeat(Time.time, 0.6f) < 0.4f ? BeaconColor : BeaconBlink;
                foreach (var enemy in RocketSilo.All)
                {
                    if (enemy.Team == Silo.Team || enemy.State == SiloState.Destroyed)
                        continue;
                    if (used == _beaconMarks.Count)
                    {
                        var frame = Line(_mapRt, "BeaconMark", Color.black, new Vector2(14f, 14f));
                        var inner = Line(frame, "Fill", BeaconColor, new Vector2(9f, 9f));
                        inner.anchorMin = inner.anchorMax = new Vector2(0.5f, 0.5f);
                        _beaconMarks.Add(inner.GetComponent<Image>());
                    }
                    var mark = _beaconMarks[used++];
                    var markRt = (RectTransform)mark.transform.parent;
                    markRt.gameObject.SetActive(true);
                    mark.color = color;
                    markRt.anchorMin = markRt.anchorMax = map.WorldToScreen(Silo.Team, enemy.transform.position);
                }
            }
            for (int i = used; i < _beaconMarks.Count; i++)
                _beaconMarks[i].transform.parent.gameObject.SetActive(false);

            _beaconBanner.SetActive(left > 0f);
            if (left > 0f)
                _beaconText.text = Loc.TConsoleF("МАЯК: шахты врага · {0} с", Mathf.CeilToInt(left));
        }

        void SetStatus(string text, Color bg)
        {
            _status.text = text;
            _statusBg.color = bg;
        }

        void LayoutMap(Texture tex)
        {
            _layoutFor = tex;
            _map.texture = tex;
            if (tex == null)
                return;
            Vector2 area = _mapArea.sizeDelta;
            float aspect = tex.width / (float)tex.height;
            Vector2 size = area.x / area.y > aspect ? new Vector2(area.y * aspect, area.y) : new Vector2(area.x, area.x / aspect);
            var rt = _map.rectTransform;
            rt.sizeDelta = size;
            rt.anchoredPosition = (area - size) * 0.5f;
        }

        // ---------- построение ----------

        public static AimConsole Create(RocketSilo silo)
        {
            // стойка с экраном в 2.4 м от центра шахты, экран смотрит наружу — с той стороны, где перед пультом ровно
            var (dir, baseY, poleBottom) = PickSide(silo);
            var root = new GameObject("AimConsole");
            root.transform.SetParent(silo.transform, false);
            root.transform.localPosition = dir * 2.4f + Vector3.up * baseY;
            root.transform.localRotation = Quaternion.LookRotation(-dir);

            // стойка уходит до земли под ней (пульт подняли к игроку на кубике — снизу не висит в воздухе)
            var gray = new Color(0.75f, 0.75f, 0.75f);
            float poleH = 1.2f - poleBottom;
            RuntimeMaterials.VisualPrimitive(PrimitiveType.Cube, root.transform, new Vector3(0f, poleBottom + poleH * 0.5f, 0.15f), new Vector3(0.1f, poleH, 0.1f), gray);

            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.3f, 0f);
            head.localRotation = Quaternion.Euler(25f, 0f, 0f);
            RuntimeMaterials.VisualPrimitive(PrimitiveType.Cube, head, new Vector3(0f, 0f, 0.035f),
                new Vector3(CanvasW * PixelSize + 0.06f, CanvasH * PixelSize + 0.06f, 0.05f), new Color(0.95f, 0.95f, 0.95f));

            var console = root.AddComponent<AimConsole>();
            console.Silo = silo;
            console.BuildCanvas(head);
            return console;
        }

        static readonly Vector2Int[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        /// <summary>
        /// Сторона шахты, где земля под стойкой и там, где стоит игрок, на уровне плиты: иначе игрок стоит на кубике
        /// (приходится садиться) или под уступом (смотреть вверх). Считается по свежему острову из сида — у всех одинаково.
        /// baseY — пульт по высоте земли под игроком, poleBottom — низ стойки (локально) до земли под ней.
        /// </summary>
        static (Vector3 dir, float baseY, float poleBottom) PickSide(RocketSilo silo)
        {
            var island = silo.Island;
            Vector3 local = silo.transform.position - island.transform.position;
            int cx = Mathf.FloorToInt(local.x), cz = Mathf.FloorToInt(local.z);
            int top = Mathf.RoundToInt(local.y);

            int best = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < Sides.Length; i++)
            {
                var s = Sides[i];
                var side = new Vector2Int(-s.y, s.x);
                var stand = new Vector2Int(cx, cz) + s * 3;
                float cost = i * 0.01f // при равенстве — прежняя сторона +X
                    + StepCost(island, cx + s.x * 2, cz + s.y * 2, top) * 2f // под стойкой
                    + StepCost(island, stand.x, stand.y, top) * 3f // где стоит игрок
                    + StepCost(island, stand.x + side.x, stand.y + side.y, top) * 0.5f
                    + StepCost(island, stand.x - side.x, stand.y - side.y, top) * 0.5f
                    + StepCost(island, cx + s.x * 4, cz + s.y * 4, top); // подход
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }

            var b = Sides[best];
            int standH = island.GetTopHeight(cx + b.x * 3, cz + b.y * 3);
            int poleH = island.GetTopHeight(cx + b.x * 2, cz + b.y * 2);
            float baseY = standH > 0 ? Mathf.Clamp(standH - top, -1, 1) : 0f;
            float poleBottom = poleH > 0 ? Mathf.Min(0f, poleH - top - baseY) : 0f;
            return (new Vector3(b.x, 0f, b.y), baseY, poleBottom);
        }

        /// <summary>Перепад столбца относительно плиты; пустой столбец (край, обрыв) — хуже любого перепада.</summary>
        static float StepCost(RocketWars.Terrain.VoxelIsland island, int x, int z, int top)
        {
            int h = island.GetTopHeight(x, z);
            return h == 0 ? 6f : Mathf.Abs(h - top);
        }

        void BuildCanvas(Transform head)
        {
            _font = GameFonts.Console; // шрифт зависит от языка (CJK — свой пиксельный)

            var canvasGo = new GameObject("Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvasRt = (RectTransform)canvasGo.transform;
            canvasRt.SetParent(head, false);
            canvasRt.sizeDelta = new Vector2(CanvasW, CanvasH);
            canvasRt.localScale = Vector3.one * PixelSize;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            var root = Rect(canvasRt, "Bg", Vector2.zero, new Vector2(CanvasW, CanvasH));
            Img(root, ScreenBg);

            var statusRt = Rect(root, "Status", new Vector2(10, 288), new Vector2(380, 34));
            _statusBg = Img(statusRt, Color.green);
            _status = FitWidth(Txt(statusRt, "", 16, Color.white)); // размеры — под широкий пиксельный шрифт

            _mapArea = Rect(root, "MapArea", new Vector2(10, 92), new Vector2(380, 190));
            Img(_mapArea, new Color(0.45f, 0.45f, 0.47f));
            var mapRt = Rect(_mapArea, "Map", Vector2.zero, _mapArea.sizeDelta);
            _map = mapRt.gameObject.AddComponent<RawImage>();
            _mapRt = mapRt;

            _hLine = Line(mapRt, "HLine", XColor, new Vector2(0f, 3f));
            _vLine = Line(mapRt, "VLine", YColor, new Vector2(3f, 0f));
            _marker = Line(mapRt, "Marker", Color.black, new Vector2(9f, 9f));

            var warnRt = Rect(mapRt, "Warning", Vector2.zero, Vector2.zero);
            warnRt.anchorMin = new Vector2(0f, 0f);
            warnRt.anchorMax = new Vector2(1f, 0.2f);
            warnRt.sizeDelta = Vector2.zero;
            Img(warnRt, new Color(0.6f, 0f, 0f, 0.75f));
            FitWidth(Txt(warnRt, Loc.TConsole("ЦЕЛЬ НА СВОЁМ ОСТРОВЕ!"), 12, Color.white));
            _warning = warnRt.gameObject;

            var beaconRt = Rect(mapRt, "Beacon", Vector2.zero, Vector2.zero);
            beaconRt.anchorMin = new Vector2(0f, 0.84f);
            beaconRt.anchorMax = new Vector2(1f, 1f);
            beaconRt.sizeDelta = Vector2.zero;
            Img(beaconRt, new Color(0.55f, 0.3f, 0f, 0.6f)); // полупрозрачно: метки рисуются поверх
            _beaconText = FitWidth(Txt(beaconRt, "", 12, Color.white));
            _beaconBanner = beaconRt.gameObject;
            _beaconBanner.SetActive(false);

            _buttons = new[]
            {
                MakeButton(root, ConsoleButtonKind.X, new Vector2(10, 10), "—", "X", XColor),
                MakeButton(root, ConsoleButtonKind.Y, new Vector2(140, 10), "|", "Y", YColor),
                MakeButton(root, ConsoleButtonKind.Launch, new Vector2(270, 10), "▲", Loc.TConsole("ПУСК"), Color.black),
            };

            RefreshUi();
        }

        ConsoleButton MakeButton(RectTransform parent, ConsoleButtonKind kind, Vector2 pos, string icon, string label, Color iconColor)
        {
            var rt = Rect(parent, $"Btn_{kind}", pos, new Vector2(120, 72));
            var bg = Img(rt, ButtonBg);
            var iconText = Txt(rt, icon, 32, iconColor);
            iconText.rectTransform.offsetMin = new Vector2(0, 24);
            // подпись (X / Y / LAUNCH) крупно и чуть выше нижнего края кнопки
            var labelText = Txt(rt, label, 16, Color.white);
            labelText.alignment = TextAnchor.LowerCenter;
            labelText.rectTransform.offsetMin = new Vector2(0, 8);
            return ConsoleButton.Attach(rt.gameObject, this, kind, bg);
        }

        static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Line(RectTransform parent, string name, Color color, Vector2 size)
        {
            var rt = Rect(parent, name, Vector2.zero, size);
            rt.pivot = new Vector2(0.5f, 0.5f);
            Img(rt, color);
            return rt;
        }

        static Image Img(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Text Txt(RectTransform parent, string text, int size, Color color)
        {
            var rt = Rect(parent, "Text", Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Переводы бывают длиннее русского: текст ужимается до ширины плашки, но не крупнее заданного размера.</summary>
        static Text FitWidth(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 6;
            t.resizeTextMaxSize = t.fontSize;
            return t;
        }
    }
}
