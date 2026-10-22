// Named lookups into the supplied UI art pack.
//
// Everything the screens draw comes from here rather than from literal paths scattered
// through the builder, so a renamed file is one compile error instead of a silent blank. The
// pack's own folder names are kept: swapping in final art later means replacing files, not
// editing code, which is what its notes ask for.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DustZero.Editor
{
    public static class UiArt
    {
        static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>Loads one sprite from the pack, warning once if a file is missing.</summary>
        public static Sprite Get(string relativePath)
        {
            if (Cache.TryGetValue(relativePath, out var cached) && cached != null) return cached;
            var path = UiAssetImporter.Root + relativePath;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[DustZero] UI 에셋이 없습니다: {path}");
            Cache[relativePath] = sprite;
            return sprite;
        }

        public static void ResetCache() => Cache.Clear();

        // ------------------------------------------------------------------ surfaces

        /// <summary>Rounded content panel, 9-sliced with a 40px corner.</summary>
        public static Sprite Panel => Get("ui_common/panel_r40.png");
        /// <summary>The same panel with the design's single shadow band baked underneath.</summary>
        public static Sprite PanelShadow => Get("ui_common/panel_r40_shadow.png");
        /// <summary>Fully rounded capsule, for chips and currency pills.</summary>
        public static Sprite Pill => Get("ui_common/pill.png");
        public static Sprite PillOutline => Get("ui_common/pill_outline.png");
        /// <summary>Progress bar track and fill share this rounded sprite.</summary>
        public static Sprite Bar => Get("ui_common/bar_r12.png");
        public static Sprite BadgeDot => Get("ui_common/badge_dot.png");
        public static Sprite TabIndicator => Get("ui_common/tab_indicator.png");
        public static Sprite Dim => Get("ui_common/dim_overlay.png");

        // ------------------------------------------------------------------ buttons

        /// <summary>
        /// A button's four faces. The pack bakes the face and its solid under-band into one
        /// sprite, so pressing swaps the sprite instead of resizing a separate band.
        /// </summary>
        public readonly struct ButtonFaces
        {
            public readonly Sprite Normal, Pressed, Disabled, Selected;
            public ButtonFaces(Sprite normal, Sprite pressed, Sprite disabled, Sprite selected)
            {
                Normal = normal; Pressed = pressed; Disabled = disabled; Selected = selected;
            }
        }

        public static ButtonFaces Faces(string kind) => new(
            Get($"ui_common/btn_{kind}.png"),
            Get($"ui_common/btn_{kind}_pressed.png"),
            Get("ui_common/btn_disabled.png"),
            Get("ui_common/btn_selected.png"));

        // ------------------------------------------------------------------ icons

        public static Sprite Icon(string name) => Get($"icons/ic_{name}.png");

        public static Sprite Currency(string name) => Get($"currency/{name}.png");

        // ------------------------------------------------------------------ battle

        public static Sprite Battle(string name) => Get($"battle/{name}.png");

        public static Sprite Collection(string name) => Get($"collection/{name}.png");

        /// <summary>Placeholder room art; replaced by swapping the file, not the code.</summary>
        public static Sprite RoomBackground(string floor) => Get($"backgrounds_temp/bg_room_{floor}.png");
    }
}
