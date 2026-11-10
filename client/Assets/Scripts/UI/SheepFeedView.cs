using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>One reusable per-sheep feed button and boost indicator. Rules remain in the service/state.</summary>
    public sealed class SheepFeedView : MonoBehaviour
    {
        [SerializeField] private SheepFeedingService feeding;
        [SerializeField] private UnityEngine.UI.Button button;
        [SerializeField] private TMP_Text label;
        private PrototypeSheepState sheep;
        private int shownSeconds = -1;
        public void Bind(PrototypeSheepState target)
        {
            if (sheep != null) sheep.Changed -= Refresh;
            sheep = target; gameObject.SetActive(target != null);
            if (isActiveAndEnabled) { if (sheep != null) { sheep.Changed -= Refresh; sheep.Changed += Refresh; } Refresh(); }
        }
        private void OnEnable()
        {
            if (feeding == null || !feeding.IsConfigured || button == null || label == null)
            { Debug.LogError("Feed view requires configured service, button and label.", this); enabled = false; return; }
            feeding.Changed += Refresh; feeding.Economy.Changed += Refresh;
            if (sheep != null) sheep.Changed += Refresh;
            button.onClick.AddListener(Feed); Refresh();
        }
        private void Update()
        {
            if (sheep != null && Mathf.CeilToInt(sheep.FeedSecondsRemaining) != shownSeconds) Refresh();
        }
        private void Feed() { feeding.TryFeed(sheep); Refresh(); }
        private void Refresh()
        {
            if (feeding == null || button == null || label == null) return;
            var availability = feeding.GetAvailability(sheep);
            button.interactable = availability == SheepFeedingService.Availability.Available;
            shownSeconds = sheep == null ? 0 : Mathf.CeilToInt(sheep.FeedSecondsRemaining);
            if (availability == SheepFeedingService.Availability.BoostActive)
                label.SetText("BOOST x{0}\n{1}s remaining", sheep.FeedMultiplier, shownSeconds);
            else if (availability == SheepFeedingService.Availability.AlreadyReady) label.text = "Feed\nAlready Ready";
            else if (availability == SheepFeedingService.Availability.Shearing) label.text = "Feed\nShearing";
            else if (availability == SheepFeedingService.Availability.Unavailable) label.text = "Feed\nUnavailable";
            else label.SetText(availability == SheepFeedingService.Availability.NeedCoins ? "Feed: {0} Coins\nx{1} for {2}s - Need Coins" : "Feed: {0} Coins\nx{1} for {2}s", feeding.Food.Cost, feeding.Food.Multiplier, feeding.Food.Duration);
        }
        private void OnDisable()
        {
            if (sheep != null) sheep.Changed -= Refresh;
            if (feeding != null) { feeding.Changed -= Refresh; if (feeding.Economy != null) feeding.Economy.Changed -= Refresh; }
            if (button != null) button.onClick.RemoveListener(Feed);
        }
    }
}
