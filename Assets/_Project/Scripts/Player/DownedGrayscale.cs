using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RocketWars.Player
{
    /// <summary>
    /// В момент, когда локального игрока сбивают с ног, картинка наполовину теряет цвет и за 2 с плавно его возвращает.
    /// Пост-обработка камеры обычно выключена, поэтому включаем её только на время эффекта, а свой глобальный Volume
    /// гасит блум, тонмаппинг и виньетку из профиля пайплайна — меняется одна насыщенность.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class DownedGrayscale : MonoBehaviour
    {
        const float StartSaturation = -50f;
        const float Duration = 2f;

        public Camera cam;

        PlayerHealth _health;
        Volume _volume;
        ColorAdjustments _color;
        bool _wasDowned, _enabledPost;
        float _t = Duration;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _wasDowned = _health.IsDowned;
        }

        void LateUpdate()
        {
            bool downed = _health.IsDowned;
            if (downed && !_wasDowned)
                _t = 0f;
            _wasDowned = downed;

            if (_t >= Duration)
                return;
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Duration);
            if (k >= 1f)
            {
                SetActive(false);
                return;
            }
            SetActive(true);
            _color.saturation.value = Mathf.Lerp(StartSaturation, 0f, Mathf.SmoothStep(0f, 1f, k));
        }

        void SetActive(bool on)
        {
            if (on && _volume == null)
                CreateVolume();
            if (_volume != null)
                _volume.enabled = on;

            var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            if (data == null)
                return;
            if (on && !data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
                _enabledPost = true;
            }
            else if (!on && _enabledPost)
            {
                data.renderPostProcessing = false;
                _enabledPost = false;
            }
        }

        void CreateVolume()
        {
            var go = new GameObject("DownedGrayscaleVolume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.sharedProfile = profile;

            _color = profile.Add<ColorAdjustments>();
            _color.saturation.Override(StartSaturation);
            profile.Add<Bloom>().intensity.Override(0f);
            profile.Add<Tonemapping>().mode.Override(TonemappingMode.None);
            profile.Add<Vignette>().intensity.Override(0f);
        }

        void OnDestroy()
        {
            SetActive(false);
            if (_volume != null)
                Destroy(_volume.sharedProfile);
        }
    }
}
