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
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>Пикселей экрана → единиц «смещения мыши» (см. FpsController.mouseSensitivity).</summary>
        const float LookScale = 2.2f;
        /// <summary>Радиус хода джойстика (пиксели интерфейса, база 1920×1080).</summary>
        const float StickRadius = 110f;
        /// <summary>Левая доля экрана, где палец ставит джойстик.</summary>
        const float StickZone = 0.42f;
        /// <summary>Джойстик дальше этого — бег.</summary>
        const float SprintAt = 0.92f;
        /// <summary>Касание короче и ближе этого — тап (кнопка пульта).</summary>
        const float TapTime = 0.3f, TapMove = 24f;

        enum Kind { Fire, Jump, Crouch, Aim, Use, Trick, Pause, Score, Start }

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
        VisualElement _layer, _stickBase, _stickKnob;
        Vector2 _stickCenter;
        int _stickFinger = -1;
        bool _visible;

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
            TouchInput.Release();

            _layer = Ui.El(root, "layer", "touch");
            _layer.pickingMode = PickingMode.Ignore;

            _stickBase = Ui.El(_layer, "touch-stick");
            _stickKnob = Ui.El(_stickBase, "touch-knob");

            Add(Kind.Fire, GameInput.TouchLabel(GameAction.Fire), "touch-fire");
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

            // лёжа бинокль и присед не нужны — после подъёма не включатся сами
            if (downed)
                foreach (var b in _buttons)
                    b.Toggled = false;

            foreach (var b in _buttons)
            {
                bool pressed = b.Finger >= 0 || b.Toggled;
                b.El.EnableInClassList("touch-pressed", pressed);
                switch (b.Kind)
                {
                    case Kind.Fire: TouchInput.SetHeld(GameAction.Fire, b.Finger >= 0); break;
                    case Kind.Jump: TouchInput.SetHeld(GameAction.Jump, b.Finger >= 0); break;
                    case Kind.Use: TouchInput.SetHeld(GameAction.Use, b.Finger >= 0); break;
                    case Kind.Trick: TouchInput.SetHeld(GameAction.Trick, b.Finger >= 0 && inFlight); break;
                    case Kind.Crouch: TouchInput.SetHeld(GameAction.Crouch, b.Toggled); break;
                    case Kind.Aim: TouchInput.SetHeld(GameAction.Aim, b.Toggled); break;
                    case Kind.Score: TouchInput.ScoreHeld = b.Finger >= 0; break;
                }
                // трюк — только верхом на летящей ракете; лёжа — только обзор и ползком
                bool show = b.Kind switch
                {
                    Kind.Trick => inFlight,
                    Kind.Start => canStart,
                    Kind.Fire or Kind.Jump or Kind.Crouch or Kind.Aim => !downed,
                    _ => true,
                };
                b.El.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool sprint = TouchInput.Move.magnitude > SprintAt && TouchInput.Move.y > 0.5f;
            TouchInput.SetHeld(GameAction.Sprint, sprint);
            UpdateStickVisual();
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
                    var offset = Vector2.ClampMagnitude(p - _stickCenter, StickRadius);
                    TouchInput.Move = new Vector2(offset.x, -offset.y) / StickRadius;
                    break;
                case Role.Look:
                    TouchInput.LookDelta += touch.delta * LookScale;
                    break;
                case Role.Button:
                    // с зажатым огнём можно и поворачиваться — как в мобильных шутерах
                    if (f.Button.Kind == Kind.Fire)
                        TouchInput.LookDelta += touch.delta * LookScale;
                    break;
            }
        }

        void End(int id, Finger f, ETouch touch)
        {
            switch (f.Role)
            {
                case Role.Stick:
                    _stickFinger = -1;
                    TouchInput.Move = Vector2.zero;
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

        void UpdateStickVisual()
        {
            bool active = _stickFinger >= 0;
            _stickBase.EnableInClassList("touch-stick-active", active);
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
        }

        bool Contains(VisualElement el, Vector2 panelPoint) => el.worldBound.Contains(panelPoint);

        /// <summary>Экран (начало внизу слева) → координаты интерфейса (начало вверху слева, с учётом масштаба).</summary>
        Vector2 ToPanel(Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(_layer.panel, new Vector2(screen.x, Screen.height - screen.y));
    }
}
