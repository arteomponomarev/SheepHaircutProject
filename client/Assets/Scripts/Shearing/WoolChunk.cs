using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>One reusable wool visual and rigidbody. Lifetime scheduling belongs to the pool.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(MeshFilter), typeof(BoxCollider))]
    public sealed class WoolChunk : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public BoxCollider CollisionShape { get; private set; }
        public float ExpiresAt { get; private set; }
        public bool IsActive => gameObject.activeSelf;
        private MeshFilter meshFilter;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            CollisionShape = GetComponent<BoxCollider>();
            meshFilter = GetComponent<MeshFilter>();
        }

        public void Launch(WoolChunkConfig config, Vector3 point, Vector3 normal)
        {
            gameObject.SetActive(true);
            var mesh = config.GetVariant(Random.Range(0, config.VariantCount));
            meshFilter.sharedMesh = mesh;
            CollisionShape.center = mesh.bounds.center;
            CollisionShape.size = mesh.bounds.size;
            float scale = Random.Range(config.MinScale, config.MaxScale);
            transform.localScale = new Vector3(scale * Random.Range(0.9f, 1.1f), scale, scale * Random.Range(0.9f, 1.1f));
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
            transform.SetPositionAndRotation(point + normal * config.SurfaceOffset, rotation);
            Body.position = transform.position; Body.rotation = rotation;
            Body.mass = config.Mass;
            Body.linearDamping = config.LinearDamping; Body.angularDamping = config.AngularDamping;
            Body.isKinematic = false; Body.useGravity = true;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Random.onUnitSphere * config.AngularSpeed;
            Vector3 sideways = Vector3.ProjectOnPlane(Random.insideUnitSphere, normal);
            Body.AddForce(normal * config.OutwardImpulse + Vector3.down * config.DownwardImpulse +
                sideways * config.SidewaysImpulse, ForceMode.Impulse);
            Body.WakeUp();
            ExpiresAt = Time.time + Random.Range(config.MinLifetime, config.MaxLifetime);
        }

        public void ReturnToPool()
        {
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            Body.Sleep(); Body.isKinematic = true;
            gameObject.SetActive(false);
        }
    }
}
