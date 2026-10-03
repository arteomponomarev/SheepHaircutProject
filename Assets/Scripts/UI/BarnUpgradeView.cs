using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Displays configured barn upgrade offers and forwards clicks, without owning purchase rules.</summary>
    public sealed class BarnUpgradeView : MonoBehaviour
    {
        [SerializeField] private BarnController barn;
        [SerializeField] private UnityEngine.UI.Button upgradeButton;
        [SerializeField] private TMP_Text label;
        private void OnEnable()
        {
            if (barn == null || !barn.IsConfigured || upgradeButton == null || label == null)
            { Debug.LogError("Barn upgrade view requires configured barn and UI references.", this); enabled = false; return; }
            barn.Changed += Refresh; barn.Economy.Changed += Refresh;
            upgradeButton.onClick.AddListener(Upgrade); Refresh();
        }
        private void Upgrade() => barn.TryUpgrade();
        private void Refresh()
        {
            upgradeButton.interactable = barn.CanUpgrade;
            if (barn.IsMaxLevel) label.SetText("Barn Lv {0} - MAX\nCapacity: {1}", barn.Level, barn.Capacity);
            else label.SetText(barn.CanUpgrade ? "Upgrade Barn Lv {0} -> {1}\n{2} Coins - Capacity {3}"
                : "Upgrade Barn Lv {0} -> {1}\n{2} Coins - Capacity {3}\nNeed Coins", barn.Level, barn.Level + 1, barn.NextPrice, barn.NextCapacity);
        }
        private void OnDisable()
        {
            if (barn != null) { barn.Changed -= Refresh; if (barn.Economy != null) barn.Economy.Changed -= Refresh; }
            if (upgradeButton != null) upgradeButton.onClick.RemoveListener(Upgrade);
        }
    }
}
