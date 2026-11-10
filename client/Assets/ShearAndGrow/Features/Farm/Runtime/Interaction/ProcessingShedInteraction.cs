using ShearAndGrow.Economy;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Wool processing building: entry to washing (needs Dirty Wool) and drying (needs Wet Wool). Functional only
    /// once its construction is completed (the hub shows the construction sheet before that).
    /// </summary>
    public sealed class ProcessingShedInteraction : FarmInteractable
    {
        public override bool HasAttention(FarmHubController hub) =>
            hub.IsInstanceComplete(InstanceId) &&
            WoolEconomy.IsReady && (WoolEconomy.Instance.Available(WoolType.Dirty) > 0 || WoolEconomy.Instance.Available(WoolType.Wet) > 0);

        public override string LabelText(FarmHubController hub) =>
            hub.IsInstanceComplete(InstanceId) ? DisplayName : DisplayName + " (building)";

        public override void OnTap(FarmHubController hub)
        {
            if (!hub.IsInstanceComplete(InstanceId)) return; // never open Wash/Dry before completion
            hub.Panels.ShowShed(this);
        }
    }
}
