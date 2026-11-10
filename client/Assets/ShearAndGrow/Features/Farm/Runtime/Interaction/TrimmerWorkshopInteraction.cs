namespace ShearAndGrow.Farm
{
    /// <summary>Trimmer Workshop: shows the trimmer and sells its upgrades.</summary>
    public sealed class TrimmerWorkshopInteraction : FarmInteractable
    {
        public override bool HasAttention(FarmHubController hub) => hub.IsInstanceComplete(InstanceId) && hub.CanUpgradeTrimmer;
        public override void OnTap(FarmHubController hub) => hub.ShowWorkshop(this);
    }
}