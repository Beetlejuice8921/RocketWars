using UnityEngine;
using UnityEngine.Rendering;

namespace RocketWars.Player
{
    /// <summary>
    /// Лёжа (и на 2 с превращения в берсерка) камера уходит в вид от третьего лица: плавно отъезжает назад и вверх от тела и облетает его
    /// вслед за мышью (поворот задаёт FpsController). Камера висит на «штативе» (boom) между CameraRoot и камерой,
    /// поэтому сама точка глаз, выстрелы и тряска не трогаются. Склоны и стены сзади подтягивают камеру ближе.
    /// В этом виде своё тело рисуется (обычно от него видна только тень).
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(FpsController), typeof(PlayerHealth))]
    public class DownedCamera : MonoBehaviour
    {
        const float Distance = 5.5f;
        const float Lift = 1.0f;
        const float BlendSpeed = 2.5f;
        const float CollisionRadius = 0.3f;
        const float CollisionPadding = 0.2f;
        /// <summary>Наклон облёта: взгляд прямо — это 25° сверху, пределы 15°–80°.</summary>
        const float OrbitPitchOffset = 25f, MinOrbitPitch = 15f, MaxOrbitPitch = 80f;

        /// <summary>Промежуточный объект между CameraRoot и камерой (создаёт LocalPlayerRig).</summary>
        public Transform boom;

        FpsController _fps;
        PlayerHealth _health;
        NetworkPlayer _net;
        Renderer[] _body;
        float _blend;
        bool _bodyShown;

        void Awake()
        {
            _fps = GetComponent<FpsController>();
            _health = GetComponent<PlayerHealth>();
            _net = GetComponent<NetworkPlayer>();
        }

        void LateUpdate()
        {
            if (boom == null)
                return;

            // лёжа и на время превращения в берсерка (его надо увидеть со стороны) — вид от третьего лица
            bool thirdPerson = _health.IsDowned || (_net != null && _net.InBerserkTransform);
            float target = thirdPerson ? 1f : 0f;
            _blend = Mathf.MoveTowards(_blend, target, BlendSpeed * Time.deltaTime);
            float k = Mathf.SmoothStep(0f, 1f, _blend);

            if (k <= 0f)
            {
                boom.localPosition = Vector3.zero;
                boom.localRotation = Quaternion.identity;
            }
            else
            {
                // облетаем середину лежащего тела (лежит на спине, голова позади ног), а не точку глаз;
                // смотрим на него сверху (мышь вверх-вниз меняет наклон) — так холмы сзади меньше заслоняют вид
                Transform root = _fps.CameraRoot;
                Vector3 euler = root.eulerAngles;
                float pitch = Mathf.DeltaAngle(0f, euler.x);
                var orbit = Quaternion.Euler(Mathf.Clamp(pitch + OrbitPitchOffset, MinOrbitPitch, MaxOrbitPitch), euler.y, 0f);
                boom.rotation = Quaternion.Slerp(root.rotation, orbit, k);

                Vector3 pivot = Vector3.Lerp(root.position, BodyCenter() + Vector3.up * Lift, k);
                Vector3 back = -(boom.rotation * Vector3.forward);
                float distance = Distance * k;
                if (Probe(pivot, back, distance, out float free))
                    distance = Mathf.Min(distance, free);
                boom.position = pivot + back * distance;
            }

            ShowBody(k > 0.3f);
        }

        Renderer _torso;

        Vector3 BodyCenter()
        {
            if (_torso == null)
            {
                var torso = transform.Find("Body/Torso");
                _torso = torso != null ? torso.GetComponent<Renderer>() : null;
            }
            return _torso != null ? _torso.bounds.center : transform.position + Vector3.up * 0.5f;
        }

        /// <summary>Свободное расстояние от точки над телом назад до ближайшего препятствия (своё тело не считается).</summary>
        bool Probe(Vector3 from, Vector3 dir, float maxDistance, out float free)
        {
            free = maxDistance;
            bool hitSomething = false;
            foreach (var hit in Physics.SphereCastAll(from, CollisionRadius, dir, maxDistance + CollisionPadding, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform))
                    continue;
                float d = Mathf.Max(0f, hit.distance - CollisionPadding);
                if (d < free)
                {
                    free = d;
                    hitSomething = true;
                }
            }
            return hitSomething;
        }

        /// <summary>От первого лица своё тело только отбрасывает тень (его настраивает PlayerVisual), от третьего — видно.</summary>
        void ShowBody(bool show)
        {
            if (show == _bodyShown)
                return;
            if (_body == null)
            {
                var body = transform.Find("Body");
                if (body == null)
                    return;
                _body = body.GetComponentsInChildren<Renderer>(true);
            }
            _bodyShown = show;
            foreach (var r in _body)
                if (r != null)
                    r.shadowCastingMode = show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        }

        void OnDestroy()
        {
            if (boom != null)
            {
                boom.localPosition = Vector3.zero;
                boom.localRotation = Quaternion.identity;
            }
        }
    }
}
