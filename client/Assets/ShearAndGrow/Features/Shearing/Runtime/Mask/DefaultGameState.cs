using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// DEFAULT GAME STATE — what every brand-new player starts with. Read-only data shipped in the build.
    /// It never contains player progress: runtime code must never write into this asset. The individual
    /// PLAYER SAVE STATE lives only in the device's private persistent storage (see <see cref="SheepWoolStateStore"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Default Game State", fileName = "DefaultGameState")]
    public sealed class DefaultGameState : ScriptableObject
    {
        [Tooltip("A new player's sheep start with this much wool removed. Must be 0 (full wool) for release builds.")]
        [Range(0f, 1f)] public float StartingCutFraction = 0f;

        [Tooltip("Bump ONLY when shipping a release whose saves must not be loaded any more (incompatible economy etc.). " +
                 "Saves with a lower epoch are ignored once and replaced with the default state. " +
                 "Normal updates keep this value so players keep their progress.")]
        [Min(0)] public int SaveEpoch = 0;

        [Tooltip("Sheep available to a brand-new player (id -> type).")]
        public string[] StartingSheepIds = { "sheep_001" };
    }
}
