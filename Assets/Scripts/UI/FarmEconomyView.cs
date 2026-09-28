using System.Globalization;
using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Displays inventory and forwards Sell requests. All sale rules belong to EconomyState.</summary>
    public sealed class FarmEconomyView : MonoBehaviour
    {
        [SerializeField] private EconomyState economy;
        [SerializeField] private TMP_Text balancesLabel;
        [SerializeField] private UnityEngine.UI.Button sellButton;
        [SerializeField] private TMP_Text sellLabel;

        private void OnEnable()
        {
            if (economy == null || balancesLabel == null || sellButton == null || sellLabel == null)
            { Debug.LogError("Farm economy view has missing references.", this); enabled = false; return; }
            economy.Changed += Refresh; sellButton.onClick.AddListener(Sell); Refresh();
        }

        private void Sell() => economy.TrySellAllWool(out _);
        private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private void Refresh()
        {
            // Strings are rebuilt only after inventory transactions, not every frame.
            balancesLabel.text = "Coins " + Format(economy.Coins) + "     Wool " + Format(economy.Wool);
            sellButton.interactable = economy.CanSellWool;
            sellLabel.text = economy.CanSellWool ? "Sell Wool\n+" + Format(economy.SaleValue) + " Coins"
                : economy.Wool == 0 ? "Sell Wool\nNo wool to sell" : "Sell unavailable";
        }

        private void OnDisable()
        {
            if (economy != null) economy.Changed -= Refresh;
            if (sellButton != null) sellButton.onClick.RemoveListener(Sell);
        }
    }
}
