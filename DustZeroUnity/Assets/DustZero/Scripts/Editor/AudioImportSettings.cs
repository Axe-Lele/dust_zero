// Import rules for the three music tracks, applied in a postprocessor for the same reason
// the sprites use one: a fresh clone reproduces the settings without inspector work.
using UnityEditor;

namespace DustZero.Editor
{
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        const string BgmFolder = "Assets/DustZero/Audio/BGM/";

        /// <summary>
        /// Music stays compressed in memory rather than decompressed on load. The three tracks
        /// are 29s, 71s and 150s of 44.1kHz mono, which is about 40MB of PCM if decoded up
        /// front, against roughly 4MB left as Vorbis. Streaming would be cheaper still, but
        /// AudioDirector rewinds the playhead on every loop and a seek into a streamed clip
        /// can audibly stall - this keeps the whole file resident so the wrap is instant.
        /// </summary>
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(BgmFolder)) return;

            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = UnityEngine.AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;

            importer.forceToMono = false;
            importer.loadInBackground = false;
        }
    }
}
