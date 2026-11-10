using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Read-only presentation of the selected sheep's progress.</summary>
    public sealed class ShearingProgressView : MonoBehaviour
    {
        [SerializeField] private ShearingProgress progress;
        [SerializeField] private TMP_Text percentageLabel;
        [SerializeField] private TMP_Text woolLabel;
        [SerializeField] private UnityEngine.UI.Image progressFill;
        [SerializeField] private GameObject completionLabel;

        private void OnEnable()
        {
            if (progress == null || percentageLabel == null || woolLabel == null ||
                progressFill == null || completionLabel == null)
            {
                Debug.LogError("ShearingProgressView requires progress and all HUD references.", this);
                enabled = false;
                return;
            }
            progress.Changed += Refresh;
            Refresh();
        }

        private void Start() => Refresh(); // All sheep Awake callbacks have now initialized data.

        private void Refresh()
        {
            percentageLabel.SetText("Sheared {0:1}%", progress.ShearedPercent);
            woolLabel.SetText("Wool {0:1} / {1:0}", progress.WoolEarned, progress.FullWoolAmount);
            progressFill.fillAmount = progress.Sheared01;
            completionLabel.SetActive(progress.IsComplete);
        }

        private void OnDisable()
        {
            if (progress != null) progress.Changed -= Refresh;
        }
    }
}
