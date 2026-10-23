using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Player;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UIElements;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace RocketWars.UI
{
    /// <summary>
    /// Экранное управление для телефона: плавающий джойстик слева, обзор пальцем справа, кнопки действий,
    /// пауза и счёт. Пальцы разбираем сами (мультитач через EnhancedTouch), а кнопки UI Toolkit только рисуют
    /// (pickingMode Ignore). Результат — в <see cref="TouchInput"/>, раньше игрока в том же кадре (порядок −200).
    /// Раскладка — по образцу Modern Strike Online / World War Heroes: большой «Огонь» под правым большим пальцем,
    /// прыжок, присед и прицел дугой вокруг него, второй «Огонь» над джойстиком (стрелять и крутить камеру
    /// одновременно), «Действие» — только когда рядом есть, что сделать. Плюс фиксация бега, автоогонь
    /// и «прилипание» прицела к врагу.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>Пикселей экрана → единиц «смещения мыши» (см. FpsController.mouseSensitivity), при высоте экрана <see cref="LookRefHeight"/>.</summary>
        const float LookScale = 2.2f;
        /// <summary>
        /// Высота экрана, под которую подобран <see cref="LookScale"/> (WebGL в Telegram — CSS-пиксели, ~400).
        /// На других экранах сдвиг пальца пересчитывается: поворот за свайп через полэкрана везде одинаковый.
        /// </summary>
        const float LookRefHeight = 400f;
        /// <summary>Ускорение обзора: медленный палец — точно (×1), быстрый взмах — до ×LookAccelMax (разворот одним движением).</summary>
        const float LookAccelFrom = 250f, LookAccelTo = 1400f, LookAccelMax = 1.7f;
        /// <summary>Прицел на враге — обзор медленнее («прилипает», легче вести цель пальцем).</summary>
        const float AimFriction = 0.5f;
        /// <summary>Радиус «прилипания» и автоогня вокруг тела врага (м) и дальность (м).</summary>
        const float FrictionRadius = 0.9f, AutoFireRadius = 0.4f, AssistRange = 120f;
        /// <summary>Радиус хода джойстика (пиксели интерфейса, база 1920×1080).</summary>
        const float StickRadius = 110f;
        /// <summary>Левая доля экрана, где палец ставит джойстик.</summary>
        const float StickZone = 0.42f;
        /// <summary>Джойстик дальше этого — бег.</summary>
        const float SprintAt = 0.92f;
        /// <summary>Палец увели вверх дальше стольких радиусов — бег фиксируется и продолжается без пальца.</summary>
        const float SprintLockAt = 1.6f;
        /// <summary>Касание короче и ближе этого — тап (кнопка пульта).</summary>
        const float TapTime = 0.3f, TapMove = 24f;

        enum Kind { Fire, FireLeft, Jump, Crouch, Aim, Use, Trick, Pause, Score, Start }

        enum Role { None, Stick, Look, Button }

        sealed class Btn
        {
            public Kind Kind;
            public VisualElement El;
            public int Finger = -1;
            public bool Toggled;
        }

        sealed class Finger
        {
            public Role Role;
            public Btn Button;
            public Vector2 Start;
            public float StartTime;
            public bool Moved;
        }

        readonly Dictionary<int, Finger> _fingers = new Dictionary<int, Finger>();
        readonly List<Btn> _buttons = new List<Btn>();
        VisualElement _layer, _stickBase, _stickKnob, _sprintLock;
        Vector2 _stickCenter;
        int _stickFinger = -1;
        bool _visible, _sprintArmed, _sprintLocked;

        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            TouchInput.Release();
        }

        /// <summary>Нарисовать заново (UiRoot пересобирает весь интерфейс при смене языка).</summary>
        public void Build(VisualElement root)
        {
            _fingers.Clear();
            _buttons.Clear();
            _stickFinger = -1;
            _sprintArmed = _sprintLocked = false;
            TouchInput.Release();

            _layer = Ui.El(root, "layer", "touch");
            _layer.pickingMode = PickingMode.Ignore;

            _stickBase = Ui.El(_layer, "touch-stick");
            _stickKnob = Ui.El(_stickBase, "touch-knob");
            // «замок» бега над джойстиком: видно, куда довести палец
            _sprintLock = Ui.El(_layer, "touch-sprint-lock");
            _sprintLock.pickingMode = PickingMode.Ignore;
            Ui.Text(_sprintLock, Loc.T("Бег"), "touch-label").pickingMode = PickingMode.Ignore;

            Add(Kind.Fire, GameInput.TouchLabel(GameAction.Fire), "touch-fire");
            Add(Kind.FireLeft, GameInput.TouchLabel(GameAction.Fire), "touch-fire-left");
            Add(Kind.Jump, GameInput.TouchLabel(GameAction.Jump), "touch-jump");
            Add(Kind.Crouch, GameInput.TouchLabel(GameAction.Crouch), "touch-crouch");
            Add(Kind.Aim, GameInput.TouchLabel(GameAction.Aim), "touch-aim");
            Add(Kind.Use, GameInput.TouchLabel(GameAction.Use), "touch-use");
            Add(Kind.Trick, GameInput.TouchLabel(GameAction.Trick), "touch-trick");
            Add(Kind.Pause, "II", "touch-pause", false);
            Add(Kind.Score, "СЧЁТ", "touch-score");
            Add(Kind.Start, "СТАРТ", "touch-start"); // хост начинает матч (на ПК — Enter)
            ApplyVisible();
        }

        void Add(Kind kind, string label, string cls, bool translate = true)
        {
            var el = Ui.El(_layer, "touch-btn", cls);
            el.pickingMode = PickingMode.Ignore;
            var text = Ui.Text(el, translate ? Loc.T(label) : label, "touch-label");
            text.pickingMode = PickingMode.Ignore;
            _buttons.Add(new Btn { Kind = kind, El = el });
        }

        /// <summary>Видно только в игре, когда управляет игрок (не пауза, не итоги, не меню).</summary>
        public void SetVisible(bool visible)
        {
            visible &= TouchInput.Enabled;
            if (visible == _visible)
                return;
            _visible = visible;
            ApplyVisible();
            if (!visible)
                ReleaseAll();
        }

        void ApplyVisible()
        {
            if (_layer != null)
                _layer.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ReleaseAll()
        {
            _fingers.Clear();
            _stickFinger = -1;
            _sprintArmed = _sprintLocked = false;
            foreach (var b in _buttons)
            {
                b.Finger = -1;
                b.Toggled = false;
            }
            TouchInput.Release();
        }

        void Update()
        {
            TouchInput.LookDelta = Vector2.zero;
            if (!_visible || _layer?.panel == null)
                return;

            var local = NetworkPlayer.LocalPlayer;
            var health = local != null ? local.GetComponent<PlayerHealth>() : null;
            bool downed = health != null && health.IsDowned;
            var rider = local != null ? local.GetComponent<RocketRider>() : null;
            bool inFlight = rider != null && rider.InFlight;
            var match = MatchManager.Instance;
            bool canStart = match != null && match.IsSpawned && match.IsServer && match.CanStartMatch && match.StartBlocker() == null;

            foreach (var touch in ETouch.activeTouches)
                Handle(touch);

            // лёжа бинокль, присед и бег не нужны — после подъёма не включатся сами
            if (downed)
                foreach (var b in _buttons)
                    b.Toggled = false;
            // зафиксированный бег не переживает падения и паузы между раундами
            if (downed || MatchManager.FreezePlayers)
                CancelSprintLock();

            // помощь в прицеливании: обзор «прилипает» к врагу, а с автоогнём оружие стреляет само
            float miss = float.MaxValue;
            var target = !downed && health != null ? AimTarget(health, out miss) : null;
            if (target != null && miss <= FrictionRadius)
                TouchInput.LookDelta *= AimFriction;
            bool autoFire = target != null && miss <= AutoFireRadius && TouchInput.AutoFire
                && PlayerInteractor.Local?.Hovered == null // огонь по кнопке пульта нажал бы её
                && !(local.GetComponent<PistolView>() is PistolView gun && gun.HoldingBazooka); // базука задевает и своих — только вручную

            bool fire = autoFire;
            foreach (var b in _buttons)
            {
                bool pressed = b.Finger >= 0 || b.Toggled;
                b.El.EnableInClassList("touch-pressed", pressed);
                switch (b.Kind)
                {
                    case Kind.Fire:
                    case Kind.FireLeft: fire |= b.Finger >= 0; break;
                    case Kind.Jump: TouchInput.SetHeld(GameAction.Jump, b.Finger >= 0); break;
                    case Kind.Use: TouchInput.SetHeld(GameAction.Use, b.Finger >= 0); break;
                    case Kind.Trick: TouchInput.SetHeld(GameAction.Trick, b.Finger >= 0 && inFlight); break;
                    case Kind.Crouch: TouchInput.SetHeld(GameAction.Crouch, b.Toggled); break;
                    case Kind.Aim: TouchInput.SetHeld(GameAction.Aim, b.Toggled); break;
                    case Kind.Score: TouchInput.ScoreHeld = b.Finger >= 0; break;
                }
                // трюк — только верхом на летящей ракете; «Действие» — когда рядом есть, что сделать (и пока его держат);
                // лёжа — только обзор и ползком
                bool show = b.Kind switch
                {
                    Kind.Trick => inFlight,
                    Kind.Start => canStart,
                    Kind.Use => TouchInput.UseContext || b.Finger >= 0,
                    Kind.Fire or Kind.FireLeft or Kind.Jump or Kind.Crouch or Kind.Aim => !downed,
                    _ => true,
                };
                b.El.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
            TouchInput.SetHeld(GameAction.Fire, fire);

            if (_sprintLocked)
                TouchInput.Move = Vector2.up;
            bool sprint = _sprintLocked || TouchInput.Move.magnitude > SprintAt && TouchInput.Move.y > 0.5f;
            TouchInput.SetHeld(GameAction.Sprint, sprint);
            UpdateStickVisual();
        }

        /// <summary>
        /// Ближайший к линии прицела враг в прямой видимости: <paramref name="miss"/> — на сколько метров
        /// прицел проходит мимо его тела (отрезок от ног до головы).
        /// </summary>
        static PlayerHealth AimTarget(PlayerHealth me, out float miss)
        {
            miss = float.MaxValue;
            var cam = PlayerInteractor.Local != null ? PlayerInteractor.Local.cam : null;
            if (cam == null)
                return null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            PlayerHealth best = null;
            foreach (var p in PlayerHealth.All)
            {
                if (p == me || !p.IsSpawned || p.IsDowned || p.Team == me.Team)
                    continue;
                Vector3 feet = p.transform.position;
                Vector3 point = ClosestOnSegment(feet + Vector3.up * 0.3f, feet + Vector3.up * 1.7f, ray);
                float along = Vector3.Dot(point - ray.origin, ray.direction);
                if (along <= 0f || along > AssistRange)
                    continue;
                float d = Vector3.Distance(point, ray.origin + ray.direction * along);
                if (d < miss && CanSee(me, p, ray.origin, point))
                {
                    miss = d;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Точка отрезка ab, ближайшая к лучу.</summary>
        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Ray ray)
        {
            Vector3 ab = b - a, w = a - ray.origin;
            float bb = Vector3.Dot(ab, ab), bd = Vector3.Dot(ab, ray.direction);
            float denom = bb - bd * bd; // направление луча единичное
            float t = denom > 1e-5f ? (bd * Vector3.Dot(w, ray.direction) - Vector3.Dot(ab, w)) / denom : 0f;
            return a + ab * Mathf.Clamp01(t);
        }

        static bool CanSee(PlayerHealth me, PlayerHealth target, Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            if (!Physics.Raycast(eye, to.normalized, out var hit, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
                return true;
            var owner = hit.collider.GetComponentInParent<PlayerHealth>();
            return owner == target || owner == me;
        }

        void Handle(ETouch touch)
        {
            int id = touch.touchId;
            Vector2 p = ToPanel(touch.screenPosition);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    _fingers[id] = Begin(id, p);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (!_fingers.TryGetValue(id, out var f))
                        _fingers[id] = f = Begin(id, p); // палец уже стоял, когда управление появилось
                    Move(f, touch, p);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (_fingers.TryGetValue(id, out var e))
                        End(id, e, touch);
                    _fingers.Remove(id);
                    break;
            }
        }

        Finger Begin(int id, Vector2 p)
        {
            var f = new Finger { Start = p, StartTime = Time.unscaledTime };
            foreach (var b in _buttons)
            {
                if (b.El.style.display.value == DisplayStyle.None || !Contains(b.El, p))
                    continue;
                f.Role = Role.Button;
                f.Button = b;
                b.Finger = id;
                switch (b.Kind)
                {
                    case Kind.Crouch:
                    case Kind.Aim:
                        b.Toggled = !b.Toggled;
                        break;
                    case Kind.Pause:
                        UiRoot.Instance?.SetPause(true);
                        break;
                    case Kind.Start:
                        if (MatchManager.Instance is MatchManager match && match.IsServer && match.CanStartMatch)
                            match.RequestStartMatch();
                        break;
                }
                return f;
            }
            if (p.x < _layer.layout.width * StickZone && _stickFinger < 0)
            {
                f.Role = Role.Stick;
                _stickFinger = id;
                _stickCenter = p;
                CancelSprintLock(); // новое касание джойстика снимает фиксацию бега
                TouchInput.Move = Vector2.zero;
                return f;
            }
            f.Role = Role.Look;
            return f;
        }

        void Move(Finger f, ETouch touch, Vector2 p)
        {
            if ((p - f.Start).sqrMagnitude > TapMove * TapMove)
                f.Moved = true;
            switch (f.Role)
            {
                case Role.Stick:
                    var raw = p - _stickCenter;
                    var offset = Vector2.ClampMagnitude(raw, StickRadius);
                    TouchInput.Move = new Vector2(offset.x, -offset.y) / StickRadius;
                    // палец ушёл вверх за «замок» — отпустите, и бег продолжится сам (как в CoD Mobile / Modern Strike)
                    _sprintArmed = -raw.y > StickRadius * SprintLockAt && Mathf.Abs(raw.x) < StickRadius;
                    break;
                case Role.Look:
                    TouchInput.LookDelta += Look(touch);
                    break;
                case Role.Button:
                    // с зажатым огнём можно и поворачиваться — как в мобильных шутерах
                    if (f.Button.Kind == Kind.Fire)
                        TouchInput.LookDelta += Look(touch);
                    break;
            }
        }

        /// <summary>Сдвиг пальца → поворот: без зависимости от разрешения экрана и с ускорением быстрых взмахов.</summary>
        static Vector2 Look(ETouch touch)
        {
            Vector2 delta = touch.delta * (LookRefHeight / Mathf.Max(1, Screen.height));
            float speed = delta.magnitude / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            float accel = Mathf.Lerp(1f, LookAccelMax, Mathf.InverseLerp(LookAccelFrom, LookAccelTo, speed));
            return delta * (LookScale * accel);
        }

        void End(int id, Finger f, ETouch touch)
        {
            switch (f.Role)
            {
                case Role.Stick:
                    _stickFinger = -1;
                    _sprintLocked = _sprintArmed;
                    _sprintArmed = false;
                    TouchInput.Move = _sprintLocked ? Vector2.up : Vector2.zero;
                    break;
                case Role.Look:
                    if (!f.Moved && Time.unscaledTime - f.StartTime < TapTime)
                        TouchInput.Tap(touch.screenPosition);
                    break;
                case Role.Button:
                    if (f.Button.Finger == id)
                        f.Button.Finger = -1;
                    break;
            }
        }

        void CancelSprintLock()
        {
            if (!_sprintLocked)
                return;
            _sprintLocked = false;
            if (_stickFinger < 0)
                TouchInput.Move = Vector2.zero;
        }

        void UpdateStickVisual()
        {
            bool active = _stickFinger >= 0;
            _stickBase.EnableInClassList("touch-stick-active", active || _sprintLocked);
            if (active)
            {
                _stickBase.style.left = _stickCenter.x - StickRadius;
                _stickBase.style.top = _stickCenter.y - StickRadius;
                _stickBase.style.bottom = StyleKeyword.Auto;
            }
            else
            {
                // в покое — на своём месте внизу слева, подсказкой «здесь ходить»
                _stickBase.style.left = StyleKeyword.Null;
                _stickBase.style.top = StyleKeyword.Null;
                _stickBase.style.bottom = StyleKeyword.Null;
            }
            var knob = new Vector2(TouchInput.Move.x, -TouchInput.Move.y) * StickRadius;
            _stickKnob.style.translate = new Translate(knob.x, knob.y);

            // «замок» бега: виден, пока палец на джойстике или бег зафиксирован; над центром джойстика
            bool lockShown = active && TouchInput.Move.y > 0.5f || _sprintLocked;
            _sprintLock.style.display = lockShown ? DisplayStyle.Flex : DisplayStyle.None;
            _sprintLock.EnableInClassList("touch-pressed", _sprintArmed || _sprintLocked);
            if (lockShown)
            {
                var b = _stickBase.layout;
                float cx = active ? _stickCenter.x : b.x + b.width * 0.5f;
                float cy = active ? _stickCenter.y : b.y + b.height * 0.5f;
                var size = _sprintLock.layout;
                _sprintLock.style.left = cx - (float.IsNaN(size.width) ? 0f : size.width * 0.5f);
                _sprintLock.style.top = cy - StickRadius * SprintLockAt - (float.IsNaN(size.height) ? 0f : size.height * 0.5f);
            }
        }

        bool Contains(VisualElement el, Vector2 panelPoint) => el.worldBound.Contains(panelPoint);

        /// <summary>Экран (начало внизу слева) → координаты интерфейса (начало вверху слева, с учётом масштаба).</summary>
        Vector2 ToPanel(Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(_layer.panel, new Vector2(screen.x, Screen.height - screen.y));
    }
}
