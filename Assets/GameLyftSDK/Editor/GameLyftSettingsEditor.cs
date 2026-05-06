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
        private const string SOLAR_ENGINE_DEFINE = "GAMELYFT_SOLAR_ENGINE";
        private const string APPSFLYER_DEFINE = "GAMELYFT_APPSFLYER";
        private const string ADJUST_DEFINE = "GAMELYFT_ADJUST";
        private const string SINGULAR_DEFINE = "GAMELYFT_SINGULAR";
        private const string TENJIN_DEFINE = "GAMELYFT_TENJIN";

        private SerializedProperty _useAdMob;
        private SerializedProperty _useAppLovin;
        private SerializedProperty _enableSolarEngineMmp;
        private SerializedProperty _enableAppsFlyerMmp;
        private SerializedProperty _enableAdjustMmp;
        private SerializedProperty _enableSingularMmp;
        private SerializedProperty _enableTenjinMmp;
        private SerializedProperty _testMode;
        private SerializedProperty _autoInitialize;

        private void OnEnable()
        {
            _useAdMob = serializedObject.FindProperty("useAdMobMediation");
            _useAppLovin = serializedObject.FindProperty("useAppLovinMax");
            _enableSolarEngineMmp = serializedObject.FindProperty("enableSolarEngineMmp");
            _enableAppsFlyerMmp = serializedObject.FindProperty("enableAppsFlyerMmp");
            _enableAdjustMmp = serializedObject.FindProperty("enableAdjustMmp");
            _enableSingularMmp = serializedObject.FindProperty("enableSingularMmp");
            _enableTenjinMmp = serializedObject.FindProperty("enableTenjinMmp");
            _testMode = serializedObject.FindProperty("testMode");
            _autoInitialize = serializedObject.FindProperty("autoInitialize");

            // Asset bool is the source of truth — re-apply it to the scripting
            // defines whenever the inspector opens. The ChangeCheck pattern below
            // only fires on toggle, so without this an out-of-sync asset (manual
            // edit, fresh import, machine switch) would silently leave defines
            // disagreeing with the checkbox state.
            DefineSymbolManager.SetDefine(ADMOB_DEFINE, _useAdMob.boolValue);
            DefineSymbolManager.SetDefine(APPLOVIN_DEFINE, _useAppLovin.boolValue);
            DefineSymbolManager.SetDefine(SOLAR_ENGINE_DEFINE, _enableSolarEngineMmp.boolValue);
            DefineSymbolManager.SetDefine(APPSFLYER_DEFINE, _enableAppsFlyerMmp.boolValue);
            DefineSymbolManager.SetDefine(ADJUST_DEFINE, _enableAdjustMmp.boolValue);
            DefineSymbolManager.SetDefine(SINGULAR_DEFINE, _enableSingularMmp.boolValue);
            DefineSymbolManager.SetDefine(TENJIN_DEFINE, _enableTenjinMmp.boolValue);
        }

        // Reconcile on every editor reload AND auto-create the settings asset on
        // first install. Catches drift introduced by source control, package
        // re-imports, or anything that touches the .asset without going through
        // the inspector. SetDefine is a no-op when the symbol is already in the
        // desired state, so this won't trigger spurious recompile loops.
        //
        // Why delayCall: AssetDatabase.CreateAsset directly inside an
        // InitializeOnLoadMethod can race with Unity's own asset import phase
        // (especially right after a UPM package import). Deferring one editor
        // frame lets Unity become idle before we write.
        [InitializeOnLoadMethod]
        private static void ReconcileDefinesOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                // LoadOrCreate auto-creates the asset if missing — first run after
                // a fresh install gets a brand-new asset with all bool fields
                // defaulting to false (per the public field defaults in
                // GameLyftSettings.cs). Consumers don't have to manually open
                // Tools → GameLyft → Settings to get a baseline asset.
                var settings = LoadOrCreate();
                if (settings == null) return;

                DefineSymbolManager.SetDefine(ADMOB_DEFINE, settings.useAdMobMediation);
                DefineSymbolManager.SetDefine(APPLOVIN_DEFINE, settings.useAppLovinMax);
                DefineSymbolManager.SetDefine(SOLAR_ENGINE_DEFINE, settings.enableSolarEngineMmp);
                DefineSymbolManager.SetDefine(APPSFLYER_DEFINE, settings.enableAppsFlyerMmp);
                DefineSymbolManager.SetDefine(ADJUST_DEFINE, settings.enableAdjustMmp);
                DefineSymbolManager.SetDefine(SINGULAR_DEFINE, settings.enableSingularMmp);
                DefineSymbolManager.SetDefine(TENJIN_DEFINE, settings.enableTenjinMmp);
            };
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
            EditorGUILayout.LabelField("MMP (Attribution)", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Enable an MMP integration to auto-report attribution as a one-shot 'mmp_install' " +
                "Firebase event. Each integration polls its own SDK for attribution after init, " +
                "then maps source/campaign/ad_set/creative to the standard schema. The install " +
                "event is guarded to fire at most once per device install regardless of how many " +
                "MMPs are enabled — whichever delivers attribution first wins.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            bool solarMmp = EditorGUILayout.ToggleLeft("Solar Engine MMP", _enableSolarEngineMmp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _enableSolarEngineMmp.boolValue = solarMmp;
                DefineSymbolManager.SetDefine(SOLAR_ENGINE_DEFINE, solarMmp);
            }

            EditorGUI.BeginChangeCheck();
            bool appsFlyerMmp = EditorGUILayout.ToggleLeft("AppsFlyer MMP", _enableAppsFlyerMmp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _enableAppsFlyerMmp.boolValue = appsFlyerMmp;
                DefineSymbolManager.SetDefine(APPSFLYER_DEFINE, appsFlyerMmp);
            }

            EditorGUI.BeginChangeCheck();
            bool adjustMmp = EditorGUILayout.ToggleLeft("Adjust MMP", _enableAdjustMmp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _enableAdjustMmp.boolValue = adjustMmp;
                DefineSymbolManager.SetDefine(ADJUST_DEFINE, adjustMmp);
            }

            EditorGUI.BeginChangeCheck();
            bool singularMmp = EditorGUILayout.ToggleLeft("Singular MMP", _enableSingularMmp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _enableSingularMmp.boolValue = singularMmp;
                DefineSymbolManager.SetDefine(SINGULAR_DEFINE, singularMmp);
            }

            EditorGUI.BeginChangeCheck();
            bool tenjinMmp = EditorGUILayout.ToggleLeft("Tenjin MMP", _enableTenjinMmp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                _enableTenjinMmp.boolValue = tenjinMmp;
                DefineSymbolManager.SetDefine(TENJIN_DEFINE, tenjinMmp);
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
            EditorGUILayout.LabelField("GAMELYFT_SOLAR_ENGINE:", DefineSymbolManager.HasDefine(SOLAR_ENGINE_DEFINE) ? "ON" : "off");
            EditorGUILayout.LabelField("GAMELYFT_APPSFLYER:", DefineSymbolManager.HasDefine(APPSFLYER_DEFINE) ? "ON" : "off");
            EditorGUILayout.LabelField("GAMELYFT_ADJUST:", DefineSymbolManager.HasDefine(ADJUST_DEFINE) ? "ON" : "off");
            EditorGUILayout.LabelField("GAMELYFT_SINGULAR:", DefineSymbolManager.HasDefine(SINGULAR_DEFINE) ? "ON" : "off");
            EditorGUILayout.LabelField("GAMELYFT_TENJIN:", DefineSymbolManager.HasDefine(TENJIN_DEFINE) ? "ON" : "off");

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
