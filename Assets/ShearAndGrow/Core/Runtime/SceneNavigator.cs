using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// Temporary navigation between the mini-games (later each entry point becomes a farm building).
    /// </summary>
    public static class SceneNavigator
    {
        public const string Hub = "Hub";
        public const string Shearing = "Shearing_Feature_Test";
        public const string Wash = "WoolWash";
        public const string Dry = "WoolDry";

        static bool _loading;

        public static bool IsInBuild(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) return true;
            }
            return false;
        }

        /// <summary>Loads a scene asynchronously. Repeated calls during a load are ignored (double taps).</summary>
        public static bool Go(string sceneName)
        {
            if (_loading || !IsInBuild(sceneName)) return false;
            _loading = true;
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null) { _loading = false; return false; }
            op.completed += _ => _loading = false;
            return true;
        }
    }
}
