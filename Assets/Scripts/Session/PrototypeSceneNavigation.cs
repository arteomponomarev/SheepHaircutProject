using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShearAndGrow
{
    /// <summary>Small scene transition adapter; contains no sheep or reward rules.</summary>
    public sealed class PrototypeSceneNavigation : MonoBehaviour
    {
        [SerializeField] private string farmScene = "Assets/Scenes/FarmPrototype.unity";
        [SerializeField] private string shearingScene = "Assets/Scenes/ShearingPrototype.unity";
        private bool loading;
        public void OpenFarm() => Open(farmScene);
        public void OpenShearing() => Open(shearingScene);
        private void Open(string path)
        {
            if (loading) return;
            if (!Application.CanStreamedLevelBeLoaded(path)) { Debug.LogError("Prototype scene is not registered: " + path, this); return; }
            loading = true; SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        }
    }
}
