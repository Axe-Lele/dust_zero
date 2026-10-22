// Maps unit and enemy ids onto the sprites extracted from the original canvas code.
//
// This is the one place ScriptableObjects are used, and only for asset wiring: it holds
// prefab/sprite references, never balance numbers. Those stay in the Excel-driven tables.
using System;
using System.Collections.Generic;
using DustZero.Data;
using UnityEngine;

namespace DustZero.Game
{
    [Serializable]
    public struct SpriteEntry
    {
        public string id;
        public Sprite sprite;
    }

    /// <summary>
    /// The six illustration slots an appliance has, as named by unit_art. Unused slots stay
    /// null rather than falling back, so a half-drawn appliance is visibly half-drawn.
    /// </summary>
    [Serializable]
    public struct UnitIllust
    {
        public string id;
        public Sprite full, turn, keys, face1, face2, face3;

        /// <summary>The three expression sheets, in table order.</summary>
        public Sprite Face(int index) => index switch { 0 => face1, 1 => face2, _ => face3 };
    }

    [CreateAssetMenu(fileName = "SpriteLibrary", menuName = "DustZero/Sprite Library")]
    public sealed class SpriteLibrary : ScriptableObject
    {
        [Header("가전 (unit.id 과 일치)")]
        public SpriteEntry[] units = Array.Empty<SpriteEntry>();

        [Header("적 (enemy.id, 없으면 look 으로 대체)")]
        public SpriteEntry[] enemies = Array.Empty<SpriteEntry>();

        [Header("외형별 대체 (enemy.look)")]
        public SpriteEntry[] enemyLooks = Array.Empty<SpriteEntry>();

        [Header("보스 외형별 대체")]
        public SpriteEntry[] bossLooks = Array.Empty<SpriteEntry>();

        /// <summary>Indexed by <see cref="Rarity"/>: R, SR, SSR. The pack also ships an N frame,
        /// which this game has no rarity for.</summary>
        [Header("일러스트 (unit_art 의 6칸)")]
        public UnitIllust[] illusts = Array.Empty<UnitIllust>();

        [Header("등급 프레임 (R, SR, SSR 순)")]
        public Sprite[] rarityFrames = Array.Empty<Sprite>();
        [Header("미보유 실루엣 프레임")]
        public Sprite notOwnedFrame;
        [Header("등급 배지 (R, SR, SSR 순)")]
        public Sprite[] gradeBadges = Array.Empty<Sprite>();

        /// <summary>
        /// Art/Icon 의 낱개 아이콘을 파일 이름으로 찾는다. 보상 칩은 어떤 아이콘이 필요한지
        /// **표를 읽어야** 알 수 있어서(coin, crystal, part_*, shard_*), 칸마다 Inspector 에
        /// 미리 끼워 둘 수가 없다.
        /// </summary>
        [Header("낱개 아이콘 (Art/Icon 파일 이름)")]
        public SpriteEntry[] icons = Array.Empty<SpriteEntry>();

        Dictionary<string, Sprite> _units, _enemies, _looks, _bossLooks, _icons;

        void Index()
        {
            if (_units != null) return;
            _units = Build(units);
            _enemies = Build(enemies);
            _looks = Build(enemyLooks);
            _bossLooks = Build(bossLooks);
            _icons = Build(icons);
        }

        /// <summary>An icon by its file name, or null when the pack has none.</summary>
        public Sprite Icon(string name)
        {
            Index();
            return !string.IsNullOrEmpty(name) && _icons.TryGetValue(name, out var s) ? s : null;
        }

        static Dictionary<string, Sprite> Build(SpriteEntry[] src)
        {
            var d = new Dictionary<string, Sprite>();
            foreach (var e in src)
                if (!string.IsNullOrEmpty(e.id) && e.sprite != null) d[e.id] = e.sprite;
            return d;
        }

        /// <summary>The collection frame for a rarity, or the silhouette when unowned.</summary>
        public Sprite Frame(Rarity rarity, bool owned)
        {
            if (!owned) return notOwnedFrame;
            int i = (int)rarity;
            return rarityFrames != null && i >= 0 && i < rarityFrames.Length ? rarityFrames[i] : null;
        }

        public Sprite Grade(Rarity rarity)
        {
            int i = (int)rarity;
            return gradeBadges != null && i >= 0 && i < gradeBadges.Length ? gradeBadges[i] : null;
        }

        public Sprite Unit(string unitId)
        {
            Index();
            return _units.TryGetValue(unitId, out var s) ? s : null;
        }

        /// <summary>
        /// The illustration set for an appliance, or an empty one when it has no art yet.
        /// Callers check the individual slots; a missing set is not an error, it is a gap.
        /// </summary>
        public UnitIllust Illust(string unitId)
        {
            if (illusts != null)
                foreach (var i in illusts)
                    if (i.id == unitId) return i;
            return default;
        }

        public bool HasIllust(string unitId) => Illust(unitId).full != null;

        /// <summary>
        /// Exact id first, then the look variant. An enemy added to the table without its own
        /// art still renders as its family rather than disappearing.
        /// </summary>
        public Sprite Enemy(string enemyId, string look, bool boss)
        {
            Index();
            if (_enemies.TryGetValue(enemyId, out var s)) return s;
            if (boss && _bossLooks.TryGetValue(look ?? string.Empty, out var b)) return b;
            return _looks.TryGetValue(look ?? string.Empty, out var l) ? l : null;
        }
    }
}
