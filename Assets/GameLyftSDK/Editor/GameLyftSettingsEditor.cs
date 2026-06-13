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
        private bool _testMode, _autoInit, _verboseLogging;

        // AppsFlyer conversion-handler wiring (cached scan + last action log). NOT staged —
        // the Wire/Unwire buttons edit the handler .cs immediately.
        private List<AppsFlyerConversionWirer.Handler> _afHandlers = new List<AppsFlyerConversionWirer.Handler>();
        private string _afLog = "";

        private void OnEnable()
        {
            _s = (GameLyftSettings)target;

            // Asset bool is the source of truth — re-apply it to the scripting defines
            // whenever the inspector opens (catches drift from a manual edit, fresh
            // import, or machine switch). Batched: one SetScriptingDefineSymbols write
            // per build target at most.
            DefineSymbolManager.SetDefines(DesiredDefinesFromAsset(_s));

            LoadStagedFromAsset();
            RefreshAfHandlers();
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
            _verboseLogging = _s.verboseLogging;
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

            // One-click AppsFlyer conversion-handler wiring (immediate file edit, not staged).
            if (_appsflyer || DefineSymbolManager.HasDefine(APPSFLYER_DEFINE))
                DrawAppsFlyerWiring();

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
                "Verbose Logging prints detailed [GameLyft] console logs of ALL SDK activity " +
                "(events, queue flush to Firebase, purchases, ad revenue, attribution). " +
                "Turn both OFF for production.",
                MessageType.None);
            _testMode = EditorGUILayout.ToggleLeft(
                "Test Mode" + PendingSuffix(_testMode != _s.testMode), _testMode);
            _verboseLogging = EditorGUILayout.ToggleLeft(
                "Verbose Logging" + PendingSuffix(_verboseLogging != _s.verboseLogging), _verboseLogging);

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
                || _autoInit != _s.autoInitialize
                || _verboseLogging != _s.verboseLogging;
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

        // === AppsFlyer conversion-handler wiring ===

        private void RefreshAfHandlers()
        {
            _afHandlers = AppsFlyerConversionWirer.FindHandlers();
        }

        // Injects/removes the PlayerPrefs bridge at the start of onConversionDataSuccess so the
        // SDK's AppsFlyerMmp can auto-report 'mmp_install'. This is the click-button equivalent
        // of the manual 3-line hookup. Shown only while the AppsFlyer MMP toggle is on.
        private void DrawAppsFlyerWiring()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("AppsFlyer conversion handler  →  PlayerPrefs bridge",
                    EditorStyles.miniBoldLabel);

                int found = _afHandlers.Count;
                int wired = 0;
                for (int i = 0; i < _afHandlers.Count; i++) if (_afHandlers[i].Wired) wired++;

                if (found == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No onConversionDataSuccess(string) handler found in the project. Add the " +
                        "AppsFlyer prefab (AppsFlyerObjectScript) to a scene first, then press Re-scan.",
                        MessageType.Warning);
                }
                else
                {
                    string state = (wired == found) ? "wired" : (wired == 0 ? "NOT wired" : wired + " / " + found + " wired");
                    EditorGUILayout.LabelField("Status:", state);
                    foreach (var h in _afHandlers)
                        EditorGUILayout.LabelField((h.Wired ? "   [wired]  " : "   [  -  ]  ") + h.RelPath);
                }

                EditorGUILayout.HelpBox(
                    "Wire injects 3 PlayerPrefs lines at the START of onConversionDataSuccess so the SDK " +
                    "auto-reports 'mmp_install' (your existing handler code is left untouched). The block is " +
                    "delimited by marker comments — idempotent, and Unwire removes exactly it. Re-run after " +
                    "an AppsFlyer SDK upgrade overwrites the handler file.",
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Wire AppsFlyer Handler", GUILayout.Height(24)))
                    {
                        int n = AppsFlyerConversionWirer.WireAll(out _afLog);
                        RefreshAfHandlers();
                        Debug.Log("[GameLyft] AppsFlyer wiring — " + n + " handler(s) newly wired.\n" + _afLog);
                    }
                    using (new EditorGUI.DisabledScope(wired == 0))
                    {
                        if (GUILayout.Button("Unwire", GUILayout.Width(80), GUILayout.Height(24)))
                        {
                            int n = AppsFlyerConversionWirer.UnwireAll(out _afLog);
                            RefreshAfHandlers();
                            Debug.Log("[GameLyft] AppsFlyer unwiring — " + n + " handler(s) unwired.\n" + _afLog);
                        }
                    }
                    if (GUILayout.Button("Re-scan", GUILayout.Width(80), GUILayout.Height(24)))
                        RefreshAfHandlers();
                }
            }
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
            _s.verboseLogging = _verboseLogging;

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
