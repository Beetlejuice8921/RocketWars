#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Dev
{
    /// <summary>
    /// Таймлайн дубля для ролика (реальное время): действие через N секунд или растянутое на отрезок (u = 0..1).
    /// Весь дубль задаётся одним вызовом из UnityMCP — нажатия, повороты взгляда, камера, запись.
    /// </summary>
    public class TrailerDirector : MonoBehaviour
    {
        struct Cue
        {
            public float Start, Duration;
            public Action<float> Act;
            public bool Started, Done;
        }

        static TrailerDirector _instance;
        readonly List<Cue> _cues = new List<Cue>();
        float _t0;

        static TrailerDirector I
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("TrailerDirector");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<TrailerDirector>();
                }
                return _instance;
            }
        }

        /// <summary>Начать новый таймлайн (старые реплики сбрасываются), отсчёт — с этого момента.</summary>
        public static void Reset()
        {
            I._cues.Clear();
            I._t0 = Time.realtimeSinceStartup;
        }

        public static float Now => Time.realtimeSinceStartup - I._t0;

        public static void At(float time, Action act) => I._cues.Add(new Cue { Start = time, Duration = 0f, Act = _ => act() });

        public static void Span(float start, float duration, Action<float> act) => I._cues.Add(new Cue { Start = start, Duration = duration, Act = act });

        public static int Remaining
        {
            get
            {
                int n = 0;
                foreach (var c in I._cues) if (!c.Done) n++;
                return n;
            }
        }

        void Update()
        {
            float t = Time.realtimeSinceStartup - _t0;
            for (int i = 0; i < _cues.Count; i++)
            {
                var c = _cues[i];
                if (c.Done || t < c.Start)
                    continue;
                float u = c.Duration <= 0f ? 1f : Mathf.Clamp01((t - c.Start) / c.Duration);
                try { c.Act(u); }
                catch (Exception e) { Debug.LogException(e); u = 1f; }
                c.Started = true;
                c.Done = u >= 1f;
                _cues[i] = c;
            }
        }
    }
}
#endif
