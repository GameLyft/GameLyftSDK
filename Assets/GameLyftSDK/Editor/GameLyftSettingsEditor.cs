using System.Collections.Generic;
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

        private GameLyftSettings _s;

        // Staged (un-applied) toggle state. Plain bools — NOT a live SerializedObject —
        // so edits survive repaints and only commit on Apply.
        private bool _admob, _applovin, _solar, _appsflyer, _adjust, _singular, _tenjin;
        private bool _testMode, _autoInit;

        private void OnEnable()
        {
            _s = (GameLyftSettings)target;

            // Asset bool is the source of truth — re-apply it to the scripting defines
            // whenever the inspector opens (catches drift from a manual edit, fresh
            // import, or machine switch). Batched: one SetScriptingDefineSymbols write
            // per build target at most.
            DefineSymbolManager.SetDefines(DesiredDefinesFromAsset(_s));

            LoadStagedFromAsset();
        }

        private void LoadStagedFromAsset()
        {
            _admob = _s.useAdMobMediation;
            _applovin = _s.useAppLovinMax;
            _solar = _s.enableSolarEngineMmp;
            _appsflyer = _s.enableAppsFlyerMmp;
            _adjust = _s.enableAdjustMmp;
            _singular = _s.enableSingularMmp;
            _tenjin = _s.enableTenjinMmp;
            _testMode = _s.testMode;
            _autoInit = _s.autoInitialize;
        }

        // Reconcile on every editor reload AND auto-create the settings asset on first
        // install. Catches drift from source control, package re-imports, or anything
        // that touches the .asset directly. SetDefines is a no-op when symbols already
        // match, so this won't trigger spurious recompile loops. delayCall defers one
        // frame so AssetDatabase.CreateAsset doesn't race Unity's import phase.
        [InitializeOnLoadMethod]
        private static void ReconcileDefinesOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                var settings = LoadOrCreate();
                if (settings == null) return;
                DefineSymbolManager.SetDefines(DesiredDefinesFromAsset(settings));
            };
        }

        private static Dictionary<string, bool> DesiredDefinesFromAsset(GameLyftSettings s)
        {
            return new Dictionary<string, bool>
            {
                { ADMOB_DEFINE, s.useAdMobMediation },
                { APPLOVIN_DEFINE, s.useAppLovinMax },
                { SOLAR_ENGINE_DEFINE, s.enableSolarEngineMmp },
                { APPSFLYER_DEFINE, s.enableAppsFlyerMmp },
                { ADJUST_DEFINE, s.enableAdjustMmp },
                { SINGULAR_DEFINE, s.enableSingularMmp },
                { TENJIN_DEFINE, s.enableTenjinMmp },
            };
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("GameLyft SDK Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Changes below are STAGED — nothing is written until you press Apply. " +
                "Toggle as many integrations as you want, then Apply once: the asset is " +
                "saved and all scripting defines update in a single recompile.",
                MessageType.Info);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Mediation", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Tick whichever ad mediation SDK(s) your project uses. Applying writes the " +
                "scripting defines (GAMELYFT_ADMOB / GAMELYFT_APPLOVIN) so the matching " +
                "AdRevenue.Report overload compiles in. Enabling both is fine.",
                MessageType.None);

            _admob = StagedToggle("AdMob Mediation", _admob, ADMOB_DEFINE);
            _applovin = StagedToggle("AppLovin MAX Mediation", _applovin, APPLOVIN_DEFINE);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MMP (Attribution)", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Enable an MMP integration to auto-report attribution as a one-shot " +
                "'mmp_install' Firebase event (deduped — whichever MMP delivers first wins).",
                MessageType.None);

            _solar = StagedToggle("Solar Engine MMP", _solar, SOLAR_ENGINE_DEFINE);
            _appsflyer = StagedToggle("AppsFlyer MMP", _appsflyer, APPSFLYER_DEFINE);
            _adjust = StagedToggle("Adjust MMP", _adjust, ADJUST_DEFINE);
            _singular = StagedToggle("Singular MMP", _singular, SINGULAR_DEFINE);
            _tenjin = StagedToggle("Tenjin MMP", _tenjin, TENJIN_DEFINE);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Initialization", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Auto Initialize polls for Firebase at app start and calls " +
                "GameLyftAnalytics.Initialize() once it's ready. Times out after 5 minutes.",
                MessageType.None);
            _autoInit = EditorGUILayout.ToggleLeft(
                "Auto Initialize" + PendingSuffix(_autoInit != _s.autoInitialize), _autoInit);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Test Mode also shows SDK integration warnings on an on-screen panel. " +
                "Turn OFF for production.",
                MessageType.None);
            _testMode = EditorGUILayout.ToggleLeft(
                "Test Mode" + PendingSuffix(_testMode != _s.testMode), _testMode);

            // ===== Apply / Revert =====
            EditorGUILayout.Space();
            bool pending = HasPendingChanges();

            if (pending)
            {
                EditorGUILayout.HelpBox(
                    "Unapplied changes. Press Apply to save the asset and update the " +
                    "scripting defines (one recompile).", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!pending))
                {
                    if (GUILayout.Button("Apply", GUILayout.Height(28)))
                        ApplyAll();

                    if (GUILayout.Button("Revert", GUILayout.Height(28), GUILayout.Width(90)))
                    {
                        LoadStagedFromAsset();
                        GUI.FocusControl(null);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status (currently applied defines)", EditorStyles.miniBoldLabel);
            DrawStatusLine(ADMOB_DEFINE, _admob);
            DrawStatusLine(APPLOVIN_DEFINE, _applovin);
            DrawStatusLine(SOLAR_ENGINE_DEFINE, _solar);
            DrawStatusLine(APPSFLYER_DEFINE, _appsflyer);
            DrawStatusLine(ADJUST_DEFINE, _adjust);
            DrawStatusLine(SINGULAR_DEFINE, _singular);
            DrawStatusLine(TENJIN_DEFINE, _tenjin);
        }

        private bool HasPendingChanges()
        {
            return _admob != _s.useAdMobMediation
                || _applovin != _s.useAppLovinMax
                || _solar != _s.enableSolarEngineMmp
                || _appsflyer != _s.enableAppsFlyerMmp
                || _adjust != _s.enableAdjustMmp
                || _singular != _s.enableSingularMmp
                || _tenjin != _s.enableTenjinMmp
                || _testMode != _s.testMode
                || _autoInit != _s.autoInitialize;
        }

        // Toggle whose "(pending Apply)" suffix appears when the staged value differs
        // from the currently-applied scripting define.
        private bool StagedToggle(string label, bool staged, string define)
        {
            bool applied = DefineSymbolManager.HasDefine(define);
            return EditorGUILayout.ToggleLeft(label + PendingSuffix(staged != applied), staged);
        }

        private static string PendingSuffix(bool pending) => pending ? "   (pending Apply)" : "";

        private void DrawStatusLine(string define, bool staged)
        {
            bool applied = DefineSymbolManager.HasDefine(define);
            string state = applied ? "ON" : "off";
            if (staged != applied)
                state += staged ? "   →  ON after Apply" : "   →  off after Apply";
            EditorGUILayout.LabelField(define + ":", state);
        }

        private void ApplyAll()
        {
            // 1) Commit staged values to the asset and persist it.
            _s.useAdMobMediation = _admob;
            _s.useAppLovinMax = _applovin;
            _s.enableSolarEngineMmp = _solar;
            _s.enableAppsFlyerMmp = _appsflyer;
            _s.enableAdjustMmp = _adjust;
            _s.enableSingularMmp = _singular;
            _s.enableTenjinMmp = _tenjin;
            _s.testMode = _testMode;
            _s.autoInitialize = _autoInit;

            EditorUtility.SetDirty(_s);
            AssetDatabase.SaveAssets();

            // 2) Write ALL scripting defines in one batched pass (at most one
            //    SetScriptingDefineSymbols call per build target → a single recompile).
            DefineSymbolManager.SetDefines(DesiredDefinesFromAsset(_s));

            GUI.FocusControl(null);
            Debug.Log("[GameLyft] Settings applied — asset saved and scripting defines updated.");
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
