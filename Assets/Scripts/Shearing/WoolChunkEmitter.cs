using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Turns newly removed wool into bounded visual requests. Never grants rewards.</summary>
    public sealed class WoolChunkEmitter : MonoBehaviour
    {
        [SerializeField] private ShearingProgress progress;
        [SerializeField] private MeshCollider woolSurface;
        [SerializeField] private WoolChunkPool pool;
        private WoolSurfaceLookup surfaceLookup;
        private float pendingArea;
        private int spawnFrame = -1;
        private int frameSpawns;

        private void OnEnable()
        {
            if (progress == null || woolSurface == null || pool == null || pool.Config == null)
            { Debug.LogError("WoolChunkEmitter requires progress, surface and pool references.", this); enabled = false; return; }
            try { surfaceLookup ??= new WoolSurfaceLookup(woolSurface); }
            catch (ArgumentException e) { Debug.LogError(e.Message, this); enabled = false; return; }
            progress.WoolRemoved += OnWoolRemoved;
            progress.ResetOccurred += ResetVisuals;
        }

        private void OnWoolRemoved(Vector2 uv, float newArea)
        {
            var config = pool.Config;
            pendingArea += newArea;
            if (pendingArea < config.AreaPerChunk) return;
            pendingArea %= config.AreaPerChunk; // Drop surplus; never emit stale cuts on later frames.
            if (spawnFrame != Time.frameCount) { spawnFrame = Time.frameCount; frameSpawns = 0; }
            if (frameSpawns >= config.MaxSpawnsPerFrame || pool.ActiveCount >= pool.Capacity) return;
            frameSpawns++;
            if (surfaceLookup.TryGetPoint(uv, out Vector3 point, out Vector3 normal)) pool.TrySpawn(point, normal);
        }

        private void ResetVisuals()
        {
            pendingArea = 0; spawnFrame = -1; frameSpawns = 0;
            if (pool != null) pool.ReleaseAll();
        }

        private void OnDisable()
        {
            if (progress != null)
            {
                progress.WoolRemoved -= OnWoolRemoved;
                progress.ResetOccurred -= ResetVisuals;
            }
            ResetVisuals();
        }
    }
}
