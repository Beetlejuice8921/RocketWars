#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RocketWars.Dev
{
    /// <summary>
    /// Запись ролика в редакторе (Play mode): каждый кадр экрана вместе с интерфейсом → JPG + отметка реального времени.
    /// Игра идёт в реальном времени (сеть и ракеты живут по серверным часам), поэтому кадры потом пересэмплируются
    /// по отметкам в ровные 30 к/с (Tools/trailer). Снимок — асинхронно с GPU, JPG кодируется в пуле потоков.
    /// </summary>
    public class TrailerCapture : MonoBehaviour
    {
        public const string Root = "D:/ClaudeRocketWars/Trailer/shots";

        static TrailerCapture _instance;

        string _folder;
        int _index, _pending;
        float _startTime, _maxFps;
        RenderTexture _rt;
        StreamWriter _times;
        readonly object _lock = new object();

        public static bool IsRecording => _instance != null && _instance._folder != null;
        public static int Frames => _instance != null ? _instance._index : 0;
        public static int Pending => _instance != null ? _instance._pending : 0;

        /// <summary>Начать дубль: кадры в Trailer/shots/&lt;name&gt;/ (папка очищается).</summary>
        public static void Begin(string name, float maxFps = 60f)
        {
            if (_instance == null)
            {
                var go = new GameObject("TrailerCapture");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<TrailerCapture>();
            }
            _instance.StartShot(name, maxFps);
        }

        public static void End()
        {
            if (_instance != null)
                _instance.StopShot();
        }

        void StartShot(string name, float maxFps)
        {
            StopShot();
            _folder = Path.Combine(Root, name);
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
            Directory.CreateDirectory(_folder);
            _times = new StreamWriter(Path.Combine(_folder, "times.txt"));
            _index = 0;
            _maxFps = maxFps;
            _startTime = Time.realtimeSinceStartup;
            StartCoroutine(Loop());
            Debug.Log($"[Trailer] recording '{name}' at {Screen.width}x{Screen.height}");
        }

        void StopShot()
        {
            if (_folder == null)
                return;
            StopAllCoroutines();
            AsyncGPUReadback.WaitAllRequests();
            // дождаться записи файлов
            for (int i = 0; i < 200 && _pending > 0; i++)
                Thread.Sleep(20);
            lock (_lock)
            {
                _times.Dispose();
                _times = null;
            }
            Debug.Log($"[Trailer] shot done: {_index} frames in {Time.realtimeSinceStartup - _startTime:0.0} s → {_folder}");
            _folder = null;
        }

        IEnumerator Loop()
        {
            var wait = new WaitForEndOfFrame();
            float last = -1f;
            while (true)
            {
                yield return wait;
                float now = Time.realtimeSinceStartup - _startTime;
                if (last >= 0f && now - last < 1f / _maxFps - 0.002f)
                    continue;
                last = now;
                Capture(now);
            }
        }

        void Capture(float time)
        {
            int w = Screen.width, h = Screen.height;
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
            int index = ++_index;
            string folder = _folder;
            Interlocked.Increment(ref _pending);
            AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError || folder == null)
                {
                    Interlocked.Decrement(ref _pending);
                    return;
                }
                var data = request.GetData<byte>().ToArray();
                lock (_lock)
                    _times?.WriteLine($"{index} {time:0.0000}");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var jpg = ImageConversion.EncodeArrayToJPG(data, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 92);
                        File.WriteAllBytes(Path.Combine(folder, $"f_{index:D5}.jpg"), jpg);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[Trailer] frame {index}: {e.Message}");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pending);
                    }
                });
            });
        }

        void OnDestroy()
        {
            StopShot();
            if (_rt != null) _rt.Release();
        }
    }
}
#endif
