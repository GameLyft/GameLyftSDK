using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Root of the GameLyft prefab (GameLyftSDK/Runtime/Prefabs/GameLyft.prefab). Put the prefab in the
    /// FIRST scene of the game (the one loaded once per launch, usually the loading scene, index 0).
    /// It survives scene loads, initializes the SDK and hosts GL_Engagement.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameLyftManager : MonoBehaviour
    {
        private static GameLyftManager _instance;

        internal static bool Exists => _instance != null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // The first scene was loaded again: keep the original.
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            if (GetComponent<GL_Engagement>() == null) gameObject.AddComponent<GL_Engagement>();
            GameLyftAnalytics.Initialize();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
