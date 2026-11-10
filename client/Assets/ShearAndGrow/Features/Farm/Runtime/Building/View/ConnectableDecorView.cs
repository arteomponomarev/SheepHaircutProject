using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Segment decoration (fence / path) that shows a connector piece toward every same-type neighbour.
    /// Mask bits: 1 north (+z), 2 east (+x), 4 south (−z), 8 west (−x). Updated only when a neighbour changes.
    /// </summary>
    public sealed class ConnectableDecorView : BuildingView
    {
        [SerializeField] private GameObject _north, _east, _south, _west;
        [Tooltip("Shown when the segment has no neighbours (e.g. a lone fence post with short stubs).")]
        [SerializeField] private GameObject _single;

        public int Mask { get; private set; } = -1;

        public void ConfigureConnectors(GameObject north, GameObject east, GameObject south, GameObject west, GameObject single)
        {
            _north = north; _east = east; _south = south; _west = west; _single = single;
        }

        public override void SetConnections(int m)
        {
            if (m == Mask) return;
            Mask = m;
            if (_north != null) _north.SetActive((m & 1) != 0);
            if (_east != null) _east.SetActive((m & 2) != 0);
            if (_south != null) _south.SetActive((m & 4) != 0);
            if (_west != null) _west.SetActive((m & 8) != 0);
            if (_single != null) _single.SetActive(m == 0);
        }
    }
}