using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// One session = one app launch, however long it lasts (no inactivity timeout).
    ///   Id      8 random characters, new every launch — sent as gl_sid on every event.
    ///   Number  how many sessions this install has had (1, 2, 3, …) — sent on gl_engagement.
    /// Both are fixed before the first scene loads, so every event of the launch carries them.
    /// </summary>
    public static class GLSession
    {
        private const string NUMBER_KEY = "GLSdk_session";   // same key as earlier SDK versions: the count continues

        private static string _id;
        private static int _number;

        public static string Id
        {
            get { if (_id == null) _id = GLIds.New(8); return _id; }
        }

        public static int Number => _number;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Begin()
        {
            // Always new: with domain reload off in the editor, statics survive between Play runs.
            _id = GLIds.New(8);
            _number = PlayerPrefs.GetInt(NUMBER_KEY, 0) + 1;
            PlayerPrefs.SetInt(NUMBER_KEY, _number);
            PlayerPrefs.Save();
        }
    }
}
