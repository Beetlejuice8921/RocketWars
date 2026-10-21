using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketWars.Fx
{
    /// <summary>
    /// Самолёт, который «сбрасывает» ящики через MatchManager.AirDropDelay после начала раунда: быстро пролетает
    /// над обоими островами вдоль оси Z и проходит над вторым островом чуть раньше сброса. Сам сброс не трогает —
    /// это только картинка. Время пролёта у всех считается из MatchState (начало раунда + задержка) по NetClock,
    /// поэтому по сети ничего не передаётся. Без коллайдеров и теней; направление чередуется по раундам.
    /// Висит на объекте MatchManager (добавляется в его Awake).
    /// </summary>
    public class AirDropPlane : MonoBehaviour
    {
        const float Speed = 70f;
        const float Height = 60f;
        /// <summary>Над центром второго по курсу острова самолёт проходит за столько секунд до сброса.</summary>
        const float LeadTime = 0.3f;
        /// <summary>Самолёт виден, пока ближе этого расстояния (по Z) от центра карты.</summary>
        const float HalfPath = 450f;
        const float PropellerSpeed = 1800f; // °/с

        MatchManager _match;
        MatchNet _net;
        Transform _plane;
        Transform[] _propellers;

        void Awake()
        {
            _match = GetComponent<MatchManager>();
            _net = GetComponent<MatchNet>();
        }

        void OnDestroy()
        {
            if (_plane != null)
                Destroy(_plane.gameObject);
        }

        void Update()
        {
            bool visible = TryGetPosition(out Vector3 pos, out float dir);
            if (!visible)
            {
                if (_plane != null && _plane.gameObject.activeSelf)
                    _plane.gameObject.SetActive(false);
                return;
            }
            if (_plane == null)
                Build();
            _plane.gameObject.SetActive(true);
            float wobble = Mathf.Sin(Time.time * 1.3f);
            _plane.SetPositionAndRotation(pos + Vector3.up * (wobble * 0.4f),
                Quaternion.LookRotation(Vector3.forward * dir) * Quaternion.Euler(0f, 0f, wobble * 3f));
            foreach (var p in _propellers)
                p.localRotation = Quaternion.Euler(0f, 0f, Time.time * PropellerSpeed);
        }

        bool TryGetPosition(out Vector3 pos, out float dir)
        {
            pos = default;
            dir = 1f;
            if (_match == null || !_match.IsSpawned)
                return false;
            var s = _match.State;
            if (s.Phase != MatchPhase.Round)
                return false;
            // в раунде PhaseEnd — момент окончания отсчёта, от него сервер и отсчитывает сброс
            double dropAt = s.PhaseEnd + MatchManager.AirDropDelay;
            float t = (float)(NetClock.Now - dropAt);
            dir = s.Round % 2 == 1 ? 1f : -1f;
            float gap = _net != null ? _net.islandGap : 45f;
            float islandCenter = (gap + VoxelIsland.Depth) * 0.5f; // центры островов — на ±этом Z
            float z = islandCenter + Speed * (t + LeadTime);
            if (Mathf.Abs(z) > HalfPath)
                return false;
            pos = new Vector3(0f, Height, z * dir);
            return true;
        }

        void Build()
        {
            var root = new GameObject("AirDropPlane").transform;
            var (mesh, mats) = Models.CargoPlane();
            NoShadows(BoxModel.Spawn(mesh, mats, root, Vector3.zero, "Body"));
            var (propMesh, propMats) = Models.Propeller();
            _propellers = new Transform[Models.PlanePropellers.Length];
            for (int i = 0; i < _propellers.Length; i++)
            {
                var prop = BoxModel.Spawn(propMesh, propMats, root, Models.PlanePropellers[i], "Propeller");
                NoShadows(prop);
                _propellers[i] = prop.transform;
            }
            _plane = root;
        }

        static void NoShadows(GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
