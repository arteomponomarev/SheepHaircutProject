namespace ShearAndGrow.Farm
{
    /// <summary>Feeding Station: pick a sheep and feed it for a temporary regrowth boost.</summary>
    public sealed class FeedingStationInteraction : FarmInteractable
    {
        public override void OnTap(FarmHubController hub) => hub.ShowFeeding(this);
    }
}