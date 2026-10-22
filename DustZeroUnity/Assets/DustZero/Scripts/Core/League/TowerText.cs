// 먼지 타워가 화면에 쓰는 문구 — 원본 `twRuleName` / `twRuleDesc` / `twBonusText` /
// `twGoalText` / `twSlots` / `twHot` 를 옮긴 것이다.
//
// 화면이 아니라 여기에 두는 까닭: 수치와 말은 전부 표에서 오고, 그 조합을 EditMode 로
// 검사할 수 있어야 하기 때문이다. 어느 것도 C# 에 복제하지 않는다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DustZero.Data;

namespace DustZero.Core.League
{
    public static class TowerText
    {
        /// <summary>층 규칙 이름. 규칙이 없는 층은 '기본'이다.</summary>
        public static string RuleName(GameDatabase db, TowerFloorRow floor)
            => floor != null && !string.IsNullOrEmpty(floor.rule)
               && db.TowerRules.TryGetValue(floor.rule, out var r)
                ? db.Text(r.nameKey) : "기본";

        /// <summary>규칙 종류(a 판 · b 목표 · c 편성). 규칙이 없으면 빈 문자열.</summary>
        public static string RuleKind(GameDatabase db, TowerFloorRow floor)
            => floor != null && !string.IsNullOrEmpty(floor.rule)
               && db.TowerRules.TryGetValue(floor.rule, out var r)
                ? r.kind ?? string.Empty : string.Empty;

        /// <summary>
        /// 규칙 설명. `{p1}` `{p2}` `{p1_pct}` `{p2_pct}` 는 규칙 행의 수치로,
        /// `{guest}` 는 그 층이 빌려 주는 가전 이름으로 바뀐다.
        /// 보스 층은 보스 이름과 그 보스의 안내를 쓴다(원본과 같다).
        /// </summary>
        public static string RuleDesc(GameDatabase db, TowerFloorRow floor)
        {
            if (floor == null || string.IsNullOrEmpty(floor.rule)
                || !db.TowerRules.TryGetValue(floor.rule, out var rule))
                return "규칙 없는 기본 층이에요. 먼지를 모두 치우면 클리어.";

            if (floor.rule == "boss")
            {
                foreach (var (id, _) in LeagueSim.ParseSquad(floor.squad))
                    if (id.StartsWith("boss_", StringComparison.Ordinal))
                    {
                        string key = "tower.boss." + id;
                        string name = db.Enemies.TryGetValue(id, out var e) ? e.Name : id;
                        if (db.HasText(key)) return $"{name} · {db.Text(key)}";
                        break;
                    }
            }

            string text = db.Text(rule.descKey);
            text = Regex.Replace(text, @"\{(p1|p2)(_pct)?\}", m =>
            {
                float v = m.Groups[1].Value == "p1" ? rule.p1 : rule.p2;
                return m.Groups[2].Success
                    ? Math.Round(v * 100f).ToString(CultureInfo.InvariantCulture)
                    : Trim(v);
            });
            if (text.Contains("{guest}"))
                text = text.Replace("{guest}", GuestName(db, floor));
            return text;
        }

        /// <summary>이 층이 빌려 주는 가전 이름. 없으면 빈 문자열.</summary>
        public static string GuestName(GameDatabase db, TowerFloorRow floor)
            => floor != null && !string.IsNullOrEmpty(floor.extra)
                ? db.Units.TryGetValue(floor.extra, out var u) ? u.Name : floor.extra
                : string.Empty;

        /// <summary>보너스 목표 한 줄. `{n}` 은 층이 정한 수다.</summary>
        public static string BonusText(GameDatabase db, TowerFloorRow floor)
        {
            if (floor == null || string.IsNullOrEmpty(floor.bonus)) return string.Empty;
            foreach (var b in db.TowerBonuses)
                if (b.id == floor.bonus)
                    return db.Text(b.nameKey)
                             .Replace("{n}", floor.bonusN.ToString(CultureInfo.InvariantCulture));
            return string.Empty;
        }

        /// <summary>이 층의 목표. 지키기 층은 버티는 시간이, 보스 층은 보스가 붙는다.</summary>
        public static string GoalText(GameDatabase db, TowerFloorRow floor)
        {
            if (floor == null) return string.Empty;
            if (floor.rule == "guard" && db.TowerRules.TryGetValue("guard", out var g))
                return $"공기청정기 {Trim(g.p1)}초 지키기";
            return floor.rule == "boss" ? "보스와 먼지 전멸" : "먼지 전멸";
        }

        /// <summary>출전할 수 있는 수. 단독 층만 규칙이 따로 정한다.</summary>
        public static int Slots(GameDatabase db, TowerFloorRow floor)
            => floor != null && floor.rule == "solo" && db.TowerRules.TryGetValue("solo", out var s)
                ? (int)s.p1 : floor?.slots ?? 0;

        /// <summary>이 층의 제한 시간. 지키기 층은 버티는 시간이 곧 제한이다.</summary>
        public static float TimeLimit(GameDatabase db, TowerFloorRow floor)
            => floor != null && floor.rule == "guard" && db.TowerRules.TryGetValue("guard", out var g)
                ? g.p1 : db.Config.towerTime;

        /// <summary>
        /// 이 층에서 특히 쓸모가 큰 가전(원본 `twHot`): 열기 층의 냉방 가전, 정전 층의
        /// 발전기, 그리고 그 층이 빌려 주는 손님.
        /// </summary>
        public static bool Recommended(GameDatabase db, TowerFloorRow floor, string unitId,
                                       string guest)
        {
            if (floor == null || string.IsNullOrEmpty(unitId)) return false;
            if (floor.rule == "heat" && db.Units.TryGetValue(unitId, out var u)
                && u.Family == CoolFamily) return true;
            if (floor.rule == "blackout" && unitId == "gen") return true;
            return !string.IsNullOrEmpty(guest) && guest == unitId;
        }

        /// <summary>원본이 열기 층에서 찾는 냉방 계열 번호.</summary>
        const int CoolFamily = 4;

        /// <summary>첫 클리어 보상 한 줄. 표에 없는 항목은 빠진다.</summary>
        public static string RewardLine(GameDatabase db, TowerFloorRow floor)
        {
            if (floor == null) return string.Empty;
            var parts = new List<string>();
            if (floor.crystal > 0) parts.Add($"크리스탈 {floor.crystal}");
            if (floor.coin > 0) parts.Add($"코인 {floor.coin:N0}");
            if (floor.part > 0) parts.Add($"부품 {floor.part}");
            if (floor.stone > 0) parts.Add($"강화석 {floor.stone}");
            if (floor.shardN > 0 && !string.IsNullOrEmpty(floor.shard))
                parts.Add($"{ShardName(db, floor.shard)} {floor.shardN}");
            return string.Join(" · ", parts);
        }

        static string ShardName(GameDatabase db, string unitId)
            => db.Units.TryGetValue(unitId, out var u) ? $"{u.Name} 조각" : "조각";

        /// <summary>표의 수치를 사람이 읽는 꼴로 — 20 은 "20", 1.5 는 "1.5".</summary>
        static string Trim(float v)
            => Math.Abs(v - Math.Round(v)) < 0.001f
                ? Math.Round(v).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
