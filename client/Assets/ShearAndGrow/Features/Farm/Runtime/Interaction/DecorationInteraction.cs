namespace ShearAndGrow.Farm
{
    /// <summary>Decoration: no function; tap offers Move / Store.</summary>
    public sealed class DecorationInteraction : FarmInteractable
    {
        public override void OnTap(FarmHubController hub) => hub.ShowBuildingActions(this);
    }
}