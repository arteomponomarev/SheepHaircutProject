using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Fixed prewarmed capacity. Exhaustion drops visual requests, never creates more bodies.</summary>
    public sealed class WoolChunkPool : MonoBehaviour
    {
        [SerializeField] private WoolChunk prefab;
        [SerializeField] private WoolChunkConfig config;
        [SerializeField] private Collider[] ignoredColliders;
        private WoolChunk[] chunks;
        public WoolChunkConfig Config => config;
        public int Capacity => chunks == null ? 0 : chunks.Length;
        public int ActiveCount { get; private set; }

        private void Awake()
        {
            if (prefab == null || config == null || config.VariantCount == 0)
            { Debug.LogError("WoolChunkPool requires prefab, config and mesh variants.", this); enabled = false; return; }
            for (int i = 0; i < config.VariantCount; i++)
                if (config.GetVariant(i) == null)
                { Debug.LogError("WoolChunkConfig has a missing mesh variant.", this); enabled = false; return; }
            chunks = new WoolChunk[config.PoolSize];
            for (int i = 0; i < chunks.Length; i++)
            {
                chunks[i] = Instantiate(prefab, transform);
                chunks[i].ReturnToPool();
            }
        }

        public bool TrySpawn(Vector3 point, Vector3 normal)
        {
            if (!isActiveAndEnabled || chunks == null || ActiveCount >= chunks.Length) return false;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].IsActive) continue;
                var chunk = chunks[i];
                chunk.Launch(config, point, normal);
                // Reapply after activation; pooled collider state must not depend on prior contacts.
                if (ignoredColliders != null)
                    for (int j = 0; j < ignoredColliders.Length; j++)
                        if (ignoredColliders[j] != null) Physics.IgnoreCollision(chunk.CollisionShape, ignoredColliders[j]);
                for (int j = 0; j < chunks.Length; j++)
                    if (i != j && chunks[j].IsActive) Physics.IgnoreCollision(chunk.CollisionShape, chunks[j].CollisionShape);
                ActiveCount++;
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (chunks == null) return;
            for (int i = 0; i < chunks.Length; i++)
                if (chunks[i].IsActive && Time.time >= chunks[i].ExpiresAt)
                { chunks[i].ReturnToPool(); ActiveCount--; }
        }

        public void ReleaseAll()
        {
            if (chunks != null)
                for (int i = 0; i < chunks.Length; i++)
                    if (chunks[i].IsActive) chunks[i].ReturnToPool();
            ActiveCount = 0;
        }

        private void OnDisable() => ReleaseAll();
    }
}
