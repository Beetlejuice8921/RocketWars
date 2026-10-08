using RocketWars.Core;
using UnityEngine;

namespace RocketWars.Fx
{
    /// <summary>
    /// Каска, слетевшая с упавшего игрока: подскакивает, кувыркается и прыгает по земле по физике,
    /// через 2 с сжимается и исчезает. Чисто визуальная и локальная (у каждого своя, без синхронизации).
    /// </summary>
    public class FlyingHelmet : MonoBehaviour
    {
        const float Lifetime = 2f;
        const float ShrinkTime = 0.25f;

        static PhysicsMaterial _bouncy;

        float _age;
        Vector3 _scale;

        /// <summary>Отделить копию каски в её текущей позиции; owner — тело игрока, о которое она не бьётся.</summary>
        public static void Spawn(Mesh mesh, Material[] materials, Transform from, GameObject owner)
        {
            var go = new GameObject("FlyingHelmet");
            go.transform.SetPositionAndRotation(from.position, from.rotation);
            go.transform.localScale = from.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;

            if (_bouncy == null)
                _bouncy = new PhysicsMaterial("HelmetBounce")
                {
                    bounciness = 0.55f,
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                    dynamicFriction = 0.4f,
                    staticFriction = 0.5f
                };
            var col = go.AddComponent<BoxCollider>();
            col.center = mesh.bounds.center;
            col.size = mesh.bounds.size;
            col.sharedMaterial = _bouncy;
            if (owner != null)
                foreach (var c in owner.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(col, c);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // подскок вверх, в случайную сторону, с кувырком
            Vector2 side = Random.insideUnitCircle.normalized * Random.Range(1.5f, 2.8f);
            rb.linearVelocity = new Vector3(side.x, Random.Range(4.5f, 6f), side.y);
            rb.angularVelocity = Random.onUnitSphere * Random.Range(6f, 12f);

            var helmet = go.AddComponent<FlyingHelmet>();
            helmet._scale = go.transform.localScale;
        }

        void Update()
        {
            _age += Time.deltaTime;
            float left = Lifetime - _age;
            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (left < ShrinkTime)
                transform.localScale = _scale * (left / ShrinkTime);
        }

        /// <summary>Глухой стук при ударе о землю.</summary>
        void OnCollisionEnter(Collision collision)
        {
            if (collision.relativeVelocity.magnitude > 1.5f)
                Sfx.PlayAt(Sfx.BulletImpact, transform.position, 0.45f, 25f, Random.Range(0.6f, 0.8f));
        }
    }
}
