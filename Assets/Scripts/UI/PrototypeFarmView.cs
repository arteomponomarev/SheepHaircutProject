using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Read-only farm status and shearing entry UI. Does not simulate growth.</summary>
    public sealed class PrototypeFarmView : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private PrototypeSceneNavigation navigation;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private UnityEngine.UI.Button shearButton;

        private void OnEnable()
        {
            if (sheep == null || navigation == null || statusLabel == null || shearButton == null)
            { Debug.LogError("Farm view has missing references.", this); enabled = false; return; }
            sheep.Changed += Refresh; shearButton.onClick.AddListener(Enter); Refresh();
        }
        private void Enter()
        {
            if (sheep.Status == PrototypeSheepState.SheepStatus.Ready) navigation.OpenShearing();
        }
        private void Refresh()
        {
            bool ready = sheep.Status == PrototypeSheepState.SheepStatus.Ready;
            shearButton.interactable = ready;
            if (ready) statusLabel.text = "Sheep ready to shear";
            else statusLabel.SetText("Wool growing: {0:0}%\nLast yield: {1:1} Wool", Mathf.Floor(sheep.WoolGrowth01 * 100f), sheep.ResultWool);
        }
        private void OnDisable()
        {
            if (sheep != null) sheep.Changed -= Refresh;
            if (shearButton != null) shearButton.onClick.RemoveListener(Enter);
        }
    }
}
