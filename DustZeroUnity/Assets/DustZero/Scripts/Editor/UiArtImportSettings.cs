// Import rules for the 10.16-n art converted out of the web build by tools/convert_art.py.
//
// Two groups with different needs: icons are small, must stay crisp, and carry alpha;
// the unit illustrations are large opaque plates shown only in the art viewer, where
// compression is invisible but the memory saving is not.
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    public sealed class UiArtImportSettings : AssetPostprocessor
    {
        const string IconFolder = "Assets/DustZero/Art/Icon/";
        const string IllustFolder = "Assets/DustZero/Art/Illust/";

        void OnPreprocessTexture()
        {
            bool icon = assetPath.StartsWith(IconFolder);
            bool illust = assetPath.StartsWith(IllustFolder);
            if (!icon && !illust) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;

            if (icon)
            {
                // Source icons are 96x96. Keeping PPU at the image size means a sprite is
                // one unit, and leaving them uncompressed avoids block artefacts on the
                // flat colours and hard edges these use - 43 icons is about 1.5MB.
                importer.spritePixelsPerUnit = 96f;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 128;
            }
            else
            {
                // Full plates run to 804x1080, so 1024 keeps them whole without rounding up
                // to 2048. These are painted illustrations; compression reads as clean here.
                importer.spritePixelsPerUnit = 100f;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = 1024;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
        }
    }
}
