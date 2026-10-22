using DustZero.Core.Defense;
using DustZero.Data;
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>
    /// 10.17-e's regen warning over one entrance: the tile's border pulses, and a bubble above
    /// it shows what is about to come out, how many, and how many seconds are left.
    ///
    /// Drawn in world space with the board rather than in the HUD, because it has to sit on a
    /// specific tile. Everything is sized in tile units so it tracks the board's framing.
    ///
    /// <example><code>
    /// warn.Bind(board, warning, db, sprites, cols, startX, startY, presentationTime);
    /// </code></example>
    /// </summary>
    public sealed class SpawnWarnView : MonoBehaviour
    {
        /// <summary>One enemy column inside the bubble.</summary>
        [System.Serializable]
        public sealed class Slot
        {
            public SpriteRenderer Icon;
            public TextMeshPro Count;
            /// <summary>The blue wing dot that marks a flyer.</summary>
            public SpriteRenderer AirDot;
            public SpriteRenderer Divider;
        }

        [SerializeField] SpriteRenderer frame;
        [SerializeField] SpriteRenderer bubbleFill;
        [SerializeField] SpriteRenderer bubbleEdge;
        [SerializeField] SpriteRenderer tail;
        [SerializeField] SpriteRenderer badge;
        [SerializeField] TextMeshPro badgeText;
        [SerializeField] TextMeshPro moreText;
        [SerializeField] Slot[] slots = new Slot[2];

        // Tile-unit geometry, read off the original's canvas code (k = one tile).
        const float KindWidth = 0.92f;
        const float KindGap = 0.05f;
        const float BubbleHeight = 0.86f;
        /// <summary>How far the bubble's top sits above the entrance tile's top edge.</summary>
        const float LiftFactor = 0.62f;
        /// <summary>Side placement clears the entrance's name label, which is wider than the tile.</summary>
        const float SideGap = 0.42f;
        const float EdgeMargin = 0.02f;
        /// <summary>Scale at which a captured enemy image renders at its true board size.</summary>
        const float EnemySpriteScale = 2.6f;
        /// <summary>Icon box, in tiles: the original uses k*0.5, and k*0.62 for a boss.</summary>
        const float IconBox = 0.5f, BossIconBox = 0.62f;

        static readonly Color BossInk = new(0.788f, 0.541f, 0.071f);   // #C98A12
        static readonly Color BossBg = new(1f, 0.965f, 0.871f);        // #FFF6DE
        static readonly Color AirInk = new(0.184f, 0.525f, 0.839f);    // #2F86D6
        static readonly Color AirBg = new(0.910f, 0.953f, 0.988f);     // #E8F3FC
        static readonly Color GroundInk = new(0.878f, 0.322f, 0.290f); // #E0524A
        static readonly Color GroundBg = Color.white;

        /// <summary>
        /// Lays the warning out over its entrance. <paramref name="time"/> is presentation
        /// time, so the border keeps pulsing while the simulation is paused - the same as the
        /// original, where the pulse is a render-time effect.
        /// </summary>
        public void Bind(BoardView board, in SpawnWarning warning, GameDatabase db,
                         SpriteLibrary sprites, int cols, int startX, int startY, float time)
        {
            gameObject.SetActive(true);

            var ink = warning.Boss ? BossInk : warning.Air ? AirInk : GroundInk;
            var bg = warning.Boss ? BossBg : warning.Air ? AirBg : GroundBg;

            // ---- the entrance tile's own pulsing border
            if (frame != null)
            {
                frame.transform.localPosition = board.ToWorld(startX + 0.5f, startY + 0.5f);
                frame.transform.localScale = Vector3.one;
                var pulse = ink;
                pulse.a = 0.55f + 0.4f * Mathf.Sin(time * 12f);
                frame.color = pulse;
                frame.enabled = true;
            }

            // ---- bubble box, in board coordinates (y grows downward)
            int kinds = warning.KindCount;
            float w = KindWidth * kinds + (kinds > 1 ? KindGap : 0f);
            float h = BubbleHeight;
            float bx = Mathf.Clamp(startX + 0.5f - w * 0.5f, EdgeMargin, cols - w - EdgeMargin);
            float by = startY - LiftFactor * h;

            // An entrance on the top row has no room above it; the bubble goes beside it so it
            // does not cover the tile and its name label.
            int side = 0;
            if (by < EdgeMargin)
            {
                side = startX + 1 + w + SideGap <= cols ? 1 : -1;
                bx = side > 0 ? startX + 1 + SideGap : startX - w - SideGap;
                by = Mathf.Max(EdgeMargin, startY + 0.5f - h * 0.5f);
            }

            PlaceBox(bubbleFill, board, bx, by, w, h, bg);
            PlaceBox(bubbleEdge, board, bx, by, w, h, ink);
            PlaceTail(board, bx, by, w, h, startX, startY, side, ink);

            // ---- one column per enemy kind
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                if (i >= kinds) { HideSlot(slot); continue; }

                var kind = i == 0 ? warning.First : warning.Second;
                float x0 = bx + (KindWidth + KindGap) * i;
                float cx = x0 + KindWidth * 0.5f;

                if (slot.Icon != null)
                {
                    var def = db.Enemy(kind.EnemyId);
                    slot.Icon.sprite = sprites != null && def != null
                        ? sprites.Enemy(kind.EnemyId, def.Look, def.Boss) : null;
                    slot.Icon.enabled = slot.Icon.sprite != null;
                    slot.Icon.transform.localPosition = board.ToWorld(cx, by + h * 0.4f);
                    slot.Icon.transform.localScale = Vector3.one * IconScaleFor(def, kind.Boss);
                }
                if (slot.Count != null)
                {
                    slot.Count.text = kind.Boss ? "보스" : "×" + kind.Count;
                    slot.Count.transform.localPosition = board.ToWorld(cx, by + h * 0.83f);
                    slot.Count.enabled = true;
                }
                if (slot.AirDot != null)
                {
                    slot.AirDot.enabled = kind.Fly;
                    slot.AirDot.color = AirInk;
                    slot.AirDot.transform.localPosition = board.ToWorld(x0 + 0.15f, by + 0.15f);
                    // Generated sprites span one world unit, so the scale IS the diameter.
                    slot.AirDot.transform.localScale = Vector3.one * 0.26f;
                }
                if (slot.Divider != null)
                {
                    slot.Divider.enabled = i > 0;
                    if (i > 0)
                    {
                        slot.Divider.transform.localPosition =
                            board.ToWorld(x0 - KindGap * 0.5f, by + h * 0.5f);
                        slot.Divider.transform.localScale = new Vector3(0.012f, h - 0.12f, 1f);
                        slot.Divider.color = new Color(0f, 0f, 0f, 0.12f);
                    }
                }
            }

            // ---- "+" when a third kind is due but will not fit
            if (moreText != null)
            {
                moreText.enabled = warning.HasMore;
                moreText.color = ink;
                moreText.transform.localPosition = board.ToWorld(bx + w - 0.1f, by + h * 0.83f);
            }

            // ---- countdown badge on the bubble's top-right corner
            float bcx = bx + w - 0.08f, bcy = by + 0.06f;
            if (badge != null)
            {
                badge.enabled = true;
                badge.color = ink;
                badge.transform.localPosition = board.ToWorld(bcx, bcy);
                badge.transform.localScale = Vector3.one * 0.34f;
            }
            if (badgeText != null)
            {
                badgeText.enabled = true;
                badgeText.text = Mathf.Max(0, Mathf.CeilToInt(warning.Seconds)).ToString();
                badgeText.transform.localPosition = board.ToWorld(bcx, bcy);
            }
        }

        public void Hide() => gameObject.SetActive(false);

        void PlaceBox(SpriteRenderer r, BoardView board, float bx, float by, float w, float h,
                      Color colour)
        {
            if (r == null) return;
            r.enabled = true;
            r.color = colour;
            r.size = new Vector2(w, h);
            r.transform.localPosition = board.ToWorld(bx + w * 0.5f, by + h * 0.5f);
        }

        /// <summary>
        /// The pointer. Below the bubble when it sits above its entrance, on the facing side
        /// when the top row forced it sideways.
        /// </summary>
        void PlaceTail(BoardView board, float bx, float by, float w, float h,
                       int startX, int startY, int side, Color ink)
        {
            if (tail == null) return;
            tail.enabled = true;
            tail.color = ink;
            tail.transform.localScale = Vector3.one * 0.2f;
            if (side == 0)
            {
                tail.transform.localPosition = board.ToWorld(startX + 0.5f, by + h + 0.04f);
                tail.transform.localRotation = Quaternion.identity;
            }
            else
            {
                float ty = Mathf.Min(by + h - 0.1f, startY + 0.35f);
                float ex = side > 0 ? bx + 0.02f : bx + w - 0.02f;
                tail.transform.localPosition = board.ToWorld(ex, ty);
                // The sprite points down; -90 aims it left, +90 right.
                tail.transform.localRotation = Quaternion.Euler(0f, 0f, side > 0 ? -90f : 90f);
            }
        }

        /// <summary>
        /// The icon is the enemy's own captured image shrunk to fit the bubble. The original
        /// scales by <c>size / (r * k * 2.4)</c>, which lands the drawn body at about 0.83 of
        /// the icon box whatever the enemy's radius; this reproduces that from Unity's side,
        /// where the image already renders life-size at <see cref="EnemySpriteScale"/>.
        /// </summary>
        static float IconScaleFor(EnemyDef def, bool boss)
        {
            float r = def != null && def.Radius > 0.01f ? def.Radius : 0.3f;
            float wanted = (boss ? BossIconBox : IconBox) * (2f / 2.4f);
            return EnemySpriteScale * wanted / (2f * r);
        }

        static void HideSlot(Slot slot)
        {
            if (slot.Icon != null) slot.Icon.enabled = false;
            if (slot.Count != null) slot.Count.enabled = false;
            if (slot.AirDot != null) slot.AirDot.enabled = false;
            if (slot.Divider != null) slot.Divider.enabled = false;
        }
    }
}
