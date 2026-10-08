using UnityEngine;

namespace RocketWars.Fx
{
    /// <summary>Тряска камеры от близких взрывов (сила падает с расстоянием).</summary>
    public class CameraShake : MonoBehaviour
    {
        const float MaxOffset = 0.35f;
        const float Decay = 1.4f;

        static CameraShake _instance;
        float _trauma;

        void OnEnable() => _instance = this;

        void OnDisable()
        {
            if (_instance == this)
                _instance = null;
            transform.localPosition = Vector3.zero;
        }

        /// <summary>Встряска от события в мировой точке: до 15 м — сильно, дальше 80 м — не ощущается.</summary>
        public static void AddAt(Vector3 worldPos, float strength = 1f)
        {
            if (_instance == null)
                return;
            float d = Vector3.Distance(_instance.transform.position, worldPos);
            float k = 1f - Mathf.InverseLerp(15f, 80f, d);
            _instance._trauma = Mathf.Clamp01(_instance._trauma + strength * k);
        }

        void LateUpdate()
        {
            if (_trauma <= 0f)
                return;
            _trauma = Mathf.Max(0f, _trauma - Decay * Time.deltaTime);
            float s = _trauma * _trauma * MaxOffset;
            transform.localPosition = new Vector3(
                (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 2f * s,
                (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 2f * s,
                0f);
        }
    }
}
