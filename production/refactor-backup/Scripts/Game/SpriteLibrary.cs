// Maps unit and enemy ids onto the sprites extracted from the original canvas code.
//
// This is the one place ScriptableObjects are used, and only for asset wiring: it holds
// prefab/sprite references, never balance numbers. Those stay in the Excel-driven tables.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DustZero.Game
{
    [Serializable]
    public struct SpriteEntry
    {
        public string id;
        public Sprite sprite;
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

        Dictionary<string, Sprite> _units, _enemies, _looks, _bossLooks;

        void Index()
        {
            if (_units != null) return;
            _units = Build(units);
            _enemies = Build(enemies);
            _looks = Build(enemyLooks);
            _bossLooks = Build(bossLooks);
        }

        static Dictionary<string, Sprite> Build(SpriteEntry[] src)
        {
            var d = new Dictionary<string, Sprite>();
            foreach (var e in src)
                if (!string.IsNullOrEmpty(e.id) && e.sprite != null) d[e.id] = e.sprite;
            return d;
        }

        public Sprite Unit(string unitId)
        {
            Index();
            return _units.TryGetValue(unitId, out var s) ? s : null;
        }

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
