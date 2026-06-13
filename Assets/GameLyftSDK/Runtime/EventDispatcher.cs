using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Analytics;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Internal persistent Firebase event queue. Buffers events to PlayerPrefs until
    /// Firebase Analytics is ready, then drains with retry/backoff. Survives app
    /// pause/quit. Not part of the public API surface.
    /// </summary>
    internal class EventDispatcher : MonoBehaviour
    {
        private const string PLAYER_PREFS_KEY = "GLSdk_FbQ";
        private const float RETRY_DELAY = 1f;
        private const float PROCESS_DELAY = 0.1f;

        [Serializable]
        internal class QueuedParameter
        {
            public string key;
            public string value;
            public string type;

            public QueuedParameter() { }

            public QueuedParameter(string key, string value, string type)
            {
                this.key = key;
                this.value = value;
                this.type = type;
            }
        }

        [Serializable]
        private class QueuedFirebaseEvent
        {
            public string eventName;
            public List<QueuedParameter> parameters;
            public string timestamp;

            public QueuedFirebaseEvent() { }

            public QueuedFirebaseEvent(string eventName, List<QueuedParameter> parameters)
            {
                this.eventName = eventName;
                this.parameters = parameters ?? new List<QueuedParameter>();
                this.timestamp = DateTime.UtcNow.ToString("o");
            }
        }

        [Serializable]
        private class EventQueue
        {
            public List<QueuedFirebaseEvent> events = new List<QueuedFirebaseEvent>();
        }

        private EventQueue eventQueue = new EventQueue();
        private bool isProcessing = false;
        private static EventDispatcher _instance;

        internal static EventDispatcher Instance => _instance;

        internal static EventDispatcher CreateAndStart()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("[GameLyft]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<EventDispatcher>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            LoadQueueFromPlayerPrefs();
        }

        private void Start()
        {
            StartCoroutine(ProcessQueueCoroutine());
        }

        private void OnApplicationQuit()
        {
            SaveQueueToPlayerPrefs();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) SaveQueueToPlayerPrefs();
        }

        internal void LogEvent(string eventName, List<QueuedParameter> parameters)
        {
            var queuedEvent = new QueuedFirebaseEvent(eventName, parameters);
            eventQueue.events.Add(queuedEvent);
            SaveQueueToPlayerPrefs();
        }

        internal static QueuedParameter StringParam(string key, string value)
        {
            return new QueuedParameter(key, value ?? "", "string");
        }

        internal static QueuedParameter LongParam(string key, long value)
        {
            return new QueuedParameter(key, value.ToString(), "long");
        }

        internal static QueuedParameter DoubleParam(string key, double value)
        {
            return new QueuedParameter(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture), "double");
        }

        internal int GetQueueSize() => eventQueue.events.Count;

        private IEnumerator ProcessQueueCoroutine()
        {
            // Wait for the consumer to finish their Firebase init and call Initialize()
            yield return new WaitUntil(() => GameLyftAnalytics.IsInitialized);

            while (true)
            {
                if (eventQueue.events.Count > 0 && !isProcessing)
                    yield return StartCoroutine(SendNextEvent());
                else
                    yield return new WaitForSeconds(PROCESS_DELAY);
            }
        }

        private IEnumerator SendNextEvent()
        {
            if (eventQueue.events.Count == 0) yield break;

            isProcessing = true;
            var queuedEvent = eventQueue.events[0];

            bool success = false;
            int retryCount = 0;

            while (!success)
            {
                if (!GameLyftAnalytics.IsInitialized)
                    yield return new WaitUntil(() => GameLyftAnalytics.IsInitialized);

                Exception sendException = null;
                try
                {
                    Parameter[] firebaseParams = ConvertToFirebaseParameters(queuedEvent.parameters);
                    FirebaseAnalytics.LogEvent(queuedEvent.eventName, firebaseParams);
                    success = true;
                }
                catch (Exception e)
                {
                    sendException = e;
                }

                if (sendException != null)
                {
                    retryCount++;
                    yield return new WaitForSeconds(RETRY_DELAY * retryCount);
                }
            }

            eventQueue.events.RemoveAt(0);
            SaveQueueToPlayerPrefs();
            isProcessing = false;
        }

        // Stamped on EVERY event at flush time — gl_purchase, gl_ad_impression,
        // mmp_install, diagnostics, consumer events, all of them — so the whole
        // SDK's Firebase output is identifiable by a single parameter.
        private const string EVENT_TYPE_VALUE = "gl_analytics";

        private Parameter[] ConvertToFirebaseParameters(List<QueuedParameter> queuedParams)
        {
            var result = new List<Parameter>();
            if (queuedParams == null)
            {
                result.Add(new Parameter("event_type", EVENT_TYPE_VALUE));
                result.Add(new Parameter("session", GameLyftAnalytics.SessionCount));
                return result.ToArray();
            }

            foreach (var qp in queuedParams)
            {
                if (string.IsNullOrEmpty(qp.key)) continue;
                // Skip any 'session' from older persisted events; we re-inject the live value below
                // so events queued pre-Initialize() (or carried over from a prior run) get the
                // correct current session number rather than a stale 0.
                if (qp.key == "session") continue;
                // Same for 'event_type': injected uniformly below. Also rewrites events
                // persisted by older SDK versions (which queued "progression_analytics").
                if (qp.key == "event_type") continue;

                switch (qp.type)
                {
                    case "long":
                        if (long.TryParse(qp.value, out long longVal))
                            result.Add(new Parameter(qp.key, longVal));
                        break;
                    case "double":
                        if (double.TryParse(qp.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double doubleVal))
                            result.Add(new Parameter(qp.key, doubleVal));
                        break;
                    case "string":
                    default:
                        result.Add(new Parameter(qp.key, qp.value ?? ""));
                        break;
                }
            }

            // Inject event_type + live session count at flush time. SessionCount is
            // reliable here because ProcessQueueCoroutine waits on IsInitialized, and
            // Initialize() sets SessionCount before flipping that flag.
            result.Add(new Parameter("event_type", EVENT_TYPE_VALUE));
            result.Add(new Parameter("session", GameLyftAnalytics.SessionCount));

            return result.ToArray();
        }

        private void SaveQueueToPlayerPrefs()
        {
            try
            {
                string json = JsonUtility.ToJson(eventQueue);
                PlayerPrefs.SetString(PLAYER_PREFS_KEY, json);
                PlayerPrefs.Save();
            }
            catch { }
        }

        private void LoadQueueFromPlayerPrefs()
        {
            try
            {
                if (PlayerPrefs.HasKey(PLAYER_PREFS_KEY))
                {
                    string json = PlayerPrefs.GetString(PLAYER_PREFS_KEY);
                    if (!string.IsNullOrEmpty(json))
                    {
                        eventQueue = JsonUtility.FromJson<EventQueue>(json);
                        if (eventQueue == null) eventQueue = new EventQueue();
                    }
                }
            }
            catch
            {
                eventQueue = new EventQueue();
            }
        }
    }
}
