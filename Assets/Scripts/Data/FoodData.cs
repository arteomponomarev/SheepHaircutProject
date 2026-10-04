using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(menuName = "Shear and Grow/Food Data")]
    public sealed class FoodData : ScriptableObject
    {
        [SerializeField, Min(1)] private int cost = 20;
        [SerializeField, Min(0.1f)] private float duration = 30;
        [SerializeField, Min(1.01f)] private float multiplier = 2;
        public int Cost => cost;
        public float Duration => duration;
        public float Multiplier => multiplier;
        public bool IsValid => cost > 0 && duration > 0 && !float.IsInfinity(duration) &&
            multiplier > 1 && !float.IsInfinity(multiplier);
    }
}
