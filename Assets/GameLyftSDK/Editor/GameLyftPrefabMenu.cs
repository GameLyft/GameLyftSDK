using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameLyft.Sdk.EditorTools
{
    /// <summary>Tools → GameLyft → Add GameLyft Prefab to Scene: drops the prefab into the open scene.</summary>
    internal static class GameLyftPrefabMenu
    {
        private const string PREFAB_NAME = "GameLyft";

        [MenuItem("Tools/GameLyft/Add GameLyft Prefab to Scene", priority = 101)]
        internal static void AddToScene()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindAnyObjectByType<GameLyftManager>() != null)
#else
            if (Object.FindObjectOfType<GameLyftManager>() != null)
#endif
            {
                EditorUtility.DisplayDialog("GameLyft", "This scene already has the GameLyft prefab.", "OK");
                return;
            }
            GameObject prefab = null;
            foreach (var guid in AssetDatabase.FindAssets(PREFAB_NAME + " t:Prefab"))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && p.GetComponent<GameLyftManager>() != null) { prefab = p; break; }
            }
            GameObject go = prefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
                : new GameObject(PREFAB_NAME, typeof(GameLyftManager), typeof(GL_Engagement));
            Undo.RegisterCreatedObjectUndo(go, "Add GameLyft");
            Selection.activeObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[GameLyft] Added the GameLyft prefab to '" + go.scene.name
                + "'. Make sure this is the FIRST scene of the game (Build Settings index 0).");
        }
    }
}
