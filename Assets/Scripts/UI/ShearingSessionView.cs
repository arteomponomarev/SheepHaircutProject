using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    public sealed class ShearingSessionView : MonoBehaviour
    {
        [SerializeField] private ShearingSession session;
        [SerializeField] private PrototypeSceneNavigation navigation;
        [SerializeField] private UnityEngine.UI.Button finishButton;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text resultLabel;
        [SerializeField] private UnityEngine.UI.Button returnButton;

        private void OnEnable()
        {
            if (session == null || navigation == null || finishButton == null || resultPanel == null || resultLabel == null || returnButton == null)
            { Debug.LogError("Session view has missing references.", this); enabled = false; return; }
            session.Changed += Refresh; finishButton.onClick.AddListener(session.Finish); returnButton.onClick.AddListener(navigation.OpenFarm);
            Refresh();
        }
        private void Refresh()
        {
            finishButton.gameObject.SetActive(session.CanFinish);
            resultPanel.SetActive(session.HasResult);
            if (session.HasResult) resultLabel.SetText("Shearing complete\n\nSheared {0:1}%\nWool earned {1:1}", session.ResultCoverage * 100f, session.ResultWool);
        }
        private void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
            if (finishButton != null && session != null) finishButton.onClick.RemoveListener(session.Finish);
            if (returnButton != null && navigation != null) returnButton.onClick.RemoveListener(navigation.OpenFarm);
        }
    }
}
