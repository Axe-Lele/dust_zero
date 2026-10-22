// 판 위의 층 규칙 연출 — 원본 `twRenderFloor` · `twRenderPrep` · `twDrawFoe` ·
// `twDrawPurifier`.
//
// 리그 판을 그대로 쓰므로 LeagueView 에 타워 가지만 더했다. 규칙이 바꾸는 것은 넷이다:
//   · 바닥에 깔리는 것 — 물바닥 웅덩이, 열기 아지랑이, 못 놓는 칸의 빗금
//   · 준비 단계에만 세우는 것 — 공기청정기, 황금 먼지, 거울에 비친 우리 가전
//   · 전투 중 모습이 다른 것 — 청정기(제 그림), 거울(회색 가전), 황금 먼지(금빛 + 반짝임)
//   · 자리 — 물바닥 세 곳은 LeagueSim.WaterPools 가 들고 있다(원본 `TW_POOLS`).
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.League;
using UnityEngine;

namespace DustZero.Game
{
    public sealed partial class LeagueView
    {
        [Header("먼지 타워")]
        [SerializeField] Sprite waterPoolSprite;
        [SerializeField] Sprite heatWaveSprite;
        [SerializeField] Sprite blockedCellSprite;
        [SerializeField] Sprite sparkleSprite;
        [SerializeField] Sprite purifierSprite;

        readonly List<SpriteRenderer> _decor = new();
        readonly List<SpriteRenderer> _sparks = new();
        readonly List<EntityView> _props = new();

        /// <summary>원본 색 그대로.</summary>
        static readonly Color Water = new(90f / 255f, 170f / 255f, 220f / 255f, 0.42f);
        static readonly Color Heat = new(232f / 255f, 110f / 255f, 50f / 255f, 0.22f);
        static readonly Color Blocked = new(80f / 255f, 70f / 255f, 60f / 255f, 0.35f);
        static readonly Color MirrorInk = new(0.62f, 0.58f, 0.66f);
        static readonly Color GoldInk = new(1f, 0.82f, 0.32f);

        /// <summary>황금 먼지 둘레의 반짝임 자리 — 원본이 적어 둔 세 곳.</summary>
        static readonly Vector2[] SparkAt = { new(-0.32f, 0.3f), new(0.3f, 0.38f), new(0.05f, 0.55f) };

        /// <summary>
        /// 바닥에 깔리는 것과(언제나) 준비 단계에만 세우는 것을 함께 맞춘다.
        /// 리그 판이면 아무것도 하지 않는다.
        /// </summary>
        void SyncTower(LeagueSim sim, bool prep)
        {
            int decor = 0;
            if (sim.IsTower)
            {
                var tower = sim.Tower;
                if (tower.RuleId == "water")
                    foreach (var (x, y, r) in LeagueSim.WaterPools)
                        Decor(ref decor, waterPoolSprite, x, y, 2f * r, 2f * r, Water, 120);

                if (tower.RuleId == "heat")
                    for (int i = 0; i < LeagueSim.Cols; i++)
                    {
                        // 원본과 같은 자리에서 올라오고, 시간에 따라 좌우로 흔들린다.
                        float x = i + 0.5f + Mathf.Sin(Time.unscaledTime * 3f + i) * 0.06f;
                        float y = (i * 37) % 5 + 0.8f;
                        Decor(ref decor, heatWaveSprite, x, y - 0.3f, 0.3f, 0.7f, Heat, 120);
                    }

                // 못 놓는 칸은 놓는 동안에만 보여 준다 — 좁은 복도의 양 옆, 청정기 자리.
                if (prep)
                    for (int i = 0; i < LeagueSim.BoardCells; i++)
                    {
                        if (sim.TowerCellOk(i)) continue;
                        Decor(ref decor, blockedCellSprite,
                              i % LeagueSim.Cols + 0.5f,
                              LeagueSim.OurRows + i / LeagueSim.Cols + 0.5f,
                              1f, 1f, Blocked, 260);
                    }
            }
            for (int i = decor; i < _decor.Count; i++)
                if (_decor[i] != null) _decor[i].gameObject.SetActive(false);

            if (prep) ShowTowerProps(sim);
            else ClearProps();
        }

        /// <summary>
        /// 준비 단계의 미리 보기(원본 `twRenderPrep`): 청정기와 황금 먼지는 전투가
        /// 시작돼야 생기므로, 그 전에는 같은 자리에 흐린 그림으로 세워 둔다.
        /// 거울은 우리가 지금 놓은 가전을 위쪽에 베껴 보여 준다.
        /// </summary>
        void ShowTowerProps(LeagueSim sim)
        {
            int n = 0, spark = 0;
            if (sim.IsTower)
            {
                var tower = sim.Tower;
                switch (tower.RuleId)
                {
                    case "guard":
                        Prop(ref n, purifierSprite, 3.5f, 5.45f, Color.white, AllyScale);
                        break;

                    case "golden":
                        Prop(ref n, sprites != null ? sprites.Enemy("dust", null, false) : null,
                             3.5f, 0.45f, GoldInk, EnemyScale);
                        foreach (var at in SparkAt)
                            Spark(ref spark, 3.5f + at.x, 0.45f - at.y);
                        break;

                    case "mirror":
                    {
                        int copies = tower.Rule != null ? (int)tower.Rule.p1 : 0;
                        foreach (var (piece, cell) in sim.Board
                                     .Select((p, i) => (p, i))
                                     .Where(t => t.p != null)
                                     .Take(copies))
                        {
                            float cx = cell % LeagueSim.Cols + 0.5f;
                            float cy = LeagueSim.OurRows + cell / LeagueSim.Cols + 0.55f;
                            Prop(ref n, sprites != null ? sprites.Unit(piece.Type) : null,
                                 cx, LeagueSim.Rows - cy, MirrorInk, AllyScale);
                        }
                        break;
                    }
                }
            }
            TrimProps(n);
            TrimSparks(spark);
        }

