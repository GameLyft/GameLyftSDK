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

            foreach (var target in Targets)
            {
                string current = PlayerSettings.GetScriptingDefineSymbols(target);
                var symbols = new List<string>(current.Split(';'));
                symbols.RemoveAll(string.IsNullOrWhiteSpace);

                bool changed = false;
                bool present = symbols.Contains(symbol);

                if (enabled && !present)
                {
                    symbols.Add(symbol);
                    changed = true;
                }
                else if (!enabled && present)
                {
                    symbols.Remove(symbol);
                    changed = true;
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
