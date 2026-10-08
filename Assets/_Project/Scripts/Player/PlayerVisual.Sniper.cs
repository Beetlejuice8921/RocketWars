using RocketWars.Core;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Снайперка: поза с оптикой у глаз (обе руки вскинуты) и блик линзы — полупрозрачная мигающая пиксельная звёздочка
    /// перед лицом снайпера, которую видят только игроки другой команды. Чем дальше смотрящий, тем крупнее блик,
    /// чтобы его можно было заметить через весь пролив.
    /// </summary>
    public partial class PlayerVisual
    {
        const float GlintBlinkHz = 2.2f;
        const float GlintMinSize = 0.6f, GlintSizePerMeter = 0.04f; // вдали ~3% высоты экрана
        static readonly Color GlintColor = Color.white; // цвета — в текстуре
        static readonly Quaternion ScopeArmL = Quaternion.Euler(0f, 28f, 0f) * Quaternion.Euler(-80f, 0f, 0f);
        static Texture2D _glintTexture;

        Transform _glint;
        Material _glintMat;
        float _glintAmount, _scopePose;

        void InitGlint()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "SniperGlint";
            _glint = quad.transform;
            _glint.SetParent(transform, false);
            // обычная прозрачность (Resources/Cloud — переживает сборку): «сложением» блик терялся на светлом небе
            var template = Resources.Load<Material>("Cloud");
            _glintMat = template != null ? new Material(template) : new Material(RuntimeMaterials.Emissive(GlintColor, 2f));
            _glintMat.SetTexture("_BaseMap", GlintTexture());
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = _glintMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad.SetActive(false);
        }

        /// <summary>Блик виден врагам, пока снайпер смотрит в оптику; мигает и смотрит в камеру.</summary>
        void UpdateGlint(float dt)
        {
            bool aiming = _net != null && _net.SniperAiming && !_health.IsDowned;
            _scopePose = Mathf.MoveTowards(_scopePose, aiming ? 1f : 0f, dt / Binoculars.RaiseTime);

            var me = NetworkPlayer.LocalPlayer;
            bool enemy = me != null && me != _net && me.Health != null && me.Health.Team != _health.Team;
            _glintAmount = Mathf.MoveTowards(_glintAmount, aiming && enemy ? 1f : 0f, dt * 4f);
            var cam = Camera.main;
            bool show = _glintAmount > 0f && cam != null;
            if (_glint.gameObject.activeSelf != show)
                _glint.gameObject.SetActive(show);
            if (!show)
                return;

            // от центра головы к смотрящему — не прячется за головой; спереди (куда смотрит оптика) ярче
            Vector3 head = _body.TransformPoint(Models.SniperGlintPoint);
            Vector3 toCam = cam.transform.position - head;
            float dist = toCam.magnitude;
            Vector3 dirToCam = toCam / Mathf.Max(dist, 0.01f);
            Vector3 pos = head + dirToCam * 0.45f;
            float front = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Vector3.Dot(transform.forward, dirToCam) + 0.3f));
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * GlintBlinkHz * 2f * Mathf.PI);
            blink *= blink;
            float size = Mathf.Max(GlintMinSize, dist * GlintSizePerMeter) * (0.7f + 0.5f * blink);
            _glint.position = pos;
            _glint.rotation = Quaternion.LookRotation(-toCam, cam.transform.up);
            _glint.localScale = Vector3.one * size;
            var c = GlintColor;
            c.a = (0.35f + 0.65f * blink) * _glintAmount * front;
            _glintMat.SetColor("_BaseColor", c);
        }

        /// <summary>Оптика у глаз: правая рука вскидывает винтовку вперёд, левая поддерживает ствол.</summary>
        void ApplyScopePose()
        {
            if (_scopePose <= 0f)
                return;
            float k = _scopePose * _scopePose * (3f - 2f * _scopePose);
            _armR.localRotation = Quaternion.Slerp(_armR.localRotation, AimRotation, k);
            _armL.localRotation = Quaternion.Slerp(_armL.localRotation, ScopeArmL, k);
        }

        /// <summary>Пиксельная звёздочка 15×15: яркое ядро, длинные лучи крестом и короткие по диагоналям.</summary>
        static Texture2D GlintTexture()
        {
            if (_glintTexture != null)
                return _glintTexture;
            const int n = 15, c = n / 2;
            _glintTexture = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SniperGlint"
            };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
                float a = 0f;
                if (dx <= 1 && dy <= 1)
                    a = 1f;                                           // ядро 3×3
                else if (dx == 0 || dy == 0)
                    a = 1f - Mathf.Max(dx, dy) / (c + 1f);            // длинный крест
                else if (dx == dy && dx <= 3)
                    a = 0.6f - dx * 0.12f;                            // короткие диагонали
                else if (dx + dy <= 3)
                    a = 0.35f;                                        // ореол вокруг ядра
                // ступеньками, по-пиксельному; ядро белое, к концам лучей — жёлтое и оранжевое (видно и на небе)
                a = Mathf.Round(a * 4f) / 4f;
                int r = Mathf.Max(dx, dy);
                Color col = r <= 1 ? Color.white : r <= 3 ? new Color(1f, 0.93f, 0.55f) : new Color(1f, 0.7f, 0.2f);
                col.a = a;
                _glintTexture.SetPixel(x, y, col);
            }
            _glintTexture.Apply(false, true);
            return _glintTexture;
        }
    }
}
