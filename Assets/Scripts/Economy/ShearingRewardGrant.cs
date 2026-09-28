using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Connects one completed shearing session to inventory; does not calculate yield.</summary>
    [DisallowMultipleComponent]
    public sealed class ShearingRewardGrant : MonoBehaviour
    {
        [SerializeField] private ShearingSession session;
        [SerializeField] private EconomyState economy;
        private bool granted;

        private void Awake()
        {
            if (session == null || economy == null)
            { Debug.LogError("Shearing reward requires session and economy references.", this); enabled = false; return; }
            // Observe for the whole scene lifetime, including when this bridge is disabled.
            session.Completed += Grant;
            Grant();
        }

        private void Grant()
        {
            if (granted || !session.HasResult) return;
            granted = true;
            if (economy.TryAddWool(session.ResultWool)) return;
            granted = false;
            Debug.LogError("Could not credit completed shearing yield to inventory.", this);
        }

        private void OnDestroy() { if (session != null) session.Completed -= Grant; }
    }
}
