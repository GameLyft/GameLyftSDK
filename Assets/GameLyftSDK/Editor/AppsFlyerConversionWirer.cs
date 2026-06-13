using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameLyft.Sdk.EditorTools
{
    /// <summary>
    /// One-click wiring of the AppsFlyer conversion handler to the GameLyft MMP bridge.
    ///
    /// Injects 3 PlayerPrefs lines at the START of every onConversionDataSuccess(string)
    /// method in the project (so any existing handler code is left untouched). The SDK's
    /// AppsFlyerMmp then polls those PlayerPrefs keys across the asmdef boundary and fires
    /// the one-shot 'mmp_install'. AppsFlyer delivers conversion data only to the GameObject
    /// registered through initSDK() — which lives in the AppsFlyer SDK's own assembly that
    /// cannot reference this SDK — so PlayerPrefs is the bridge.
    ///
    /// The injected block is delimited by BEGIN/END marker comments, which makes the
    /// operation idempotent (re-running skips already-wired handlers) and reversible
    /// (Unwire removes exactly the injected block). It edits source files only and never
    /// deletes user code.
    ///
    /// Driven from Tools -> GameLyft -> Settings (the "Wire AppsFlyer Handler" button under
    /// the AppsFlyer MMP toggle). Re-run after an AppsFlyer SDK upgrade overwrites the
    /// handler file.
    /// </summary>
    internal static class AppsFlyerConversionWirer
    {
        // Must match GameLyft.Sdk.AppsFlyerMmp.CONVERSION_DATA_KEY / CONVERSION_SET_KEY.
        public const string DATA_KEY = "AppsflyerGameLyftConversionData";
        public const string SET_KEY  = "isAppsflyerGameLyftConversionSet";

        private const string BEGIN = "// GAMELYFT_APPSFLYER_BRIDGE_BEGIN";
        private const string END   = "// GAMELYFT_APPSFLYER_BRIDGE_END";

        // Method signature through its opening brace. group "p" = the string parameter name.
        // Matches with or without access modifiers; requires a body "{" so the interface
        // declaration (which ends in ";") is never matched.
        private static readonly Regex MethodRx = new Regex(
            @"\bvoid\s+onConversionDataSuccess\s*\(\s*string\s+(?<p>\w+)\s*\)\s*\{",
            RegexOptions.Compiled);

        // The injected block (with its leading newline + indent) for removal on Unwire.
        private static readonly Regex BlockRx = new Regex(
            @"\r?\n[ \t]*" + Regex.Escape(BEGIN) + @".*?" + Regex.Escape(END),
            RegexOptions.Compiled | RegexOptions.Singleline);

        public struct Handler
        {
            public string RelPath;   // Assets/...
            public bool Wired;
        }

        /// <summary>
        /// Every .cs in the project with an onConversionDataSuccess(string) body, excluding
        /// the GameLyft SDK package itself (its only occurrence is in a doc comment).
        /// </summary>
        public static List<Handler> FindHandlers()
        {
            var result = new List<Handler>();
            string assetsAbs = Application.dataPath.Replace('\\', '/');
            string[] all;
            try { all = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories); }
            catch { return result; }

            foreach (var abs in all)
            {
                string norm = abs.Replace('\\', '/');
                string rel = "Assets" + norm.Substring(assetsAbs.Length);
                if (rel.Contains("/GameLyftSDK/")) continue;   // the SDK package has no real handler

                string src;
                try { src = File.ReadAllText(abs); } catch { continue; }
                if (!MethodRx.IsMatch(src)) continue;

                result.Add(new Handler { RelPath = rel, Wired = IsWired(src) });
            }
            return result;
        }

        /// <summary>Inject the bridge into every un-wired handler. Returns count newly wired.</summary>
        public static int WireAll(out string log)
        {
            var sb = new StringBuilder();
            int wired = 0;
            foreach (var h in FindHandlers())
            {
                string abs = ToAbs(h.RelPath);
                string src;
                try { src = File.ReadAllText(abs); } catch { continue; }

                if (IsWired(src)) { sb.AppendLine("  already wired:  " + h.RelPath); continue; }

                // Inject right after the FIRST handler method's opening brace.
                string after = MethodRx.Replace(src, m => m.Value + BuildBlock(m.Groups["p"].Value), 1);
                if (after == src) { sb.AppendLine("  no method match: " + h.RelPath); continue; }

                try { File.WriteAllText(abs, after); wired++; sb.AppendLine("  WIRED:          " + h.RelPath); }
                catch (System.Exception e) { sb.AppendLine("  FAILED:         " + h.RelPath + " - " + e.Message); }
            }
            if (wired > 0) AssetDatabase.Refresh();
            log = sb.Length == 0
                ? "No AppsFlyer conversion handler (onConversionDataSuccess) found in the project."
                : sb.ToString();
            return wired;
        }

        /// <summary>Remove the injected bridge block from every handler. Returns count unwired.</summary>
        public static int UnwireAll(out string log)
        {
            var sb = new StringBuilder();
            int unwired = 0;
            foreach (var h in FindHandlers())
            {
                string abs = ToAbs(h.RelPath);
                string src;
                try { src = File.ReadAllText(abs); } catch { continue; }
                if (!src.Contains(BEGIN)) continue;   // only marker-delimited blocks are removable

                string after = BlockRx.Replace(src, "");
                if (after == src) continue;

                try { File.WriteAllText(abs, after); unwired++; sb.AppendLine("  unwired: " + h.RelPath); }
                catch (System.Exception e) { sb.AppendLine("  FAILED:  " + h.RelPath + " - " + e.Message); }
            }
            if (unwired > 0) AssetDatabase.Refresh();
            log = sb.Length == 0 ? "Nothing to unwire (no marker-delimited bridge blocks found)." : sb.ToString();
            return unwired;
        }

        // Wired = our marker block OR the keys present some other way (e.g. a manual edit) —
        // either way we won't double-inject.
        private static bool IsWired(string src) => src.Contains(BEGIN) || src.Contains(SET_KEY);

        private static string BuildBlock(string param)
        {
            // Leading newline drops the block onto its own lines right after the method's "{".
            // Fully-qualified UnityEngine.PlayerPrefs so it compiles without a using directive.
            return
                "\n        " + BEGIN + "   (auto-generated by Tools -> GameLyft -> Settings; do not edit this block)" +
                "\n        // Stashes AppsFlyer's conversion payload in PlayerPrefs so the GameLyft SDK's AppsFlyerMmp" +
                "\n        // can poll it across the asmdef boundary and fire the one-shot 'mmp_install'." +
                "\n        UnityEngine.PlayerPrefs.SetString(\"" + DATA_KEY + "\", " + param + ");" +
                "\n        UnityEngine.PlayerPrefs.SetInt(\"" + SET_KEY + "\", 1);" +
                "\n        UnityEngine.PlayerPrefs.Save();" +
                "\n        " + END;
        }

        private static string ToAbs(string rel)
        {
            // Application.dataPath = <project>/Assets ; rel = "Assets/..."
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, rel).Replace('\\', '/');
        }
    }
}
