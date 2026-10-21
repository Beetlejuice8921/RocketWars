using RocketWars.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace RocketWars.UI
{
    /// <summary>
    /// Окно скинов из главного меню: по центру медленно вращается солдат в полный рост, внизу — ячейки скинов (Core/Skins)
    /// и заглушки «Скоро». Клик по ячейке выбирает скин (сохраняется сразу, в матче его видят все).
    /// Солдат и его камера создаются при открытии и удаляются при закрытии (как пьедестал — далеко за картой, свой слой).
    /// </summary>
    public class SkinView
    {
        /// <summary>Сколько всего ячеек в ряду: после настоящих скинов — заглушки «Скоро».</summary>
        const int CellCount = 6;

        readonly VisualElement _screen, _figure;
        readonly Button[] _cells = new Button[Skins.Count];
        SkinStage _stage;

        public bool IsOpen => Ui.IsShown(_screen);

        public SkinView(VisualElement root)
        {
            _screen = Ui.El(root, "layer", "overlay");
            var panel = Ui.El(_screen, "panel", "skin-panel");
            Ui.Text(panel, Loc.T("СКИНЫ"), "title", "shadow");

            _figure = Ui.El(panel, "skin-figure");
            _figure.pickingMode = PickingMode.Ignore;

            var list = Ui.El(panel, "row", "row-center", "skin-list");
            for (int i = 0; i < Skins.Count; i++)
            {
                int skin = i;
                _cells[i] = Ui.Btn(list, Loc.T(Skins.Get(i).NameKey), () => Select(skin), "btn-small", "skin-cell");
            }
            for (int i = Skins.Count; i < CellCount; i++)
                Ui.Btn(list, Loc.T("Скоро"), null, "btn-small", "skin-cell", "skin-cell-locked").SetEnabled(false);
            MarkSelected();

            Ui.Btn(panel, Loc.T(TouchInput.Enabled ? "Закрыть" : "Закрыть (Esc)"), Close, "btn-primary");
            Ui.Show(_screen, false);
        }

        public void Open()
        {
            Ui.Show(_screen, true);
            MarkSelected();
            _stage ??= new SkinStage(Skins.Selected);
            _figure.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
        }

        public void Close()
        {
            Ui.Show(_screen, false);
            Dispose();
        }

        public void Update() => _stage?.Update();

        void Select(int skin)
        {
            Skins.Selected = skin;
            MarkSelected();
            _stage?.SetSkin(skin);
        }

        void MarkSelected()
        {
            int selected = Skins.Selected;
            for (int i = 0; i < _cells.Length; i++)
                _cells[i].EnableInClassList("btn-primary", i == selected);
        }

        public void Dispose()
        {
            _stage?.Dispose();
            _stage = null;
            _figure.style.backgroundImage = StyleKeyword.None;
        }

        /// <summary>Один солдат в полный рост со своей камерой: рисуется в маленькую текстуру (point-фильтр — крупные пиксели).</summary>
        sealed class SkinStage
        {
            const int TexWidth = 64, TexHeight = 96;
            const int Layer = 31;
            // в стороне от пьедестала (тот же слой, но его камеры сюда не достают)
            static readonly Vector3 Origin = new Vector3(0f, -3000f, 300f);
            const float SpinSpeed = 45f;

            readonly GameObject _root;
            readonly Transform _pivot;
            readonly Camera _camera;
            GameObject _model;
            Transform _armL, _armR;
            int _skin = -1;

            public RenderTexture Texture { get; }

            public SkinStage(int skin)
            {
                _root = new GameObject("SkinStage");
                _root.transform.position = Origin;

                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(_root.transform, false);
                light.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                light.type = LightType.Directional;
                light.intensity = 0.9f;
                light.cullingMask = 1 << Layer;
                light.shadows = LightShadows.None;

                _pivot = new GameObject("Pivot").transform;
                _pivot.SetParent(_root.transform, false);
                SetSkin(skin);

                Texture = new RenderTexture(TexWidth, TexHeight, 16, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Point,
                    name = "skin_preview"
                };
                _camera = new GameObject("Camera").AddComponent<Camera>();
                _camera.transform.SetParent(_root.transform, false);
                _camera.transform.localPosition = new Vector3(0f, 1.15f, 4.4f);
                _camera.transform.LookAt(_root.transform.position + new Vector3(0f, 1.1f, 0f));
                _camera.fieldOfView = 34f;
                _camera.nearClipPlane = 0.5f;
                _camera.farClipPlane = 12f;
                _camera.cullingMask = 1 << Layer;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _camera.targetTexture = Texture;
                var data = _camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
            }

            /// <summary>Пересобрать солдата в другом скине (зелёная команда, как в диораме меню).</summary>
            public void SetSkin(int skin)
            {
                if (skin == _skin)
                    return;
                _skin = skin;
                if (_model != null)
                    Object.Destroy(_model);
                _model = PodiumStage.BuildSoldier(_pivot, 1, Layer, out _armL, out _armR, skin);
            }

            public void Update()
            {
                float t = Time.unscaledTime;
                _pivot.localRotation = Quaternion.Euler(0f, 180f + t * SpinSpeed, 0f);
                float sway = Mathf.Sin(t * 2f) * 6f;
                _armL.localRotation = Quaternion.Euler(sway, 0f, -6f);
                _armR.localRotation = Quaternion.Euler(-sway, 0f, 6f);
            }

            public void Dispose()
            {
                _camera.targetTexture = null;
                Texture.Release();
                Object.Destroy(Texture);
                Object.Destroy(_root);
            }
        }
    }
}
