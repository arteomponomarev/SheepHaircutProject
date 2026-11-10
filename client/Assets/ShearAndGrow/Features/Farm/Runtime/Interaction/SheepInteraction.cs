using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>A sheep on the farm. Tapping it starts shearing exactly this sheep (its own persistent wool state).</summary>
    public sealed class SheepInteraction : FarmInteractable
    {
        [SerializeField] private string _sheepId = "sheep_001";
        [SerializeField] private FarmSheepView _view;

        public string SheepId => _sheepId;
        public FarmSheepView View => _view;
        protected override bool IsDynamic => true;

        public void ConfigureSheep(string sheepId, FarmSheepView view) { _sheepId = sheepId; _view = view; }

        public override bool HasAttention(FarmHubController hub) => hub.SheepInfo(_sheepId).RemainingWool > 0.01f;

        public override string LabelText(FarmHubController hub)
        {
            var info = hub.SheepInfo(_sheepId);
            return DisplayName + "  " + info.WoolPercent + "%" + (info.Preserved ? "  [style]" : "") + (info.Boosted ? "  x" + info.BoostMultiplier.ToString("0.#") : "");
        }

        /// <summary>Sheep card: wool %, growth status, hairstyle lock, Shear / Unlock.</summary>
        public override void OnTap(FarmHubController hub) => hub.Panels.ShowSheep(this, hub.SheepInfo(_sheepId));
    }
}
