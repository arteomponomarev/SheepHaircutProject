namespace ShearAndGrow.Farm
{
    /// <summary>Barn & sheep pen: lists the farm's sheep with their wool state; "Shear" starts shearing that sheep.</summary>
    public sealed class BarnInteraction : FarmInteractable
    {
        public override bool HasAttention(FarmHubController hub) => hub.AnySheepWithWool;
        public override string LabelText(FarmHubController hub) => DisplayName + "  " + hub.HerdText;
        public override void OnTap(FarmHubController hub) => hub.ShowBarnHerd(this);
    }
}