        void ClearProps()
        {
            TrimProps(0);
            TrimSparks(0);
        }

        /// <summary>전투 중 모습이 다른 셋 — 청정기 · 거울 · 황금 먼지.</summary>
        void DressTowerUnit(LeagueSim sim, LeagueUnit u, EntityView v)
        {
            if (!sim.IsTower) return;

            if (u.Type == "purifier")
            {
                v.Body.sprite = purifierSprite;
                v.transform.localScale = Vector3.one * AllyScale;
                if (v.TierBadge != null) v.TierBadge.text = string.Empty;
                return;
            }
            if (u.Mirror)
            {
                // 거울에 비친 우리 가전이다 — 먼지 그림이 아니라 가전 그림을 회색으로.
                v.Body.sprite = sprites != null ? sprites.Unit(u.Type) : null;
                if (v.TierBadge != null)
                {
                    v.TierBadge.text = "거울";
                    // 원본도 흐린 보랏빛 글씨로 적는다.
                    v.TierBadge.color = new Color(0.35f, 0.31f, 0.43f, 0.85f);
                }
            }
        }

        /// <summary>전투 중 칠. 거울은 회색, 황금 먼지는 금빛이고 둘레가 반짝인다.</summary>
        Color TowerTint(LeagueSim sim, LeagueUnit u, Color normal)
        {
            if (!sim.IsTower) return normal;
            if (u.Mirror) return normal * MirrorInk;
            if (sim.Tower.Gold == u) return normal * GoldInk;
            return normal;
        }

        /// <summary>전투 중 황금 먼지 둘레의 반짝임.</summary>
        void SyncTowerSparks(LeagueSim sim)
        {
            int spark = 0;
            var gold = sim.IsTower ? sim.Tower.Gold : null;
            if (gold != null && gold.Alive)
                foreach (var at in SparkAt)
                    Spark(ref spark, gold.X + at.x, gold.Y - at.y);
            TrimSparks(spark);
        }

        // ------------------------------------------------------------------ 빌려 쓰는 것들

        void Decor(ref int index, Sprite sprite, float x, float y, float w, float h,
                   Color color, int order)
        {
            while (_decor.Count <= index)
            {
                var go = new GameObject("TowerDecor");
                go.transform.SetParent(entityRoot, false);
                go.layer = entityRoot.gameObject.layer;
                _decor.Add(go.AddComponent<SpriteRenderer>());
            }
            var sr = _decor[index++];
            sr.gameObject.SetActive(true);
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.color = color;
            sr.transform.localPosition = ToWorld(x, y);
            sr.transform.localScale = new Vector3(w, h, 1f);
        }

        void Spark(ref int index, float x, float y)
        {
            while (_sparks.Count <= index)
            {
                var go = new GameObject("GoldSpark");
                go.transform.SetParent(entityRoot, false);
                go.layer = entityRoot.gameObject.layer;
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = sparkleSprite;
                r.sortingOrder = 620;
                _sparks.Add(r);
            }
            var s = _sparks[index++];
            s.gameObject.SetActive(true);
            // 원본처럼 깜빡인다.
            float a = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f + index);
            s.color = new Color(1f, 0.94f, 0.71f, a);
            s.transform.localPosition = ToWorld(x, y);
            s.transform.localScale = Vector3.one * 0.22f;
        }

        void TrimSparks(int used)
        {
            for (int i = used; i < _sparks.Count; i++)
                if (_sparks[i] != null) _sparks[i].gameObject.SetActive(false);
        }

        void Prop(ref int index, Sprite sprite, float x, float y, Color tint, float scale)
        {
            while (_props.Count <= index)
            {
                var v = _pool.Get();
                if (v.BarRoot != null) v.BarRoot.gameObject.SetActive(false);
                _props.Add(v);
            }
            var prop = _props[index++];
            prop.gameObject.SetActive(true);
            prop.transform.localScale = Vector3.one * scale;
            prop.Body.sprite = sprite;
            prop.Body.sortingOrder = 360;
            prop.Body.flipX = false;
            // 준비 단계의 미리 보기는 원본처럼 옅다.
            prop.Tint(new Color(tint.r, tint.g, tint.b, 0.62f));
            prop.transform.localPosition = ToWorld(x, y);
            if (prop.TierBadge != null) prop.TierBadge.text = string.Empty;
        }

        void TrimProps(int used)
        {
            for (int i = used; i < _props.Count; i++)
                if (_props[i] != null) _props[i].gameObject.SetActive(false);
        }

        void ClearTower()
        {
            foreach (var d in _decor) if (d != null) Destroy(d.gameObject);
            _decor.Clear();
            foreach (var s in _sparks) if (s != null) Destroy(s.gameObject);
            _sparks.Clear();
            foreach (var p in _props) if (p != null) _pool.Release(p);
            _props.Clear();
        }
    }
}
