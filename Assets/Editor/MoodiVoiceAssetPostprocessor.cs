using UnityEditor;

/// <summary>
/// Import settings for Assets/Resources/MoodiumVoice/ (Moodi's voice lines).
/// Short mono clips: decompressed on load so they start instantly, Vorbis in the build to keep it small.
/// Re-import the folder after copying the WAVs in.
/// </summary>
public sealed class MoodiVoiceAssetPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MoodiumVoice/";

    void OnPreprocessAudio()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder))
            return;
        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = true;
        importer.loadInBackground = false;
        var settings = importer.defaultSampleSettings;
        settings.loadType = UnityEngine.AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
        settings.quality = .7f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
    }
}
