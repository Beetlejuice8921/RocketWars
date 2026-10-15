#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RocketWars.Dev
{
    /// <summary>
    /// Кинокамера для ролика: рисует поверх игровой (та на время дубля выключается).
    /// Режимы: следовать за объектом (смещение в его осях или мировых), неподвижная, проезд A→B, облёт точки.
    /// </summary>
    public class FilmCamera : MonoBehaviour
    {
        public enum Mode { Follow, Fixed, Dolly, Orbit }

        static FilmCamera _instance;
        Camera _cam, _main;

        public Mode mode;
        public Transform target;
        public Vector3 offset, lookOffset;
        public bool offsetInTargetSpace = true;
        public float smooth = 6f;
        public Vector3 posA, posB, lookA, lookB;
        public float duration = 3f, orbitRadius = 20f, orbitHeight = 8f, orbitSpeed = 20f, orbitAngle;
        float _t;

        public static FilmCamera Get()
        {
            if (_instance != null)
                return _instance;
            var go = new GameObject("FilmCamera");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FilmCamera>();
            _instance._cam = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>();
            return _instance;
        }

        /// <summary>Включить кинокамеру (игровая гаснет), настройки неба/дальности — как у игровой.</summary>
        public FilmCamera On(float fov = 60f)
        {
            _main = Camera.main != null && Camera.main != _cam ? Camera.main : _main;
            if (_main != null)
            {
                _cam.CopyFrom(_main);
                _cam.targetTexture = null;
                var src = _main.GetComponent<UniversalAdditionalCameraData>();
                var dst = _cam.GetComponent<UniversalAdditionalCameraData>();
                if (src != null && dst != null)
                {
                    dst.renderPostProcessing = src.renderPostProcessing;
                    dst.antialiasing = src.antialiasing;
                }
                _main.enabled = false;
            }
            _cam.fieldOfView = fov;
            _cam.depth = 100;
            _cam.enabled = true;
            _t = 0f;
            return this;
        }

        public void Off()
        {
            _cam.enabled = false;
            if (_main != null)
                _main.enabled = true;
            target = null;
        }

        public FilmCamera Follow(Transform t, Vector3 off, Vector3 look, bool targetSpace = true, float smoothing = 6f)
        {
            mode = Mode.Follow; target = t; offset = off; lookOffset = look; offsetInTargetSpace = targetSpace; smooth = smoothing;
            if (t != null) Snap();
            return this;
        }

        public FilmCamera Fixed(Vector3 pos, Vector3 look)
        {
            mode = Mode.Fixed; posA = pos; lookA = look;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            return this;
        }

        public FilmCamera Dolly(Vector3 a, Vector3 b, Vector3 lookFrom, Vector3 lookTo, float seconds)
        {
            mode = Mode.Dolly; posA = a; posB = b; lookA = lookFrom; lookB = lookTo; duration = seconds; _t = 0f;
            transform.SetPositionAndRotation(a, Quaternion.LookRotation(lookFrom - a));
            return this;
        }

        public FilmCamera Orbit(Vector3 center, float radius, float height, float degPerSec, float startAngle)
        {
            mode = Mode.Orbit; lookA = center; orbitRadius = radius; orbitHeight = height; orbitSpeed = degPerSec; orbitAngle = startAngle;
            return this;
        }

        void Snap()
        {
            Vector3 pos = offsetInTargetSpace ? target.TransformPoint(offset) : target.position + offset;
            Vector3 look = offsetInTargetSpace ? target.TransformPoint(lookOffset) : target.position + lookOffset;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.enabled)
                return;
            float dt = Time.unscaledDeltaTime;
            switch (mode)
            {
                case Mode.Follow:
                    if (target == null) return;
                    Vector3 pos = offsetInTargetSpace ? target.TransformPoint(offset) : target.position + offset;
                    Vector3 look = offsetInTargetSpace ? target.TransformPoint(lookOffset) : target.position + lookOffset;
                    float k = 1f - Mathf.Exp(-smooth * dt);
                    transform.position = Vector3.Lerp(transform.position, pos, k);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - transform.position), k);
                    break;
                case Mode.Dolly:
                    _t += dt;
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / duration));
                    Vector3 p = Vector3.Lerp(posA, posB, u);
                    transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.Lerp(lookA, lookB, u) - p));
                    break;
                case Mode.Orbit:
                    orbitAngle += orbitSpeed * dt;
                    var dir = Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward;
                    Vector3 o = lookA + dir * orbitRadius + Vector3.up * orbitHeight;
                    transform.SetPositionAndRotation(o, Quaternion.LookRotation(lookA - o));
                    break;
            }
        }
    }
}
#endif
