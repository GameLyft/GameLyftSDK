using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameLyft.Sdk.EditorTools
{
    [CustomEditor(typeof(GameLyftSettings))]
    internal class GameLyftSettingsEditor : Editor
    {
        // define, label, a type that exists only when that SDK is in the project
        private static readonly (string define, string label, string probeType)[] Destinations =
        {
            ("GAMELYFT_FIREBASE",     "Firebase",     "Firebase.Analytics.FirebaseAnalytics, Firebase.Analytics"),
            ("GAMELYFT_APPSFLYER",    "AppsFlyer",    "AppsFlyerSDK.AppsFlyer, AppsFlyer"),
            ("GAMELYFT_ADJUST",       "Adjust",       "AdjustSdk.Adjust, AdjustSdk.Scripts"),
            ("GAMELYFT_SOLAR_ENGINE", "Solar Engine", "SolarEngine.Analytics, SolarEngineSDK"),
            ("GAMELYFT_SINGULAR",     "Singular",     "Singular.SingularSDK, SingularSDK"),
            ("GAMELYFT_AIRBRIDGE",    "Airbridge",    "Airbridge, AirbridgeSDK"),
        };
        private static readonly (string define, string label, string probeType)[] Mediations =
        {
            ("GAMELYFT_ADMOB",    "AdMob",        "GoogleMobileAds.Api.MobileAds, GoogleMobileAds"),
            ("GAMELYFT_APPLOVIN", "AppLovin MAX", "MaxSdk, MaxSdk.Scripts"),
        };
        // Defines of integrations from earlier SDK versions that no longer exist: always removed.
        private static readonly string[] RetiredDefines = { "GAMELYFT_TENJIN" };

        private GameLyftSettings _s;
        private bool[] _dest = new bool[6];
        private bool[] _med = new bool[2];
        private bool _milestones, _testMode, _verbose;
        private List<int> _levels = new List<int>();
        private Dictionary<string, string> _tokens = new Dictionary<string, string>();

        private void OnEnable()
        {
            _s = (GameLyftSettings)target;
            DefineSymbolManager.SetDefines(DesiredDefines(_s));
            Load();
        }

        [InitializeOnLoadMethod]
        private static void ReconcileDefinesOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                var settings = LoadOrCreate();
                if (settings != null) DefineSymbolManager.SetDefines(DesiredDefines(settings));
            };
        }

        private static bool[] DestFlags(GameLyftSettings s) => new[]
            { s.sendToFirebase, s.sendToAppsFlyer, s.sendToAdjust, s.sendToSolarEngine, s.sendToSingular, s.sendToAirbridge };

        private static Dictionary<string, bool> DesiredDefines(GameLyftSettings s)
        {
            var d = new Dictionary<string, bool>();
            var f = DestFlags(s);
            // A define is only written when its SDK is present, so a stale tick can never break the build.
            for (int i = 0; i < Destinations.Length; i++) d[Destinations[i].define] = f[i] && SdkPresent(Destinations[i].probeType);
            d[Mediations[0].define] = s.useAdMobMediation && SdkPresent(Mediations[0].probeType);
            d[Mediations[1].define] = s.useAppLovinMax && SdkPresent(Mediations[1].probeType);
            foreach (var r in RetiredDefines) d[r] = false;
            return d;
        }

        private static bool SdkPresent(string probeType)
        {
            if (Type.GetType(probeType) != null) return true;
            string name = probeType.Split(',')[0].Trim();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (asm.GetType(name) != null) return true; } catch { }
            }
            return false;
        }

        private void Load()
        {
            _dest = DestFlags(_s);
            _med = new[] { _s.useAdMobMediation, _s.useAppLovinMax };
            _milestones = _s.sendLevelMilestones;
            _levels = new List<int>(_s.levelMilestones ?? new List<int>());
            _tokens = new Dictionary<string, string>();
            foreach (var t in _s.adjustTokens ?? new List<GameLyftSettings.AdjustToken>())
                if (t != null && !string.IsNullOrEmpty(t.eventName)) _tokens[t.eventName] = t.token ?? "";
            _testMode = _s.testMode;
            _verbose = _s.verboseLogging;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("GameLyft SDK Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Changes are staged: press Apply to save them and update the scripting defines (one recompile).", MessageType.Info);

            Section("Destinations", "Every event is sent to each ticked platform through that platform's own SDK. "
                + "Your game initializes those SDKs as usual; events wait on disk until each one has started.");
            for (int i = 0; i < Destinations.Length; i++) _dest[i] = SdkToggle(Destinations[i].label, _dest[i], Destinations[i].probeType, Destinations[i].define);

            Section("Ad mediation", "Enables AdRevenue.Report(...) for that mediation (fires gl_ad_impression). "
                + "AppLovin MAX full-screen ad time is tracked automatically; with AdMob, call "
                + "GameLyftAnalytics.AdStarted() / AdClosed() from the ad's open / close callbacks.");
            for (int i = 0; i < Mediations.Length; i++) _med[i] = SdkToggle(Mediations[i].label, _med[i], Mediations[i].probeType, Mediations[i].define);

            Section("Level milestones", "Also fire gl_level_<N>_completed once per install when one of these levels is completed.");
            _milestones = EditorGUILayout.ToggleLeft("Send level milestones", _milestones);
            if (_milestones) DrawMilestones();

            if (_dest[2]) DrawAdjustTokens();

            Section("Debug", "Turn both OFF for production.");
            _testMode = EditorGUILayout.ToggleLeft("Test Mode (on-screen warnings)", _testMode);
            _verbose = EditorGUILayout.ToggleLeft("Verbose Logging", _verbose);

            EditorGUILayout.Space();
            bool pending = HasPending();
            if (pending) EditorGUILayout.HelpBox("Unapplied changes.", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!pending))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(28))) Apply();
                if (GUILayout.Button("Revert", GUILayout.Height(28), GUILayout.Width(90))) { Load(); GUI.FocusControl(null); }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Add GameLyft prefab to the open scene")) GameLyftPrefabMenu.AddToScene();
        }

        private static void Section(string title, string help)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(help, MessageType.None);
        }

        private static bool SdkToggle(string label, bool staged, string probeType, string define)
        {
            bool present = SdkPresent(probeType);
            using (new EditorGUI.DisabledScope(!present && !staged))
            {
                string suffix = !present ? "   (SDK not found in project)"
                    : (staged != DefineSymbolManager.HasDefine(define) ? "   (pending Apply)" : "");
                return EditorGUILayout.ToggleLeft(label + suffix, staged);
            }
        }

        private void DrawMilestones()
        {
            for (int i = 0; i < _levels.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _levels[i] = Mathf.Max(1, EditorGUILayout.IntField("Level", _levels[i]));
                    if (GUILayout.Button("×", GUILayout.Width(24))) { _levels.RemoveAt(i); GUIUtility.ExitGUI(); }
                }
            }
            using (new EditorGUI.DisabledScope(_levels.Count >= GameLyftSettings.MAX_LEVEL_MILESTONES))
            {
                if (GUILayout.Button("Add milestone (" + _levels.Count + "/" + GameLyftSettings.MAX_LEVEL_MILESTONES + ")"))
                    _levels.Add(_levels.Count > 0 ? _levels[_levels.Count - 1] + 5 : 5);
            }
        }

        private List<string> AdjustRows()
        {
            var rows = new List<string>(GameLyftSettings.SdkEvents);
            if (_milestones) foreach (int l in _levels) rows.Add("gl_level_" + l + "_completed");
            return rows;
        }

        private void DrawAdjustTokens()
        {
            Section("Adjust event tokens", "Adjust only accepts events created in its dashboard. Enter the token of each SDK event "
                + "you want in Adjust (leave empty to skip it). For your own events pass the token to "
                + "GameLyftAnalytics.TrackEvent(name, params, adjustToken).");
            foreach (var ev in AdjustRows())
            {
                _tokens.TryGetValue(ev, out var cur);
                _tokens[ev] = EditorGUILayout.TextField(ev, cur ?? "").Trim();
            }
        }

        private bool HasPending()
        {
            var f = DestFlags(_s);
            for (int i = 0; i < f.Length; i++) if (f[i] != _dest[i]) return true;
            if (_s.useAdMobMediation != _med[0] || _s.useAppLovinMax != _med[1]) return true;
            if (_s.sendLevelMilestones != _milestones || _s.testMode != _testMode || _s.verboseLogging != _verbose) return true;
            var ls = _s.levelMilestones ?? new List<int>();
            if (ls.Count != _levels.Count) return true;
            for (int i = 0; i < ls.Count; i++) if (ls[i] != _levels[i]) return true;
            var saved = new Dictionary<string, string>();
            foreach (var t in _s.adjustTokens ?? new List<GameLyftSettings.AdjustToken>())
                if (t != null && !string.IsNullOrEmpty(t.eventName)) saved[t.eventName] = t.token ?? "";
            foreach (var kv in _tokens)
            {
                saved.TryGetValue(kv.Key, out var v);
                if ((v ?? "") != (kv.Value ?? "")) return true;
            }
            return false;
        }

        private void Apply()
        {
            Undo.RecordObject(_s, "GameLyft Settings");
            _s.sendToFirebase = _dest[0]; _s.sendToAppsFlyer = _dest[1]; _s.sendToAdjust = _dest[2];
            _s.sendToSolarEngine = _dest[3]; _s.sendToSingular = _dest[4]; _s.sendToAirbridge = _dest[5];
            _s.useAdMobMediation = _med[0]; _s.useAppLovinMax = _med[1];
            _s.sendLevelMilestones = _milestones;
            var distinct = new List<int>();
            foreach (int l in _levels) if (!distinct.Contains(l) && distinct.Count < GameLyftSettings.MAX_LEVEL_MILESTONES) distinct.Add(l);
            _s.levelMilestones = distinct;
            _levels = new List<int>(distinct);
            _s.adjustTokens = new List<GameLyftSettings.AdjustToken>();
            foreach (var kv in _tokens)
                if (!string.IsNullOrEmpty(kv.Value)) _s.adjustTokens.Add(new GameLyftSettings.AdjustToken { eventName = kv.Key, token = kv.Value });
            _s.testMode = _testMode;
            _s.verboseLogging = _verbose;
            EditorUtility.SetDirty(_s);
            AssetDatabase.SaveAssets();
            DefineSymbolManager.SetDefines(DesiredDefines(_s));
            GUI.FocusControl(null);
            Debug.Log("[GameLyft] Settings applied.");
        }

        [MenuItem("Tools/GameLyft/Settings", priority = 100)]
        public static void OpenOrCreateSettings()
        {
            var settings = LoadOrCreate();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        internal static GameLyftSettings LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameLyftSettings>(GameLyftSettings.DEFAULT_ASSET_PATH);
            if (existing != null) return existing;
            string dir = Path.GetDirectoryName(GameLyftSettings.DEFAULT_ASSET_PATH);
            if (!AssetDatabase.IsValidFolder(dir)) { Directory.CreateDirectory(dir); AssetDatabase.Refresh(); }
            var asset = CreateInstance<GameLyftSettings>();
            AssetDatabase.CreateAsset(asset, GameLyftSettings.DEFAULT_ASSET_PATH);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
