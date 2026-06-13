using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace GameLyft.Sdk.EditorTools
{
    /// <summary>
    /// Adds/removes scripting define symbols across all build target groups.
    /// Used by the GameLyftSettings inspector to flip GAMELYFT_ADMOB / GAMELYFT_APPLOVIN
    /// in response to checkbox changes.
    /// </summary>
    internal static class DefineSymbolManager
    {
        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
            NamedBuildTarget.WebGL
        };

        internal static void SetDefine(string symbol, bool enabled)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            SetDefines(new Dictionary<string, bool> { { symbol, enabled } });
        }

        /// <summary>
        /// Batch variant: applies many symbol add/removes with at most ONE
        /// SetScriptingDefineSymbols write per build target, so toggling several
        /// integrations triggers a single recompile instead of one per symbol.
        /// </summary>
        internal static void SetDefines(IDictionary<string, bool> desired)
        {
            if (desired == null || desired.Count == 0) return;

            foreach (var target in Targets)
            {
                string current = PlayerSettings.GetScriptingDefineSymbols(target);
                var symbols = new List<string>(current.Split(';'));
                symbols.RemoveAll(string.IsNullOrWhiteSpace);

                bool changed = false;
                foreach (var kvp in desired)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;

                    bool present = symbols.Contains(kvp.Key);
                    if (kvp.Value && !present)
                    {
                        symbols.Add(kvp.Key);
                        changed = true;
                    }
                    else if (!kvp.Value && present)
                    {
                        symbols.Remove(kvp.Key);
                        changed = true;
                    }
                }

                if (changed)
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }
        }

        internal static bool HasDefine(string symbol)
        {
            string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            foreach (var s in current.Split(';'))
                if (s.Trim() == symbol) return true;
            return false;
        }
    }
}
