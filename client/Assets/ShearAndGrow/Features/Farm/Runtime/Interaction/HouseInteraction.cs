namespace ShearAndGrow.Farm
{
    /// <summary>Farmer house: progression hub. Opens the level / requirements / upgrade sheet.</summary>
    public sealed class HouseInteraction : FarmInteractable
    {
        public override string LabelText(FarmHubController hub) => DisplayName + "  Lv " + hub.LevelOf(InstanceId) + (hub.IsInstanceComplete(InstanceId) ? "" : " (upgrading)");
        public override bool HasAttention(FarmHubController hub) => hub.CanUpgrade(InstanceId);
        public override void OnTap(FarmHubController hub) => hub.ShowHouseProgress(this);
    }
}
