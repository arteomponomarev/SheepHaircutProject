using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Shows level/price/availability and forwards purchase requests; owns no purchase rules.</summary>
    public sealed class TrimmerUpgradeView : MonoBehaviour
    {
        [SerializeField] private TrimmerUpgradeState upgrades;
        [SerializeField] private UnityEngine.UI.Button upgradeButton;
        [SerializeField] private TMP_Text label;

        private void OnEnable()
        {
            if (upgrades == null || !upgrades.IsConfigured || upgradeButton == null || label == null)
            { Debug.LogError("Trimmer upgrade view requires valid state, button and label.", this); enabled = false; return; }
            upgrades.Changed += Refresh; upgrades.Economy.Changed += Refresh;
            upgradeButton.onClick.AddListener(Upgrade); Refresh();
        }
        private void Upgrade() => upgrades.TryUpgrade();
        private void Refresh()
        {
            upgradeButton.interactable = upgrades.CanUpgrade;
            if (upgrades.IsMaxLevel) label.SetText("Trimmer Lv {0}\nMAX", upgrades.Level);
            else label.SetText(upgrades.CanUpgrade ? "Trimmer Lv {0} -> {1}\nUpgrade: {2} Coins"
                : "Trimmer Lv {0} -> {1}\nUpgrade: {2} Coins\nNeed Coins", upgrades.Level, upgrades.Level + 1, upgrades.NextPrice);
        }
        private void OnDisable()
        {
            if (upgrades != null)
            {
                upgrades.Changed -= Refresh;
                if (upgrades.Economy != null) upgrades.Economy.Changed -= Refresh;
            }
            if (upgradeButton != null) upgradeButton.onClick.RemoveListener(Upgrade);
        }
    }
}
