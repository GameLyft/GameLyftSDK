using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameLyft.Sdk.EditorTools
{
    [CustomEditor(typeof(GameLyftSettings))]
    internal class GameLyftSettingsEditor : Editor
    {
        private const string ADMOB_DEFINE = "GAMELYFT_ADMOB";
        private const string APPLOVIN_DEFINE = "GAMELYFT_APPLOVIN";

        private SerializedProperty _useAdMob;
        private SerializedProperty _useAppLovin;
        private SerializedProperty _testMode;
        private SerializedProperty _autoInitialize;

        private void OnEnable()
        {
            _useAdMob = serializedObject.FindProperty("useAdMobMediation");
            _useAppLovin = serializedObject.FindProperty("useAppLovinMax");
            _testMode = serializedObject.FindProperty("testMode");
            _autoInitialize = serializedObject.FindProperty("autoInitialize");

            // Asset bool is the source of truth — re-apply it to the scripting
            // defines whenever the inspector opens. The ChangeCheck pattern below
            // only fires on toggle, so without this an out-of-sync asset (manual
            // edit, fresh import, machine switch) would silently leave defines
            // disagreeing with the checkbox state.
            DefineSymbolManager.SetDefine(ADMOB_DEFINE, _useAdMob.boolValue);
            DefineSymbolManager.SetDefine(APPLOVIN_DEFINE, _useAppLovin.boolValue);
        }

        // Reconcile on every editor reload, even when the inspector never opens.
        // Catches drift introduced by source control, package re-imports, or
        // anything that touches the .asset without going through the inspector.
        // SetDefine is a no-op when the symbol is already in the desired state,
        // so this won't trigger spurious recompile loops.
        [InitializeOnLoadMethod]
        private static void ReconcileDefinesOnLoad()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GameLyftSettings>(
                GameLyftSettings.DEFAULT_ASSET_PATH);
            if (settings == null) return;

            DefineSymbolManager.SetDefine(ADMOB_DEFINE, settings.useAdMobMediation);
            DefineSymbolManager.SetDefine(APPLOVIN_DEFINE, settings.useAppLovinMax);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("GameLyft SDK Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Mediation", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Tick whichever ad mediation SDK(s) your project uses. Toggling these writes " +
                "scripting define symbols (GAMELYFT_ADMOB / GAMELYFT_APPLOVIN) so the matching " +
                "ReportAdRevenue overload is compiled in. Enabling both is fine — the overloads " +
                "are distinguished by parameter type (AdValue vs MaxSdkBase.AdInfo).",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            bool admob = EditorGUILayout.ToggleLeft("AdMob Mediation", _useAdMob.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _useAdMob.boolValue = admob;
                DefineSymbolManager.SetDefine(ADMOB_DEFINE, admob);
            }

            EditorGUI.BeginChangeCheck();
            bool applovin = EditorGUILayout.ToggleLeft("AppLovin MAX Mediation", _useAppLovin.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _useAppLovin.boolValue = applovin;
                DefineSymbolManager.SetDefine(APPLOVIN_DEFINE, applovin);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Initialization", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Auto Initialize polls for Firebase readiness at app start and calls "
                + "GameLyftAnalytics.Initialize() automatically once it detects Firebase is up. "
                + "Requires zero code changes on your side. Times out after 5 minutes if "
                + "Firebase never initializes.",
                MessageType.Info);

            EditorGUILayout.PropertyField(_autoInitialize, new GUIContent("Auto Initialize"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Test Mode routes SDK integration warnings (e.g. calls made before Initialize(), "
                + "null ad callbacks, param limits exceeded) to an on-screen IMGUI panel in addition to "
                + "the console. Turn this OFF for production builds — warnings will still go to the "
                + "console, just not shown on-screen.",
                MessageType.Info);

            EditorGUILayout.PropertyField(_testMode, new GUIContent("Test Mode"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("GAMELYFT_ADMOB:", DefineSymbolManager.HasDefine(ADMOB_DEFINE) ? "ON" : "off");
            EditorGUILayout.LabelField("GAMELYFT_APPLOVIN:", DefineSymbolManager.HasDefine(APPLOVIN_DEFINE) ? "ON" : "off");

            serializedObject.ApplyModifiedProperties();
        }

        // === Menu / asset bootstrap ===

        [MenuItem("Tools/GameLyft/Settings", priority = 100)]
        public static void OpenOrCreateSettings()
        {
            var settings = LoadOrCreate();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private static GameLyftSettings LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameLyftSettings>(GameLyftSettings.DEFAULT_ASSET_PATH);
            if (existing != null) return existing;

            string dir = Path.GetDirectoryName(GameLyftSettings.DEFAULT_ASSET_PATH);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<GameLyftSettings>();
            AssetDatabase.CreateAsset(asset, GameLyftSettings.DEFAULT_ASSET_PATH);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
