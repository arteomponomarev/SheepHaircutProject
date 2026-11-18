using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// One persistent object (DontDestroyOnLoad) that advances <see cref="WoolGrowthClock"/> game time in every scene
    /// and saves it every few seconds, on scene changes, pause and quit. Created once by <see cref="EconomyBootstrap"/>.
    /// </summary>
    public sealed class WoolGrowthClockDriver : MonoBehaviour
    {
        const double SaveInterval = 15.0;
        static WoolGrowthClockDriver _instance;

        public static void Ensure()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("[WoolGrowthClock]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WoolGrowthClockDriver>();
        }

        void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;
        void OnDisable() => SceneManager.activeSceneChanged -= OnSceneChanged;
        void OnSceneChanged(Scene a, Scene b) { WoolGrowthClock.Save(); GameClock.Save(true); }

        void Update()
        {
            WoolGrowthClock.Advance(Time.unscaledDeltaTime);
            WoolGrowthClock.SaveIfDue(SaveInterval);
            GameClock.Save(); // throttled internally (high-water mark for rollback protection)
        }

        void OnApplicationPause(bool paused) { if (paused) { WoolGrowthClock.Save(); GameClock.Save(true); } }
        void OnApplicationQuit() { WoolGrowthClock.Save(); GameClock.Save(true); }
        void OnDestroy() { if (_instance == this) { WoolGrowthClock.Save(); GameClock.Save(true); _instance = null; } }
    }
}
