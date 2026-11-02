using UnityEngine;

namespace ShearAndGrow.Shearing
{
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Chunk Physics Config", fileName = "ChunkPhysicsConfig")]
    public sealed class ChunkPhysicsConfig : ScriptableObject
    {
        [Header("Attachment graph")]
        [Tooltip("Average clump support-region radius on the body surface.")]
        public float ClumpRegionRadius = 0.075f;
        [Tooltip("Fraction of clumps that are large 'hero' clumps.")]
        [Range(0f, 1f)] public float HeroClumpFraction = 0.18f;
        public float HeroClumpScale = 1.55f;
        [Range(0f, 1f)] public float HangingThreshold = 0.6f;
        [Range(0f, 1f)] public float DetachThreshold = 0.2f;
        [Tooltip("Hang duration range in seconds (spec 0.15–0.70).")]
        public Vector2 HangDuration = new Vector2(0.15f, 0.7f);
        public float HeroExtraHang = 0.35f;
        [Tooltip("Minimum visible hang even if support vanished in one frame.")]
        public float MinHang = 0.12f;
        public float MaxHangStretch = 0.12f;

        [Header("Soft body")]
        [Tooltip("Number of simultaneously high-quality simulated clumps (spec 12–24).")]
        public int MaxHeroActive = 20;
        public int HeroIterations = 4;
        public int StandardIterations = 1;
        [Range(0f, 1f)] public float ShapeStiffness = 0.35f;
        [Range(0f, 1f)] public float HangingStiffness = 0.18f;
        public float FixedStep = 1f / 60f;
        public int MaxSubsteps = 3;
        public float Gravity = 9.81f;
        [Tooltip("Linear air drag per second; higher = floatier fall.")]
        public float AirDrag = 2.2f;
        public float TumbleStrength = 0.6f;
        public float DetachImpulse = 0.55f;
        public float TangentImpulse = 0.6f;
        [Range(0f, 1f)] public float GroundFriction = 0.85f;
        public float SleepVelocity = 0.06f;
        public float SleepTime = 0.3f;
        [Tooltip("Velocity damping per second once a clump has touched the floor/pile.")]
        public float LandedDamping = 9f;
        [Tooltip("Hard cap: a landed clump always goes to sleep after this many seconds (stays visible).")]
        public float MaxSettleTime = 1.4f;
        public float MaxAirTime = 6f;

        [Header("Floor")]
        public float FloorY = 0f;
        public Vector2 FloorHalfExtents = new Vector2(2.6f, 2.6f);
        public int FloorGridResolution = 96;
        [Tooltip("Visible individual settled clumps before batches are merged into static pile meshes.")]
        public int FloorIndividualBudget = 160;
        public int MergeBatchSize = 24;
        [Tooltip("Pre-allocated clump objects. Never Instantiate during a stroke.")]
        public int PoolSize = 260;
    }
}
