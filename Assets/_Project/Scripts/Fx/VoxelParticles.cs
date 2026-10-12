using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketWars.Fx
{
    /// <summary>
    /// Воксельные частицы (кубики) без GameObject'ов: симуляция в массиве, отрисовка через GPU-инстансинг
    /// пачками по цветам палитры. Из них собраны гриб взрыва, дымный след и обломки террейна.
    /// </summary>
    public class VoxelParticles : MonoBehaviour
    {
        public const int MaxParticles = 6000;
        const int BatchSize = 1023;

        // палитра: индекс = материал
        public const byte Smoke0 = 0, Smoke1 = 1, Smoke2 = 2, Smoke3 = 3, Fire0 = 4, Fire1 = 5, Grass = 6, GrassDark = 7, Dirt = 8, DirtDark = 9,
            HitRed = 10, HitRedDark = 11, Stone = 12, StoneDark = 13, Sand = 14, SandDark = 15, Sandstone = 16, SandstoneDark = 17,
            Snow = 18, SnowDark = 19, ColdStone = 20, ColdStoneDark = 21;
        public const byte NoColor = 255;

        /// <summary>Дым (Smoke0..Smoke3) — белый, полупрозрачный и без теней; остальное — непрозрачные кубики с тенью.</summary>
        const float SmokeAlpha = 0.55f;
        static bool IsSmoke(int color) => color <= Smoke3;

        static readonly Color[] Palette =
        {
            new Color(1f, 1f, 1f, SmokeAlpha), new Color(0.95f, 0.95f, 0.96f, SmokeAlpha),
            new Color(0.89f, 0.9f, 0.91f, SmokeAlpha), new Color(0.82f, 0.83f, 0.85f, SmokeAlpha),
            new Color(1f, 0.55f, 0.12f), new Color(1f, 0.85f, 0.3f),
            new Color32(118, 186, 72, 255), new Color32(92, 158, 56, 255), new Color32(121, 85, 58, 255), new Color32(95, 66, 45, 255),
            new Color(0.9f, 0.12f, 0.08f), new Color(0.55f, 0.05f, 0.05f), // попадание пули в игрока
            new Color(0.62f, 0.62f, 0.6f), new Color(0.45f, 0.45f, 0.44f),  // обломки камня
            new Color32(226, 198, 134, 255), new Color32(196, 160, 100, 255), new Color32(196, 140, 88, 255), new Color32(160, 108, 68, 255), // пустыня
            new Color32(240, 244, 250, 255), new Color32(208, 218, 234, 255), new Color32(132, 138, 148, 255), new Color32(96, 102, 112, 255), // снег
        };

        struct Particle
        {
            public Vector3 Pos, Vel, Spin;
            public Quaternion Rot;
            public float Age, Life, Size0, Size1, GrowEnd, ShrinkStart, Gravity, Drag, ColorSwitch;
            public byte Color, ColorLate;
            public bool Collide;
        }

        static VoxelParticles _instance;

        readonly Particle[] _particles = new Particle[MaxParticles];
        int _count;
        Mesh _cube;
        Material[] _materials;
        Matrix4x4[][] _batches;
        int[] _batchCounts;

        public static VoxelParticles Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new GameObject("VoxelParticles").AddComponent<VoxelParticles>();
                return _instance;
            }
        }

        public int Count => _count;

        /// <summary>Без видеокарты (сервер, -nographics) частицы не нужны вовсе.</summary>
        static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        /// <summary>Старые видеокарты без инстансинга рисуют кубики по одному.</summary>
        static readonly bool Instancing = SystemInfo.supportsInstancing;

        void Awake()
        {
            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _materials = new Material[Palette.Length];
            _batches = new Matrix4x4[Palette.Length][];
            _batchCounts = new int[Palette.Length];
            // Основа — ассет Resources/VoxelParticle (URP Lit с включённым GPU Instancing): без ассета с инстансингом
            // сборка вырезает instancing-вариант шейдера (Instancing Variants: Strip Unused), и в билде частиц не видно вовсе
            // дым — от ассета Resources/VoxelSmoke (тот же Lit, но прозрачный, тоже с инстансингом)
            var template = Resources.Load<Material>("VoxelParticle");
            var smokeTemplate = Resources.Load<Material>("VoxelSmoke");
            for (int i = 0; i < Palette.Length; i++)
            {
                var source = IsSmoke(i) && smokeTemplate != null ? smokeTemplate : template;
                _materials[i] = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _materials[i].enableInstancing = SystemInfo.supportsInstancing;
                _materials[i].name = $"VoxelFx {i}";
                _materials[i].SetColor("_BaseColor", Palette[i]);
                _materials[i].SetFloat("_Smoothness", 0.05f);
                _batches[i] = new Matrix4x4[BatchSize];
            }
        }

        public static byte RandomSmoke() => (byte)Random.Range(Smoke0, Smoke3 + 1);

        /// <summary>
        /// Выпустить кубик. Размер растёт size0→size1 до growEnd (доля жизни), держится, а с shrinkStart уменьшается до нуля.
        /// colorLate/colorSwitch — смена цвета (огонь → дым).
        /// </summary>
        public void Emit(Vector3 pos, Vector3 vel, float life, float size0, float size1, byte color,
            float gravity = 0f, float drag = 0f, float growEnd = 0.2f, float shrinkStart = 0.6f,
            byte colorLate = NoColor, float colorSwitch = 0f, bool collide = false)
        {
            if (_count >= MaxParticles || !HasGraphics)
                return;
            _particles[_count++] = new Particle
            {
                Pos = pos,
                Vel = vel,
                Spin = Random.insideUnitSphere * 180f,
                Rot = Random.rotationUniform,
                Life = life,
                Size0 = size0,
                Size1 = size1,
                GrowEnd = Mathf.Max(0.001f, growEnd),
                ShrinkStart = shrinkStart,
                Gravity = gravity,
                Drag = drag,
                Color = color,
                ColorLate = colorLate,
                ColorSwitch = colorSwitch,
                Collide = collide
            };
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    _particles[i] = _particles[--_count];
                    i--;
                    continue;
                }

                p.Vel.y -= p.Gravity * dt;
                if (p.Drag > 0f)
                    p.Vel *= Mathf.Max(0f, 1f - p.Drag * dt);

                Vector3 step = p.Vel * dt;
                if (p.Collide && step.sqrMagnitude > 1e-6f &&
                    Physics.Raycast(p.Pos, step, out var hit, step.magnitude + 0.15f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // отскок с потерей скорости, потом кубик «ложится»
                    p.Pos = hit.point + hit.normal * 0.15f;
                    p.Vel = Vector3.Reflect(p.Vel, hit.normal) * 0.3f;
                    p.Spin *= 0.5f;
                    if (p.Vel.sqrMagnitude < 1f)
                    {
                        p.Vel = Vector3.zero;
                        p.Gravity = 0f;
                        p.Spin = Vector3.zero;
                    }
                }
                else
                {
                    p.Pos += step;
                }

                if (p.Spin != Vector3.zero)
                    p.Rot = Quaternion.Normalize(Quaternion.Euler(p.Spin * dt) * p.Rot); // без нормализации копится погрешность
            }
        }

        void LateUpdate()
        {
            for (int c = 0; c < _batchCounts.Length; c++)
                _batchCounts[c] = 0;

            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _particles[i];
                float t = p.Age / p.Life;
                float size = t < p.GrowEnd
                    ? Mathf.Lerp(p.Size0, p.Size1, t / p.GrowEnd)
                    : t > p.ShrinkStart ? Mathf.Lerp(p.Size1, 0f, (t - p.ShrinkStart) / Mathf.Max(0.001f, 1f - p.ShrinkStart)) : p.Size1;
                if (size <= 0.01f)
                    continue;

                int color = p.ColorLate != NoColor && t >= p.ColorSwitch ? p.ColorLate : p.Color;
                _batches[color][_batchCounts[color]++] = Matrix4x4.TRS(p.Pos, p.Rot, new Vector3(size, size, size));
                if (_batchCounts[color] == BatchSize)
                    Flush(color);
            }

            for (int c = 0; c < _batchCounts.Length; c++)
                Flush(c);
        }

        void Flush(int color)
        {
            int n = _batchCounts[color];
            if (n == 0)
                return;
            bool smoke = IsSmoke(color);
            var rp = new RenderParams(_materials[color])
            {
                shadowCastingMode = smoke ? ShadowCastingMode.Off : ShadowCastingMode.On,
                receiveShadows = !smoke,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 2000f)
            };
            if (Instancing)
            {
                Graphics.RenderMeshInstanced(rp, _cube, 0, _batches[color], n);
            }
            else
            {
                var batch = _batches[color];
                for (int i = 0; i < n; i++)
                    Graphics.RenderMesh(rp, _cube, 0, batch[i]);
            }
            _batchCounts[color] = 0;
        }

        public void Clear() => _count = 0;

        void OnDestroy()
        {
            if (_materials != null)
                foreach (var m in _materials)
                    Destroy(m);
            if (_instance == this)
                _instance = null;
        }
    }
}
