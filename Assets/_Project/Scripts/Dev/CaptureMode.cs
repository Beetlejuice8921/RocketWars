using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Net;
using RocketWars.Player;
using RocketWars.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RocketWars.Dev
{
    /// <summary>
    /// Режим съёмки роликов (редактор и dev-сборки): F8 — своя камера поверх игровой, игрок стоит на месте.
    /// Свободный полёт, слежение за игроком или ракетой с облётом мышью, автооблёт; плавность, зум, скрытие HUD,
    /// замедление и стоп-кадр (только в одиночной игре — иначе разъедется сеть). Звук слышен из точки камеры.
    /// Игровая камера не выключается (Camera.main остаётся у игрока) — ей только обнуляется маска слоёв.
    /// </summary>
    public class CaptureMode : MonoBehaviour
    {
        enum Mode { Free, Follow, Orbit }

        const float MinSpeed = 1f, MaxSpeed = 120f;
        const float MinFov = 10f, MaxFov = 100f;
        const float LookSpeed = 0.1f;
        const float HintTime = 6f;
        static readonly float[] TimeScales = { 1f, 0.5f, 0.25f, 0.1f, 0f };

        static CaptureMode _instance;

        /// <summary>Режим включён: игровой ввод заблокирован, кадр рисует камера съёмки.</summary>
        public static bool Active => _instance != null && _instance._active;

        /// <summary>Камера, из которой сейчас смотрит экран (для билбордов: облака, трассеры, блики).</summary>
        public static Camera ViewCamera => Active ? _instance._cam : Camera.main;

        Camera _cam, _main;
        AudioListener _listener, _mainListener;
        int _mainMask;
        bool _active, _smooth = true, _hint = true, _hideHud = true;
        float _hintUntil;
        Mode _mode;
        Transform _target;
        string _targetName;
        int _targetIndex = -1, _timeIndex;

        // желаемое (ввод) и сглаженное (на экране) положение камеры
        Vector3 _wantPos, _pos;
        float _wantYaw, _wantPitch, _yaw, _pitch, _wantFov = 60f, _fov = 60f;
        float _speed = 10f, _distance = 8f, _orbitSpeed = 12f;
        Vector3 _followOffset;

        readonly List<Renderer> _body = new List<Renderer>();
        readonly List<Renderer> _viewModel = new List<Renderer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (!Debug.isDebugBuild || _instance != null)
                return;
            var go = new GameObject("CaptureMode");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CaptureMode>();
        }

        void Awake()
        {
            _cam = gameObject.AddComponent<Camera>();
            gameObject.AddComponent<UniversalAdditionalCameraData>();
            _cam.enabled = false;
            _listener = gameObject.AddComponent<AudioListener>();
            _listener.enabled = false;
        }

        void OnDestroy()
        {
            if (_active)
                Exit();
            if (_instance == this)
                _instance = null;
        }

        static bool InGame
        {
            get
            {
                var net = NetworkBootstrap.Instance;
                return net != null && net.IsRunning && NetworkPlayer.LocalPlayer != null;
            }
        }

        /// <summary>Замедлять время можно, только если других живых игроков нет (хост с ботами или офлайн).</summary>
        static bool CanSlowTime
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer && nm.ConnectedClientsIds.Count <= 1;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;
            if (_active && (!InGame || Camera.main == null))
            {
                Exit();
                return;
            }
            if (kb.f8Key.wasPressedThisFrame && InGame)
            {
                if (_active) Exit();
                else Enter();
            }
            if (!_active)
                return;
            // пауза (Esc) открыта — мышь у интерфейса, камеру не крутим
            if (UiRoot.CursorFree)
                return;

            HandleKeys(kb);
            HandleMovement(kb);
        }

        void Enter()
        {
            _main = Camera.main;
            _cam.CopyFrom(_main);
            _cam.targetTexture = null;
            var src = _main.GetComponent<UniversalAdditionalCameraData>();
            var dst = _cam.GetComponent<UniversalAdditionalCameraData>();
            if (src != null && dst != null)
            {
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
            }
            _cam.depth = _main.depth + 10f;
            _cam.nearClipPlane = 0.1f;
            _cam.enabled = true;
            _mainMask = _main.cullingMask;
            _main.cullingMask = 0;

            _mainListener = _main.GetComponent<AudioListener>();
            if (_mainListener != null)
                _mainListener.enabled = false;
            _listener.enabled = true;

            // старт ровно с вида игрока (без крена от тряски)
            var t = _main.transform;
            var euler = t.rotation.eulerAngles;
            _pos = _wantPos = t.position;
            _yaw = _wantYaw = euler.y;
            _pitch = _wantPitch = Mathf.DeltaAngle(0f, euler.x);
            _fov = _wantFov = 60f;
            _mode = Mode.Free;
            _target = null;
            _targetIndex = -1;
            _timeIndex = 0;

            CollectPlayerRenderers();
            _active = true;
            GameInput.Blocked = true;
            UiRoot.HudHidden = _hideHud;
            ShowHint();
            Apply();
        }

        void Exit()
        {
            _active = false;
            GameInput.Blocked = false;
            UiRoot.HudHidden = false;
            Time.timeScale = 1f;
            _cam.enabled = false;
            _listener.enabled = false;
            if (_main != null)
                _main.cullingMask = _mainMask;
            if (_mainListener != null)
                _mainListener.enabled = true;
            RestorePlayerRenderers();
            _main = null;
            _mainListener = null;
        }

        /// <summary>Своё тело в игре рисуется только тенью, а оружие в руке висит перед игровой камерой — со стороны их надо поправить.</summary>
        void CollectPlayerRenderers()
        {
            _body.Clear();
            _viewModel.Clear();
            var me = NetworkPlayer.LocalPlayer;
            var body = me != null ? me.transform.Find("Body") : null;
            if (body != null)
                body.GetComponentsInChildren(true, _body);
            _main.GetComponentsInChildren(true, _viewModel);
        }

        void RestorePlayerRenderers()
        {
            var me = NetworkPlayer.LocalPlayer;
            var downed = me != null ? me.GetComponent<DownedCamera>() : null;
            var mode = downed != null && downed.BodyShown ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var r in _body)
                if (r != null)
                    r.shadowCastingMode = mode;
            foreach (var r in _viewModel)
                if (r != null)
                    r.forceRenderingOff = false;
            _body.Clear();
            _viewModel.Clear();
        }

        void HandleKeys(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame) SetMode(Mode.Free);
            if (kb.digit2Key.wasPressedThisFrame) SetMode(Mode.Follow);
            if (kb.digit3Key.wasPressedThisFrame) SetMode(Mode.Orbit);
            if (kb.tKey.wasPressedThisFrame) NextTarget();
            if (kb.gKey.wasPressedThisFrame) _smooth = !_smooth;
            if (kb.hKey.wasPressedThisFrame)
            {
                if (Time.unscaledTime < _hintUntil) _hintUntil = 0f;
                else ShowHint();
            }
            if (kb.uKey.wasPressedThisFrame)
                UiRoot.HudHidden = _hideHud = !_hideHud;

            if (kb.periodKey.wasPressedThisFrame) SetTimeIndex(_timeIndex + 1);
            if (kb.commaKey.wasPressedThisFrame) SetTimeIndex(_timeIndex - 1);
            if (_timeIndex != 0 && !CanSlowTime)
                SetTimeIndex(0); // в комнату зашёл живой игрок

            float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            if (scroll != 0f)
            {
                float step = scroll > 0f ? 1.2f : 1f / 1.2f;
                if (_mode == Mode.Free) _speed = Mathf.Clamp(_speed * step, MinSpeed, MaxSpeed);
                else _distance = Mathf.Clamp(_distance / step, 2f, 80f);
            }
            float zoom = (kb.xKey.isPressed ? 1f : 0f) - (kb.zKey.isPressed ? 1f : 0f);
            if (zoom != 0f)
                _wantFov = Mathf.Clamp(_wantFov * Mathf.Pow(1.6f, zoom * Time.unscaledDeltaTime), MinFov, MaxFov);
            if (kb.cKey.wasPressedThisFrame)
                _wantFov = 60f;
        }

        void SetTimeIndex(int index)
        {
            index = Mathf.Clamp(index, 0, TimeScales.Length - 1);
            if (index != 0 && !CanSlowTime)
            {
                ShowHint();
                return;
            }
            _timeIndex = index;
            Time.timeScale = TimeScales[index];
        }

        void SetMode(Mode mode)
        {
            if (mode != Mode.Free && _target == null)
                NextTarget();
            if (mode != Mode.Free && _target == null)
                return; // следить не за кем
            if (mode != Mode.Free && _mode == Mode.Free)
                AimAtTarget();
            _mode = mode;
        }

        /// <summary>Облетать с текущей точки: расстояние и углы — от камеры до цели (без рывка при переключении).</summary>
        void AimAtTarget()
        {
            _followOffset = Vector3.zero;
            Vector3 toTarget = TargetPoint - _pos;
            if (toTarget.sqrMagnitude < 0.01f)
                toTarget = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            _distance = Mathf.Clamp(toTarget.magnitude, 2f, 80f);
            var dir = toTarget.normalized;
            _wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _wantPitch = Mathf.Clamp(-Mathf.Asin(dir.y) * Mathf.Rad2Deg, -89f, 89f);
        }

        /// <summary>Цели по кругу: ракеты в полёте (новые первыми), свой игрок, остальные игроки.</summary>
        void NextTarget()
        {
            var list = new List<(Transform t, string name)>();
            for (int i = Rocket.Active.Count - 1; i >= 0; i--)
                if (Rocket.Active[i] != null)
                    list.Add((Rocket.Active[i].transform, "ракета"));
            var me = NetworkPlayer.LocalPlayer;
            if (me != null)
                list.Add((me.transform, "свой игрок"));
            foreach (var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.InstanceID))
                if (p != me)
                    list.Add((p.transform, p.IsBot ? "бот" : "игрок"));
            if (list.Count == 0)
            {
                _target = null;
                return;
            }
            _targetIndex = (_targetIndex + 1) % list.Count;
            (_target, _targetName) = list[_targetIndex];
            AimAtTarget();
            if (_mode == Mode.Free)
                _mode = Mode.Follow;
        }

        Vector3 TargetPoint => _target != null ? _target.position + Vector3.up * 1.4f : _pos;

        void HandleMovement(Keyboard kb)
        {
            float dt = Time.unscaledDeltaTime;
            var mouse = Mouse.current;
            Vector2 look = mouse != null ? mouse.delta.ReadValue() * (LookSpeed * GameInput.Sensitivity) : Vector2.zero;
            _wantYaw += look.x;
            _wantPitch = Mathf.Clamp(_wantPitch - look.y, -89f, 89f);

            if (_mode != Mode.Free && _target == null)
                _mode = Mode.Free; // ракета взорвалась — остаёмся там, где были

            Vector3 move = new Vector3(
                (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                (kb.eKey.isPressed || kb.spaceKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0),
                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            float speed = _speed * (kb.leftShiftKey.isPressed ? 4f : 1f) * (kb.leftAltKey.isPressed ? 0.25f : 1f);

            switch (_mode)
            {
                case Mode.Free:
                    var rot = Quaternion.Euler(_wantPitch, _wantYaw, 0f);
                    _wantPos += (rot * new Vector3(move.x, 0f, move.z) + Vector3.up * move.y) * (speed * dt);
                    break;
                case Mode.Orbit:
                    _wantYaw += _orbitSpeed * dt;
                    // A/D — скорость облёта, W/S — ближе/дальше
                    _orbitSpeed = Mathf.Clamp(_orbitSpeed + move.x * 20f * dt, -60f, 60f);
                    _distance = Mathf.Clamp(_distance - move.z * 10f * dt, 2f, 80f);
                    _wantPos = OrbitPosition();
                    break;
                case Mode.Follow:
                    // WASD сдвигает точку, вокруг которой облетаем (камера над плечом, впереди ракеты…)
                    var flat = Quaternion.Euler(0f, _wantYaw, 0f);
                    _followOffset += (flat * new Vector3(move.x, 0f, move.z) + Vector3.up * move.y) * (2f * dt);
                    _wantPos = OrbitPosition();
                    break;
            }
        }

        Vector3 OrbitPosition() =>
            TargetPoint + _followOffset - Quaternion.Euler(_wantPitch, _wantYaw, 0f) * Vector3.forward * _distance;

        void ShowHint()
        {
            _hint = true;
            _hintUntil = Time.unscaledTime + HintTime;
        }

        void LateUpdate()
        {
            if (!_active)
                return;
            // за целью камера идёт в LateUpdate — цель уже сдвинулась в этом кадре
            if (_mode != Mode.Free && _target != null)
                _wantPos = OrbitPosition();
            Apply();
            // DownedCamera и смена оружия могли переписать — держим своё
            foreach (var r in _body)
                if (r != null && r.shadowCastingMode != ShadowCastingMode.On)
                    r.shadowCastingMode = ShadowCastingMode.On;
            foreach (var r in _viewModel)
                if (r != null)
                    r.forceRenderingOff = true;
        }

        void Apply()
        {
            float dt = Time.unscaledDeltaTime;
            bool follow = _mode != Mode.Free;
            // плавно — как с операторского крана; за целью позицию держим жёстче, иначе ракета уходит из кадра
            float k = _smooth ? 1f - Mathf.Exp(-(follow ? 10f : 4f) * dt) : 1f;
            float kLook = _smooth ? 1f - Mathf.Exp(-6f * dt) : 1f;
            _pos = Vector3.Lerp(_pos, _wantPos, k);
            _yaw = Mathf.LerpAngle(_yaw, _wantYaw, kLook);
            _pitch = Mathf.Lerp(_pitch, _wantPitch, kLook);
            _fov = Mathf.Lerp(_fov, _wantFov, kLook);
            _cam.transform.SetPositionAndRotation(_pos, follow
                ? Quaternion.LookRotation(TargetPoint + _followOffset - _pos)
                : Quaternion.Euler(_pitch, _yaw, 0f));
            _cam.fieldOfView = _fov;
        }

        void OnGUI()
        {
            if (!_active || !_hint)
                return;
            if (Time.unscaledTime > _hintUntil)
            {
                _hint = false;
                return;
            }
            string target = _target != null ? _targetName : "нет";
            string mode = _mode switch { Mode.Follow => "слежение", Mode.Orbit => "облёт", _ => "свободная" };
            string time = CanSlowTime ? $"×{TimeScales[_timeIndex]:0.##}" : "только в одиночной игре";
            string text =
                $"РЕЖИМ СЪЁМКИ — {mode}, цель: {target}\n" +
                "F8 выход   H подсказка   U интерфейс игры   G плавность: " + (_smooth ? "вкл" : "выкл") + "\n" +
                "1 свободная   2 слежение   3 облёт   T следующая цель (ракеты, игроки)\n" +
                "WASD, E/Пробел вверх, Q вниз, Shift быстрее, Alt медленнее\n" +
                $"Колесо: скорость {_speed:0} / дистанция {_distance:0}   Z/X зум, C сброс зума ({_fov:0}°)\n" +
                $", . замедление времени: {time}";
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, padding = new RectOffset(12, 12, 10, 10) };
            style.normal.textColor = Color.white;
            var size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(16f, 16f, size.x, size.y), text, style);
        }
    }
}
