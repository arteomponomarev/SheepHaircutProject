using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// Navigation between the farm (home screen) and the activities. Every activity returns to <see cref="Home"/>.
    /// </summary>
    public static class SceneNavigator
    {
        /// <summary>The farm: first scene of the game and the return target of every activity.</summary>
        public const string Home = "FarmHub";
        /// <summary>Kept for existing call sites ("return to hub") – now the farm.</summary>
        public const string Hub = Home;
        /// <summary>Old temporary menu (dev tool, not in the build).</summary>
        public const string DevMenu = "Hub";
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
