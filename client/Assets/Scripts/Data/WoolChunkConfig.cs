using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(fileName = "WoolChunkConfig", menuName = "Shear and Grow/Wool Chunk Config")]
    public sealed class WoolChunkConfig : ScriptableObject
    {
        [SerializeField] private Mesh[] meshVariants;
        [SerializeField, Range(1, 64)] private int poolSize = 40;
        [SerializeField] private Vector2 lifetime = new Vector2(2f, 4f);
        [SerializeField, Range(0.0001f, 0.01f)] private float areaPerChunk = 0.0004f;
        [SerializeField, Range(1, 8)] private int maxSpawnsPerFrame = 3;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.85f, 1.15f);
        [SerializeField, Min(0.01f)] private float surfaceOffset = 0.07f;
        [SerializeField, Min(0.001f)] private float mass = 0.03f;
        [SerializeField, Min(0f)] private float outwardImpulse = 0.018f;
        [SerializeField, Min(0f)] private float downwardImpulse = 0.006f;
        [SerializeField, Min(0f)] private float sidewaysImpulse = 0.004f;
        [SerializeField, Min(0f)] private float angularSpeed = 3f;
        [SerializeField, Min(0f)] private float linearDamping = 2f;
        [SerializeField, Min(0f)] private float angularDamping = 1.2f;

        public int PoolSize => Mathf.Clamp(poolSize, 1, 64);
        public int VariantCount => meshVariants == null ? 0 : meshVariants.Length;
        public Mesh GetVariant(int index) => meshVariants[index];
        public float MinLifetime => Mathf.Max(0.1f, lifetime.x);
        public float MaxLifetime => Mathf.Max(MinLifetime, lifetime.y);
        public float AreaPerChunk => Mathf.Max(0.0001f, areaPerChunk);
        public int MaxSpawnsPerFrame => Mathf.Clamp(maxSpawnsPerFrame, 1, 8);
        public float MinScale => Mathf.Max(0.1f, scaleRange.x);
        public float MaxScale => Mathf.Max(MinScale, scaleRange.y);
        public float SurfaceOffset => Mathf.Max(0.01f, surfaceOffset);
        public float Mass => Mathf.Max(0.001f, mass);
        public float OutwardImpulse => Mathf.Max(0f, outwardImpulse);
        public float DownwardImpulse => Mathf.Max(0f, downwardImpulse);
        public float SidewaysImpulse => Mathf.Max(0f, sidewaysImpulse);
        public float AngularSpeed => Mathf.Max(0f, angularSpeed);
        public float LinearDamping => Mathf.Max(0f, linearDamping);
        public float AngularDamping => Mathf.Max(0f, angularDamping);
    }
}
