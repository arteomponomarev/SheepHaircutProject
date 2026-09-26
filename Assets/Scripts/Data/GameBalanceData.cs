using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(fileName = "GameBalanceData", menuName = "Shear and Grow/Game Balance")]
    public sealed class GameBalanceData : ScriptableObject
    {
        [Tooltip("Fraction of unique shearable area required to mark the shearing session complete.")]
        [SerializeField, Range(0.01f, 1f)] private float completionThreshold = 0.95f;

        public float CompletionThreshold => completionThreshold;

        private void OnValidate()
        {
            completionThreshold = Mathf.Clamp(completionThreshold, 0.01f, 1f);
        }
    }
}
