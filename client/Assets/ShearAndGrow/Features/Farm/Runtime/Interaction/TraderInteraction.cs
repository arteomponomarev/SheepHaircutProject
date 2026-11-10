using ShearAndGrow.Economy;

namespace ShearAndGrow.Farm
{
    /// <summary>Trader stall: opens the existing sell screen.</summary>
    public sealed class TraderInteraction : FarmInteractable
    {
        public override bool HasAttention(FarmHubController hub) => WoolEconomy.IsReady && WoolEconomy.Instance.TotalWool > 0;
        public override void OnTap(FarmHubController hub) => hub.OpenTrader(this);
    }
}
