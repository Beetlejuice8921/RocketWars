using RocketWars.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RocketWars.UI
{
    /// <summary>
    /// Модельки призёров для пьедестала в итогах матча: три воксельных солдата в цвете своих команд стоят далеко
    /// за картой, у каждого своя камера, которая рисует его в маленькую текстуру (point-фильтр — крупные пиксели).
    /// Победитель машет поднятыми руками, остальные покачиваются. Камеры работают, только пока пьедестал на экране.
    /// </summary>
    public class PodiumStage
    {
        public const int TexWidth = 48, TexHeight = 64;
        const int Layer = 31;
        static readonly Vector3 Origin = new Vector3(0f, -3000f, 0f);
        const float Spacing = 20f;

        readonly GameObject _root;
        readonly Figure[] _figures = new Figure[3];
        bool _active;

        class Figure
        {
            public Transform Pivot, ArmL, ArmR;
            public GameObject Model;
            public Camera Camera;
            public RenderTexture Texture;
            public int Team = -1;
            public int Place;
        }

        public PodiumStage()
        {
            _root = new GameObject("PodiumStage");
            _root.transform.position = Origin;

            // свой свет: только для слоя пьедестала, спереди-сверху
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(_root.transform, false);
            light.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;

            for (int i = 0; i < 3; i++)
            {
                var f = new Figure { Place = i + 1 };
                f.Pivot = new GameObject($"Place{i + 1}").transform;
                f.Pivot.SetParent(_root.transform, false);
                f.Pivot.localPosition = new Vector3(i * Spacing, 0f, 0f);

                f.Texture = new RenderTexture(TexWidth, TexHeight, 16, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Point,
                    name = $"podium_{i + 1}"
                };
                f.Camera = new GameObject("Camera").AddComponent<Camera>();
                f.Camera.transform.SetParent(_root.transform, false);
                f.Camera.transform.localPosition = f.Pivot.localPosition + new Vector3(0f, 1.2f, 3.7f);
                f.Camera.transform.LookAt(f.Pivot.position + new Vector3(0f, 1.18f, 0f));
                f.Camera.fieldOfView = 32f;
                f.Camera.nearClipPlane = 0.5f;
                f.Camera.farClipPlane = 12f;
                f.Camera.cullingMask = 1 << Layer;
                f.Camera.clearFlags = CameraClearFlags.SolidColor;
                f.Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                f.Camera.targetTexture = f.Texture;
                f.Camera.enabled = false;
                var data = f.Camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                _figures[i] = f;
            }
        }

        /// <summary>Текстура места 1..3.</summary>
        public Texture TextureOf(int place) => _figures[place - 1].Texture;

        /// <summary>Кто стоит на месте 1..3: команда (цвет каски и повязок) или -1 — место пустое.</summary>
        public void SetFigure(int place, int team)
        {
            var f = _figures[place - 1];
            if (f.Team == team && (team < 0 || f.Model != null))
                return;
            f.Team = team;
            if (f.Model != null)
                Object.Destroy(f.Model);
            f.Model = null;
            if (team >= 0)
                Build(f, team);
        }

        public void SetActive(bool active)
        {
            if (_active == active)
                return;
            _active = active;
            foreach (var f in _figures)
                f.Camera.enabled = active;
        }

        public void Update()
        {
            if (!_active)
                return;
            float t = Time.unscaledTime;
            foreach (var f in _figures)
            {
                if (f.Model == null)
                    continue;
                // поворачивается то к одному плечу, то к другому
                f.Pivot.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.8f + f.Place) * 25f, 0f);
                if (f.Place == 1)
                {
                    // победитель машет руками над головой
                    float wave = Mathf.Sin(t * 6f) * 18f;
                    f.ArmL.localRotation = Quaternion.Euler(180f, 0f, -20f + wave);
                    f.ArmR.localRotation = Quaternion.Euler(180f, 0f, 20f + wave);
                }
                else
                {
                    float sway = Mathf.Sin(t * 2f + f.Place) * 6f;
                    f.ArmL.localRotation = Quaternion.Euler(sway, 0f, -6f);
                    f.ArmR.localRotation = Quaternion.Euler(-sway, 0f, 6f);
                }
            }
        }

        public void Dispose()
        {
            foreach (var f in _figures)
            {
                if (f.Camera != null)
                    f.Camera.targetTexture = null;
                if (f.Texture != null)
                    f.Texture.Release();
                Object.Destroy(f.Texture);
            }
            if (_root != null)
                Object.Destroy(_root);
        }

        /// <summary>Тот же солдат, что в игре (PlayerVisual), только без оружия и анимации ходьбы.</summary>
        static void Build(Figure f, int team)
        {
            var body = new GameObject("Soldier").transform;
            body.SetParent(f.Pivot, false);
            var (bodyMesh, bodyMats) = Models.CharacterBody(team);
            BoxModel.Spawn(bodyMesh, bodyMats, body, Vector3.zero, "Torso");
            var (armMesh, armMats) = Models.CharacterArm(team);
            f.ArmL = BoxModel.Spawn(armMesh, armMats, body, new Vector3(-0.4f, 1.48f, 0f), "ArmL").transform;
            f.ArmR = BoxModel.Spawn(armMesh, armMats, body, new Vector3(0.4f, 1.48f, 0f), "ArmR").transform;
            var (legMesh, legMats) = Models.CharacterLeg();
            BoxModel.Spawn(legMesh, legMats, body, new Vector3(-0.14f, 0.84f, 0f), "LegL");
            BoxModel.Spawn(legMesh, legMats, body, new Vector3(0.14f, 0.84f, 0f), "LegR");
            var (helmetMesh, helmetMats) = Models.CharacterHelmet(team);
            BoxModel.Spawn(helmetMesh, helmetMats, body, new Vector3(0f, Models.HelmetBaseY, 0f), "Helmet");
            var (binoMesh, binoMats) = Models.Binoculars();
            BoxModel.Spawn(binoMesh, binoMats, body, new Vector3(0f, 1.24f, 0.24f), "Binoculars").transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);

            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = Layer;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f.Model = body.gameObject;
        }
    }
}
