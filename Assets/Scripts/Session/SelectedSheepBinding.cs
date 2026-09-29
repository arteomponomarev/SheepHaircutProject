using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Connects the selected roster entry to the existing isolated shearing session.</summary>
    public sealed class SelectedSheepBinding : MonoBehaviour
    {
        [SerializeField] private SheepRosterState roster;
        [SerializeField] private ShearingSession session;
        private void Awake()
        {
            if (roster != null && roster.IsConfigured && session != null && session.AssignSheep(roster.SelectedSheep)) return;
            Debug.LogError("Selected sheep binding requires a configured roster and an unstarted session.", this);
        }
    }
}
