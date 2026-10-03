using System.Globalization;
using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Herd paging, per-sheep selection controls and purchase UI. No economy or growth rules.</summary>
    public sealed class FarmHerdView : MonoBehaviour
    {
        [SerializeField] private SheepManager manager;
        [SerializeField] private PrototypeSceneNavigation navigation;
        [SerializeField] private FarmHerdDisplay display;
        [SerializeField] private TMP_Text countLabel, pageLabel, buyLabel;
        [SerializeField] private UnityEngine.UI.Button buyButton, previousButton, nextButton;
        [SerializeField] private UnityEngine.UI.Button[] sheepButtons;
        [SerializeField] private TMP_Text[] sheepLabels;
        private PrototypeSheepState[] shown;
        private UnityEngine.Events.UnityAction[] selectActions;
        private int page;
        private int PageCount => (manager.Roster.Count + sheepButtons.Length - 1) / sheepButtons.Length;

        private void OnEnable()
        {
            if (manager == null || !manager.IsConfigured || navigation == null || display == null ||
                countLabel == null || pageLabel == null || buyLabel == null || buyButton == null || previousButton == null || nextButton == null ||
                sheepButtons == null || sheepButtons.Length == 0 || sheepLabels == null || sheepLabels.Length != sheepButtons.Length || display.SlotCount != sheepButtons.Length)
            { Debug.LogError("Farm herd view requires manager, display and UI references.", this); enabled = false; return; }
            shown = new PrototypeSheepState[sheepButtons.Length]; selectActions = new UnityEngine.Events.UnityAction[sheepButtons.Length];
            for (int i = 0; i < sheepButtons.Length; i++)
            { int slot = i; selectActions[i] = () => Select(slot); sheepButtons[i].onClick.AddListener(selectActions[i]); }
            manager.Roster.Changed += HerdChanged; manager.Economy.Changed += RefreshPurchase;
            manager.Changed += RefreshPurchase;
            manager.Roster.Barn.Changed += RefreshPurchase;
            buyButton.onClick.AddListener(Buy); previousButton.onClick.AddListener(Previous); nextButton.onClick.AddListener(Next);
            page = manager.Roster.SelectedIndex / sheepButtons.Length; BindPage();
        }
        private void Buy() { manager.TryBuySheep(); RefreshPurchase(); }
        private void Previous() { if (page > 0) { page--; BindPage(); } }
        private void Next() { if (page + 1 < PageCount) { page++; BindPage(); } }
        private void Select(int slot)
        { if (!navigation.IsLoading && manager.TrySelectSheep(page * sheepButtons.Length + slot)) navigation.OpenShearing(); }
        private void HerdChanged() { page = PageCount - 1; BindPage(); }
        private void UnsubscribeSheep()
        { if (shown != null) foreach (var sheep in shown) if (sheep != null) sheep.Changed -= RefreshSheep; }
        private void BindPage()
        {
            UnsubscribeSheep(); page = Mathf.Clamp(page, 0, Mathf.Max(0, PageCount - 1));
            for (int i = 0; i < shown.Length; i++)
            {
                shown[i] = manager.Roster.GetSheep(page * shown.Length + i);
                if (shown[i] != null) shown[i].Changed += RefreshSheep;
                sheepButtons[i].gameObject.SetActive(shown[i] != null); display.Show(i, shown[i]);
            }
            pageLabel.SetText("Page {0} / {1}", page + 1, PageCount);
            previousButton.interactable = page > 0; nextButton.interactable = page + 1 < PageCount;
            RefreshSheep(); RefreshPurchase();
        }
        private void RefreshSheep()
        {
            for (int i = 0; i < shown.Length; i++)
            {
                var sheep = shown[i]; if (sheep == null) continue;
                int index = page * shown.Length + i; bool ready = sheep.Status == PrototypeSheepState.SheepStatus.Ready;
                sheepButtons[i].interactable = manager.Roster.CanSelect(index);
                if (ready) sheepLabels[i].SetText("Sheep {0} - Ready\nShear", index + 1);
                else if (sheep.Status == PrototypeSheepState.SheepStatus.Shearing) sheepLabels[i].SetText("Sheep {0}\nShearing", index + 1);
                else sheepLabels[i].SetText("Sheep {0}\nGrowing {1:0}%", index + 1, Mathf.Floor(sheep.WoolGrowth01 * 100));
            }
        }
        private void RefreshPurchase()
        {
            countLabel.SetText("Sheep: {0} / {1}", manager.Roster.Count, manager.Roster.Barn.Capacity);
            buyButton.interactable = manager.CanBuySheep;
            if (manager.IsBarnFull)
            {
                buyLabel.text = manager.Roster.Barn.IsMaxLevel ? "Barn Full\nMaximum Capacity Reached" : "Barn Full\nUpgrade Barn";
                return;
            }
            buyLabel.text = "Buy Sheep: " + manager.NextSheepPrice.ToString("0", CultureInfo.InvariantCulture) + " Coins"
                + (manager.CanBuySheep ? "" : "\nNeed Coins");
        }
        private void OnDisable()
        {
            UnsubscribeSheep();
            if (manager != null)
            {
                manager.Changed -= RefreshPurchase;
                if (manager.Roster != null)
                {
                    manager.Roster.Changed -= HerdChanged;
                    if (manager.Roster.Barn != null) manager.Roster.Barn.Changed -= RefreshPurchase;
                }
                if (manager.Economy != null) manager.Economy.Changed -= RefreshPurchase;
            }
            if (buyButton != null) buyButton.onClick.RemoveListener(Buy);
            if (previousButton != null) previousButton.onClick.RemoveListener(Previous);
            if (nextButton != null) nextButton.onClick.RemoveListener(Next);
            if (selectActions != null) for (int i = 0; i < selectActions.Length; i++) sheepButtons[i].onClick.RemoveListener(selectActions[i]);
        }
    }
}
