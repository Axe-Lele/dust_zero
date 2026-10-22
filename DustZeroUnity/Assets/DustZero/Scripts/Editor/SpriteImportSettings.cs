// Import rules for the sprites extracted from the original canvas code.
//
// Doing this in a postprocessor rather than by hand means re-exporting the art (or pulling
// the repo fresh) reproduces identical import settings with no manual inspector work.
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    public sealed class SpriteImportSettings : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/DustZero/Art/Sprites/";

        /// <summary>
        /// 256 px captures. One image does NOT cover one tile - the capture box is wider than
        /// the art's footprint, and by a different factor for units than for enemies - so
        /// BoardView scales the body by the factor the exporter implies. Keeping PPU at the
        /// image size means that factor is the only number involved.
        /// </summary>
        const float PixelsPerUnit = 256f;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SpriteFolder)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;

            // Centre pivot. A guessed "feet" pivot cannot be right for both units and enemies,
            // and it hides the real offset: where the original's draw origin sits inside the
            // capture box is a property of the exporter, so BoardView applies it explicitly.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
        }
    }
}
