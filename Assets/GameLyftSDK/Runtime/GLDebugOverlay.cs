using System.Collections.Generic;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Internal on-screen warning panel shown only when testMode is enabled in
    /// GameLyftSettings. Stacks warnings vertically with individual close buttons.
    /// Pure IMGUI — zero scene / Canvas / EventSystem dependencies.
    /// </summary>
    internal class GLDebugOverlay : MonoBehaviour
    {
        private const int MAX_VISIBLE = 6;
        private const float PANEL_WIDTH = 520f;
        private const float ENTRY_MIN_HEIGHT = 52f;
        private const float MARGIN = 12f;

        private static GLDebugOverlay _instance;
        private readonly List<string> _messages = new List<string>();
        private Vector2 _scroll;

        private GUIStyle _titleStyle;
        private GUIStyle _messageStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _entryStyle;
        private GUIStyle _closeBtnStyle;
        private bool _stylesReady;

        internal static void Push(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (_instance == null)
            {
                var go = new GameObject("[GameLyft.Debug]");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideInHierarchy;
                _instance = go.AddComponent<GLDebugOverlay>();
            }

            _instance._messages.Add(message);

            // Cap so a runaway stream of warnings can't memory-leak.
            while (_instance._messages.Count > 100)
                _instance._messages.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (_messages.Count == 0) return;
            EnsureStyles();

            float panelW = Mathf.Min(PANEL_WIDTH, Screen.width - (MARGIN * 2));
            int visibleCount = Mathf.Min(_messages.Count, MAX_VISIBLE);
            float panelH = (ENTRY_MIN_HEIGHT * visibleCount) + 48f;

            Rect panelRect = new Rect(MARGIN, MARGIN, panelW, panelH);

            GUI.Box(panelRect, GUIContent.none, _panelStyle);

            GUILayout.BeginArea(new Rect(panelRect.x + 8, panelRect.y + 6,
                                          panelRect.width - 16, panelRect.height - 12));
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("GameLyft Integration Warnings (" + _messages.Count + ")", _titleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear All", _closeBtnStyle, GUILayout.Width(90), GUILayout.Height(26)))
                    _messages.Clear();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                _scroll = GUILayout.BeginScrollView(_scroll);
                int removeIndex = -1;
                for (int i = 0; i < _messages.Count; i++)
                {
                    GUILayout.BeginHorizontal(_entryStyle);
                    GUILayout.Label(_messages[i], _messageStyle, GUILayout.MinHeight(ENTRY_MIN_HEIGHT - 8));
                    if (GUILayout.Button("×", _closeBtnStyle, GUILayout.Width(30), GUILayout.Height(30)))
                        removeIndex = i;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
                GUILayout.EndScrollView();

                if (removeIndex >= 0) _messages.RemoveAt(removeIndex);
            }
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = Tex(new Color(0.12f, 0.12f, 0.14f, 0.95f));

            _entryStyle = new GUIStyle(GUI.skin.box);
            _entryStyle.normal.background = Tex(new Color(0.55f, 0.15f, 0.15f, 0.85f));
            _entryStyle.padding = new RectOffset(10, 6, 6, 6);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.fontSize = 14;
            _titleStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);

            _messageStyle = new GUIStyle(GUI.skin.label);
            _messageStyle.wordWrap = true;
            _messageStyle.fontSize = 12;
            _messageStyle.normal.textColor = Color.white;

            _closeBtnStyle = new GUIStyle(GUI.skin.button);
            _closeBtnStyle.fontStyle = FontStyle.Bold;

            _stylesReady = true;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
