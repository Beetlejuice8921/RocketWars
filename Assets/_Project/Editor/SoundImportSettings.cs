using UnityEditor;
using UnityEngine;

namespace RocketWars.EditorTools
{
    /// <summary>
    /// Настройки импорта звуков из Assets/_Project/Resources/Sounds — ставятся один раз, при первом импорте файла
    /// (потом их можно менять в инспекторе, а замена файла с тем же именем сохраняет .meta и настройки):
    /// • эффекты — моно, распаковка при загрузке (без задержки при первом проигрывании), PCM;
    /// • фоновые лупы (Ambient*) — стриминг с диска, Vorbis: длинные, в памяти держать незачем;
    /// • джинглы конца матча (Win, Lose) — стерео, Vorbis, распаковка при загрузке.
    /// </summary>
    public class SoundImportSettings : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Resources/Sounds/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder) || !assetImporter.importSettingsMissing)
                return;

            var importer = (AudioImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool ambient = name.StartsWith("Ambient");
            bool jingle = name == "Win" || name == "Lose"; // музыка конца матча: стерео, сжата, но готова сразу
            importer.forceToMono = !ambient && !jingle;
            importer.loadInBackground = ambient;

            var settings = importer.defaultSampleSettings;
            settings.loadType = ambient ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = ambient || jingle ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = 0.7f;
            settings.preloadAudioData = !ambient;
            importer.defaultSampleSettings = settings;
        }
    }
}
