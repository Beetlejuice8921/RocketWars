using System.IO;
using System.Linq;
using UnityEditor.Media;
using UnityEngine;

namespace RocketWars.EditorTools.Trailer
{
    /// <summary>
    /// Склейка готовой последовательности JPG (уже с подписями, ровно fps кадров в секунду) в MP4 H.264
    /// встроенным кодировщиком Unity — без ffmpeg.
    /// </summary>
    public static class TrailerEncoder
    {
        public static string Encode(string framesFolder, string outPath, int fps = 30)
        {
            var files = Directory.GetFiles(framesFolder, "*.jpg").OrderBy(f => f).ToArray();
            if (files.Length == 0)
                return "no frames";
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(files[0]));
            // JPG грузится как RGB24, кодировщику нужен RGBA32
            var frame = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            var attrs = new VideoTrackAttributes
            {
                frameRate = new MediaRational(fps),
                width = (uint)tex.width,
                height = (uint)tex.height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High
            };
            if (File.Exists(outPath))
                File.Delete(outPath);
            using (var encoder = new MediaEncoder(outPath, attrs))
            {
                foreach (var file in files)
                {
                    tex.LoadImage(File.ReadAllBytes(file));
                    frame.SetPixels32(tex.GetPixels32());
                    frame.Apply(false);
                    encoder.AddFrame(frame);
                }
            }
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(frame);
            return $"{files.Length} frames → {outPath} ({new FileInfo(outPath).Length / (1024f * 1024f):0.0} MB)";
        }
    }
}
