using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(fileName = "SheepShearingData", menuName = "Shear and Grow/Sheep Shearing Data")]
    public sealed class SheepShearingData : ScriptableObject
    {
        [Tooltip("Wool from 100% unique coverage. Partial coverage earns the same fraction of this amount.")]
        [SerializeField, Min(0f)] private float woolAmount = 100f;
        public float WoolAmount => Mathf.Max(0f, woolAmount);
    }
}
