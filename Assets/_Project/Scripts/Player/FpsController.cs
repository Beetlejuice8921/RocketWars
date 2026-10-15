using RocketWars.Core;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketWars.Player
{
    /// <summary>
    /// Движение от первого лица: бег, спринт, прыжок, присед, автоподъём на 1 кубик (stepOffset),
    /// езда верхом на ракете (позицию задаёт RocketRider), инерция после схода с ракеты и парашют
    /// (сам раскрывается при падении выше 10 м, исчезает при приземлении или по пробелу),
    /// жёсткое приземление без парашюта (событие HardLanding, урон — на сервере).
    /// Pivot — в ногах. Без IsLocal ввод не читается (боты, удалённые игроки).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FpsController : MonoBehaviour
    {
        // Параметры движения зашифрованы в памяти (ObscuredFloat) и не настраиваются в инспекторе:
        // иначе ArtMoney находит «5» / «−22» и превращает их в спидхак, суперпрыжок или полёт.
        public const float SprintSpeed = 7.5f;
        readonly ObscuredFloat _walkSpeed = 5f;
        readonly ObscuredFloat _sprintSpeed = SprintSpeed;
        readonly ObscuredFloat _crouchSpeed = 2.5f;
        /// <summary>Лежащий ползёт втрое медленнее шага.</summary>
        readonly ObscuredFloat _crawlSpeed = 5f / 3f;
        const float CrawlTurnSpeed = 240f;
        readonly ObscuredFloat _jumpHeight = 1.3f;
        readonly ObscuredFloat _gravity = -22f;
        readonly ObscuredFloat _stepOffset = 1.05f;
        readonly ObscuredFloat _maxFallSpeed = 40f;
        const float MaxStep = 0.05f;
        const int MaxSubsteps = 4;

        readonly ObscuredFloat _parachuteOpenHeight = 10f;
        readonly ObscuredFloat _parachuteFallSpeed = 4f;
        public const float ParachuteSteerSpeed = 3.5f;
        readonly ObscuredFloat _parachuteSteerSpeed = ParachuteSteerSpeed;

        [Header("Body")]
        public float standHeight = 2f;
        public float crouchHeight = 1.3f;
        public float downedHeight = 0.8f;
        public float radius = 0.4f;
        public float eyeBelowTop = 0.25f;

        [Header("Look")]
        public float mouseSensitivity = 0.1f;
        /// <summary>Множитель чувствительности (бинокль снижает её пропорционально увеличению).</summary>
        public float LookSensitivityScale { get; set; } = 1f;

        public bool IsLocal { get; set; }
        /// <summary>Двигать CharacterController здесь (владелец). У чужих копий позицию ведёт NetworkTransform.</summary>
        public bool Simulate { get; set; } = true;
        /// <summary>Присед чужого игрока, пришедший по сети.</summary>
        public bool RemoteCrouch { get; set; }
        public bool IsCrouching { get; private set; }
        /// <summary>Насколько игрок присел: 0 — стоит, 1 — полностью сидит (плавно, для анимаций и HUD).</summary>
        public float CrouchAmount => Mathf.Clamp01(Mathf.InverseLerp(standHeight, crouchHeight, _currentHeight));
        public bool IsGrounded => _cc.isGrounded;
        /// <summary>Внешняя блокировка движения (лежит, стоит за пультом).</summary>
        public bool MovementLocked { get; set; }
        public bool LookLocked { get; set; }
        public Transform CameraRoot { get; private set; }
        /// <summary>Висит на ракете: гравитации и ходьбы нет, позицию каждый кадр ставит RocketRider.</summary>
        public bool Attached { get; private set; }
        public bool ParachuteOpen { get; private set; }
        /// <summary>Инерция (например, после схода с ракеты): гасится на земле быстро, под парашютом — заметно.</summary>
        public Vector3 ExternalVelocity { get; set; }
        /// <summary>
        /// Зависнуть в воздухе: вертикаль тянется к этой высоте (мировой Y), гравитации нет, ходьба работает.
        /// null — обычная гравитация (берсерк «возносится» на 2 м на время превращения).
        /// </summary>
        public float? HoverTarget { get; set; }
        /// <summary>Жёсткое приземление без парашюта: высота падения в метрах (урон считает сервер).</summary>
        public event System.Action<float> HardLanding;

        /// <summary>Ввод бота: направление движения в мировых осях (длина ≤ 1) и бег.</summary>
        public Vector3 BotMove { get; set; }
        public bool BotSprint { get; set; }
        bool _botJump;
        /// <summary>Бот: прыгнуть (если стоит на земле).</summary>
        public void BotJump() => _botJump = true;
        /// <summary>Бот: наклон взгляда (руки с оружием и выстрелы — из CameraRoot).</summary>
        public void SetPitch(float pitch)
        {
            _pitch = Mathf.Clamp(pitch, -89f, 89f);
            ApplyCameraRotation();
        }

        CharacterController _cc;
        PlayerHealth _health;
        Vector3 _velocity;
        float _pitch;
        float _lookYaw;
        float _currentHeight;
        bool _parachuteCut;
        float _airPeakY;
        bool _wasGrounded = true;
        bool _ignoreNextLanding;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            _cc.radius = radius;
            _cc.slopeLimit = 50f;
            _cc.skinWidth = 0.05f;

            CameraRoot = new GameObject("CameraRoot").transform;
            CameraRoot.SetParent(transform, false);
            ApplyHeight(standHeight);
        }

        void Update()
        {
            bool downed = _health != null && _health.IsDowned;

            if (IsLocal)
                UpdateCursor();
            if (IsLocal && !LookLocked && Cursor.lockState == CursorLockMode.Locked)
                UpdateLook(downed);
            if (!downed && _lookYaw != 0f)
                MergeLookYaw();

            UpdateCrouch(downed);
            if (!Simulate)
                return;
            if (Attached)
            {
                _velocity = Vector3.zero;
                _airPeakY = transform.position.y; // высоту падения считаем с момента, когда отцепились
                _wasGrounded = false;
                return;
            }

            if (HoverTarget.HasValue)
                ParachuteOpen = false;
            else
                UpdateParachute();

            Vector3 move = Vector3.zero;
            float speed = ParachuteOpen ? _parachuteSteerSpeed : _walkSpeed;
            if (IsLocal && !downed && !MovementLocked)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    float h = (GameInput.IsPressed(GameAction.Right) ? 1 : 0) - (GameInput.IsPressed(GameAction.Left) ? 1 : 0);
                    float v = (GameInput.IsPressed(GameAction.Forward) ? 1 : 0) - (GameInput.IsPressed(GameAction.Back) ? 1 : 0);
                    move = Vector3.ClampMagnitude(transform.right * h + transform.forward * v, 1f);

                    if (ParachuteOpen)
                        speed = _parachuteSteerSpeed;
                    else if (IsCrouching)
                        speed = _crouchSpeed;
                    else if (GameInput.IsPressed(GameAction.Sprint))
                        speed = _sprintSpeed;

                    if (GameInput.WasPressed(GameAction.Jump))
                    {
                        if (ParachuteOpen)
                        {
                            // отстегнуть парашют: дальше свободное падение, до земли он больше не раскроется
                            ParachuteOpen = false;
                            _parachuteCut = true;
                        }
                        else if (_cc.isGrounded && !IsCrouching)
                        {
                            _velocity.y = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
                        }
                    }
                }
            }
            else if (IsLocal && downed && !MovementLocked)
            {
                move = CrawlInput();
                speed = _crawlSpeed;
            }
            else if (!IsLocal && !downed && !MovementLocked)
            {
                move = Vector3.ClampMagnitude(new Vector3(BotMove.x, 0f, BotMove.z), 1f);
                speed = ParachuteOpen ? _parachuteSteerSpeed : BotSprint ? _sprintSpeed : _walkSpeed;
                if (_botJump && _cc.isGrounded)
                    _velocity.y = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
            }
            _botJump = false;

            // длинный кадр (загрузка, подключение) режем на подшаги, чтобы не «пробить» тонкий остров
            float remaining = Mathf.Min(Time.deltaTime, MaxStep * MaxSubsteps);
            while (remaining > 0f)
            {
                float dt = Mathf.Min(remaining, MaxStep);
                remaining -= dt;
                if (HoverTarget.HasValue)
                {
                    // плавно подтягиваемся к высоте зависания; высоту падения отсчитываем заново
                    _velocity.y = Mathf.Clamp((HoverTarget.Value - transform.position.y) * 8f, -6f, 6f);
                    _airPeakY = transform.position.y;
                }
                else
                {
                    if (_cc.isGrounded && _velocity.y < 0)
                        _velocity.y = -2f;
                    _velocity.y = Mathf.Max(_velocity.y + _gravity * dt, ParachuteOpen ? -_parachuteFallSpeed : -_maxFallSpeed);
                }

                // инерция: на земле гасится за доли секунды, под куполом — за пару секунд, в свободном полёте почти сохраняется
                var ext = ExternalVelocity;
                if (_cc.isGrounded) ext = Vector3.MoveTowards(ext, Vector3.zero, 30f * dt);
                else ext *= Mathf.Max(0f, 1f - (ParachuteOpen ? 1.2f : 0.05f) * dt);
                ExternalVelocity = ext;

                _cc.Move((move * speed + ext + Vector3.up * _velocity.y) * dt);
            }

            TrackLanding();

            if (_cc.isGrounded)
            {
                ParachuteOpen = false;
                _parachuteCut = false;
            }
        }

        /// <summary>
        /// Высота падения — от верхней точки полёта; под парашютом отсчёт начинается заново каждый кадр,
        /// поэтому приземление с куполом безопасно, а после «отстегнуть» считается с точки отстёгивания.
        /// </summary>
        void TrackLanding()
        {
            bool grounded = _cc.isGrounded;
            float y = transform.position.y;
            if (!grounded)
            {
                _airPeakY = _wasGrounded || ParachuteOpen ? y : Mathf.Max(_airPeakY, y);
            }
            else if (!_wasGrounded)
            {
                float drop = _airPeakY - y;
                if (!_ignoreNextLanding && drop > 0f)
                    HardLanding?.Invoke(drop);
                _ignoreNextLanding = false;
            }
            _wasGrounded = grounded;
        }

        /// <summary>Парашют раскрывается сам: в воздухе, при падении, если до земли больше 10 м (или под ногами пустота).</summary>
        void UpdateParachute()
        {
            if (ParachuteOpen || _parachuteCut || _cc.isGrounded || _velocity.y > -2f)
                return;
            bool groundClose = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
                _parachuteOpenHeight, ~0, QueryTriggerInteraction.Ignore);
            if (!groundClose)
                ParachuteOpen = true;
        }

        // ---------- ракета ----------

        /// <summary>Повиснуть на ракете: дальше позицию задаёт SetAttachedPosition.</summary>
        public void Attach()
        {
            Attached = true;
            ParachuteOpen = false;
            _parachuteCut = false;
            ExternalVelocity = Vector3.zero;
            _velocity = Vector3.zero;
        }

        /// <summary>Позиция, пока висим (коллайдер остаётся включённым — взрыв ракеты должен нас задеть).</summary>
        public void SetAttachedPosition(Vector3 position) => transform.position = position;

        /// <summary>Отцепиться: летим дальше по инерции ракеты.</summary>
        public void Release(Vector3 velocity)
        {
            if (!Attached)
                return;
            Attached = false;
            _cc.enabled = false; // синхронизировать CharacterController с новой позицией
            _cc.enabled = true;
            ResetEye();
            ExternalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            _velocity = new Vector3(0f, velocity.y, 0f);
        }

        /// <summary>Курсор захвачен, пока интерфейсу (меню, пауза, итоги) он не нужен.</summary>
        void UpdateCursor()
        {
            bool free = UI.UiRoot.CursorFree || LookLocked;
            var wanted = free ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.lockState != wanted)
            {
                // в браузере Esc снимает захват сам (а игра Esc может и не получить) — открываем паузу, а не захватываем снова
                if (!free && _cursorWasLocked && GamePlatform.Current.IsWeb)
                {
                    _cursorWasLocked = false;
                    UI.UiRoot.Instance?.OnPointerLockLost();
                    return;
                }
                Cursor.lockState = wanted;
                Cursor.visible = free;
            }
            _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
        }

        bool _cursorWasLocked;

        /// <summary>
        /// Обзор мышью. Лёжа крутится только камера (_lookYaw), тело остаётся лежать как лежит —
        /// иначе остальные видели бы, как лежащее тело вращается вокруг ног.
        /// </summary>
        void UpdateLook(bool downed)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;
            Vector2 delta = mouse.delta.ReadValue() * (mouseSensitivity * GameInput.Sensitivity * LookSensitivityScale);
            if (downed)
                _lookYaw = Mathf.Repeat(_lookYaw + delta.x + 180f, 360f) - 180f;
            else
                transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -89f, 89f);
            ApplyCameraRotation();
        }

        /// <summary>
        /// Ползком: WASD относительно взгляда камеры (лёжа она крутится отдельно от тела), тело разворачивается
        /// головой по ходу движения, а камера при этом остаётся смотреть туда же.
        /// </summary>
        Vector3 CrawlInput()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return Vector3.zero;
            float h = (GameInput.IsPressed(GameAction.Right) ? 1 : 0) - (GameInput.IsPressed(GameAction.Left) ? 1 : 0);
            float v = (GameInput.IsPressed(GameAction.Forward) ? 1 : 0) - (GameInput.IsPressed(GameAction.Back) ? 1 : 0);
            if (h == 0f && v == 0f)
                return Vector3.zero;

            var view = transform.rotation * Quaternion.Euler(0f, _lookYaw, 0f);
            Vector3 move = Vector3.ClampMagnitude(view * Vector3.right * h + view * Vector3.forward * v, 1f);

            // лёжа голова — со стороны −forward (PlayerVisual): поворачиваем тело затылком назад к движению
            float bodyYaw = transform.eulerAngles.y;
            float wantYaw = Quaternion.LookRotation(-move).eulerAngles.y;
            float turn = Mathf.MoveTowardsAngle(bodyYaw, wantYaw, CrawlTurnSpeed * Time.deltaTime) - bodyYaw;
            if (turn != 0f)
            {
                transform.Rotate(0f, turn, 0f, Space.Self);
                _lookYaw = Mathf.Repeat(_lookYaw - turn + 180f, 360f) - 180f;
                ApplyCameraRotation();
            }
            return move;
        }

        /// <summary>Игрока подняли — он смотрит туда же, куда смотрел лёжа.</summary>
        void MergeLookYaw()
        {
            transform.Rotate(0f, _lookYaw, 0f, Space.Self);
            _lookYaw = 0f;
            ApplyCameraRotation();
        }

        void ApplyCameraRotation() => CameraRoot.localRotation = Quaternion.Euler(_pitch, _lookYaw, 0f);

        void UpdateCrouch(bool downed)
        {
            float target;
            if (downed)
            {
                IsCrouching = false;
                target = downedHeight;
            }
            else
            {
                bool wantCrouch = !IsLocal && RemoteCrouch;
                if (IsLocal && !MovementLocked)
                {
                    wantCrouch = GameInput.IsPressed(GameAction.Crouch);
                }

                if (Simulate && !wantCrouch && _currentHeight < standHeight && !HasHeadroom(standHeight))
                    wantCrouch = true; // под потолком встать нельзя

                IsCrouching = wantCrouch;
                target = wantCrouch ? crouchHeight : standHeight;
            }

            if (!Mathf.Approximately(target, _currentHeight))
                ApplyHeight(Mathf.MoveTowards(_currentHeight, target, 8f * Time.deltaTime));
        }

        bool HasHeadroom(float height)
        {
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (height - radius);
            foreach (var c in Physics.OverlapCapsule(bottom, top, radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                if (c != _cc)
                    return false;
            return true;
        }

        void ApplyHeight(float height)
        {
            _currentHeight = height;
            _cc.height = height;
            _cc.center = Vector3.up * (height * 0.5f);
            _cc.stepOffset = Mathf.Min(_stepOffset, Mathf.Max(0f, height - 0.1f));
            ResetEye();
        }

        /// <summary>Глаза — над капсулой (верхом на ракете их на время переносит в голову RocketRider).</summary>
        void ResetEye() => CameraRoot.localPosition = Vector3.up * Mathf.Max(0.3f, _currentHeight - eyeBelowTop);

        public void Teleport(Vector3 position)
        {
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            _velocity = Vector3.zero;
            ExternalVelocity = Vector3.zero;
            ParachuteOpen = false;
            _parachuteCut = false;
            Attached = false;
            ResetEye();
            _ignoreNextLanding = true; // появление сверху острова — не «падение»
            _airPeakY = position.y;
            // без телепорта NetworkTransform чужие копии «проплывут» через всю карту
            if (TryGetComponent(out NetworkTransform nt) && nt.IsSpawned && nt.CanCommitToTransform)
                nt.Teleport(position, transform.rotation, transform.localScale);
        }
    }
}
