using System.Collections.Generic;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Fx
{
    /// <summary>Готовые воксельные эффекты: гриб взрыва, обломки террейна, дым ракеты, вспышка.</summary>
    public static class VoxelFx
    {
        const int MaxDebrisBlocks = 45;

        static VoxelParticles P => VoxelParticles.Instance;

        /// <summary>Воксельный «гриб»: огненный шар, ножка из дыма и расползающаяся шапка.</summary>
        public static void Mushroom(Vector3 pos)
        {
            // огненный шар
            for (int i = 0; i < 45; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f;
                P.Emit(pos + dir * 0.5f, dir * Random.Range(6f, 13f), Random.Range(1.1f, 1.8f), 1.2f, Random.Range(1.6f, 2.4f),
                    Random.value < 0.5f ? VoxelParticles.Fire0 : VoxelParticles.Fire1, drag: 3f, growEnd: 0.15f, shrinkStart: 0.55f,
                    colorLate: VoxelParticles.RandomSmoke(), colorSwitch: Random.Range(0.2f, 0.35f));
            }

            // ножка: кубики поднимаются и тормозят на разной высоте
            for (int i = 0; i < 55; i++)
            {
                Vector2 r = Random.insideUnitCircle * 1.4f;
                P.Emit(pos + new Vector3(r.x, 0.5f, r.y), new Vector3(r.x * 0.4f, Random.Range(6f, 14f), r.y * 0.4f),
                    Random.Range(3.5f, 5f), 1.1f, Random.Range(1.6f, 2.3f), VoxelParticles.Fire0, drag: 1.2f,
                    growEnd: 0.25f, shrinkStart: 0.65f, colorLate: VoxelParticles.RandomSmoke(), colorSwitch: Random.Range(0.08f, 0.18f));
            }

            // шапка: поднимается выше ножки и расползается в стороны
            for (int i = 0; i < 95; i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(2.5f, 7.5f);
                P.Emit(pos + new Vector3(0f, Random.Range(3f, 5f), 0f), new Vector3(r.x, Random.Range(10f, 13.5f), r.y),
                    Random.Range(4f, 6f), 1.4f, Random.Range(2.2f, 3.2f), VoxelParticles.Fire1, drag: 1f,
                    growEnd: 0.3f, shrinkStart: 0.7f, colorLate: VoxelParticles.RandomSmoke(), colorSwitch: Random.Range(0.06f, 0.12f));
            }

            Flash(pos);
        }

        /// <summary>Разлёт кубиков травы, земли и камня из разрушенных блоков.</summary>
        public static void Debris(Vector3 center, List<Vector3> blocks, List<BlockKind> kinds)
        {
            if (blocks == null || blocks.Count == 0)
                return;
            int stride = Mathf.Max(1, blocks.Count / MaxDebrisBlocks);
            for (int i = 0; i < blocks.Count; i += stride)
            {
                Vector3 b = blocks[i];
                Vector3 dir = b - center;
                dir.y = Mathf.Abs(dir.y) + 0.6f;
                dir.Normalize();
                bool alt = Random.value < 0.5f;
                byte color = kinds[i] switch
                {
                    BlockKind.Grass => alt ? VoxelParticles.Grass : VoxelParticles.GrassDark,
                    BlockKind.Stone => alt ? VoxelParticles.Stone : VoxelParticles.StoneDark,
                    _ => alt ? VoxelParticles.Dirt : VoxelParticles.DirtDark,
                };
                P.Emit(b, dir * Random.Range(7f, 15f) + Vector3.up * Random.Range(4f, 9f), Random.Range(2.5f, 4f),
                    Random.Range(0.35f, 0.55f), Random.Range(0.35f, 0.55f), color, gravity: 22f, growEnd: 0.01f, shrinkStart: 0.75f, collide: true);
            }
        }

        /// <summary>Клуб дыма за ракетой.</summary>
        public static void RocketSmoke(Vector3 pos, Vector3 rocketVelocity)
        {
            // ракета крупная (×2) — и клубы дыма под стать ей
            P.Emit(pos + Random.insideUnitSphere * 0.35f, -rocketVelocity * 0.05f + Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f,
                Random.Range(2.2f, 3.0f), 0.7f, Random.Range(1.5f, 2.2f), VoxelParticles.RandomSmoke(), drag: 1.5f, growEnd: 0.15f, shrinkStart: 0.45f);
        }

        /// <summary>Пламя из сопла (короткоживущие яркие кубики).</summary>
        public static void RocketFlame(Vector3 pos, Vector3 backward)
        {
            for (int i = 0; i < 2; i++)
                P.Emit(pos + Random.insideUnitSphere * 0.2f, backward * Random.Range(6f, 12f) + Random.insideUnitSphere * 1.5f, Random.Range(0.12f, 0.25f), 0.6f, 0.4f,
                    Random.value < 0.5f ? VoxelParticles.Fire0 : VoxelParticles.Fire1, growEnd: 0.1f, shrinkStart: 0.3f);
        }

        /// <summary>Короткая вспышка света в точке взрыва.</summary>
        public static void Flash(Vector3 pos)
        {
            var go = new GameObject("ExplosionFlash");
            go.transform.position = pos + Vector3.up * 2f;
            go.AddComponent<FlashLight>();
        }

        class FlashLight : MonoBehaviour
        {
            const float Duration = 0.45f;
            Light _light;
            float _t;

            void Awake()
            {
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = new Color(1f, 0.65f, 0.3f);
                _light.range = 35f;
                _light.intensity = 25f;
                _light.shadows = LightShadows.None;
            }

            void Update()
            {
                _t += Time.deltaTime;
                _light.intensity = Mathf.Lerp(25f, 0f, _t / Duration);
                if (_t >= Duration)
                    Destroy(gameObject);
            }
        }
    }
}
