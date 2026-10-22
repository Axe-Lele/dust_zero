// 가전 대청소 리그의 저장 기록과 판 끝 보상.
//
// 원본 `lgSave` / `lgFinish` 를 옮긴 것이다. 보상 수치는 전부 league_tier 와 config 에서
// 오고, 여기서는 **누구에게 얼마를 넣을지**만 정한다.
using System;
using System.Collections.Generic;
using DustZero.Core.League;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    public sealed partial class PlayerProfile
    {
        /// <summary>오늘 기준으로 맞춰진 리그 기록. 하루가 바뀌면 보상 판 수만 0 이 된다.</summary>
        public LeagueRecord League
        {
            get
            {
                Data.league ??= new LeagueRecord();
                Data.league.best ??= new Dictionary<string, int>();
                Data.league.cleared ??= new Dictionary<string, bool>();
                if (Data.league.tierMax < 1) Data.league.tierMax = 1;

                var today = SaveClock.TodayKey();
                if (Data.league.day != today)
                {
                    Data.league.day = today;
                    Data.league.runs = 0;
                }
                return Data.league;
            }
        }

        /// <summary>오늘 남은 코인·부품 보상 판 수.</summary>
        public int LeagueRunsLeft()
            => Math.Max(0, _cfg.leagueDailyRewards - League.runs);

        public int LeagueBest(int tier)
            => League.best.TryGetValue(tier.ToString(), out var v) ? v : 0;

        public bool LeagueCleared(int tier)
            => League.cleared.TryGetValue(tier.ToString(), out var v) && v;

        /// <summary>도전할 수 있는 가장 높은 티어.</summary>
        public int LeagueTierMax => Math.Max(1, League.tierMax);

        /// <summary>
        /// 판을 끝내고 받을 것을 넣는다.
        ///
        /// - 코인·부품은 **이긴 라운드 수**에 비례한다. 오늘 보상 판이 남아 있을 때만 준다.
        /// - 장비 강화석은 완주했을 때만.
        /// - 첫 완주는 크리스탈을 주고 다음 티어를 연다.
        /// - 시즌 패스 경험치는 보상 판과 무관하게 쌓이되 40 이 상한이다(원본과 같다).
        /// </summary>
        public LeagueFinishResult FinishLeagueRun(int tier, LeagueTierRow row, int wins,
                                                  bool clear, bool counted, bool quit)
        {
            var sv = League;
            var result = new LeagueFinishResult { Clear = clear, Counted = false };

            if (counted && sv.runs < _cfg.leagueDailyRewards && row != null)
            {
                sv.runs++;
                result.Counted = true;

                long coin = (long)row.winCoin * wins;
                int part = row.winPart * wins;
                if (coin > 0)
                {
                    Data.AddItem(CoinItemId, coin);
                    result.Got.Add($"코인 {coin:N0}");
                }
                if (part > 0)
                {
                    foreach (var family in Db.PartOfFamily.Keys)
                        Data.AddItem(Db.PartOfFamily[family], part);
                    result.Got.Add($"부품 {part}씩");
                }
                if (clear && row.finishStone > 0)
                {
                    Data.AddItem(GearStoneItem, row.finishStone);
                    result.Got.Add($"장비 강화석 {row.finishStone}");
                }
            }

            if (clear) AddPoolPoints(_cfg.poolLeagueClear);

            if (clear && !LeagueCleared(tier))
            {
                sv.cleared[tier.ToString()] = true;
                if (row != null && row.firstCrystal > 0)
                {
                    Data.AddItem(CrystalItemId, row.firstCrystal);
                    result.Got.Add($"첫 완주 크리스탈 {row.firstCrystal}");
                }
                sv.tierMax = Math.Max(sv.tierMax, Math.Min(Db.LeagueTiers.Count, tier + 1));
            }

            var key = tier.ToString();
            sv.best[key] = Math.Max(LeagueBest(tier), wins);
            sv.lastTier = tier;

            int passXp = Math.Min(LeaguePassXpMax, wins * _cfg.passXpLeaguePerWin);
            if (passXp > 0 && !quit)
            {
                AddPassXp(passXp);
                result.Got.Add($"시즌 패스 경험치 +{passXp}");
            }
            return result;
        }

        /// <summary>원본이 한 판에서 주는 패스 경험치 상한.</summary>
        const int LeaguePassXpMax = 40;
    }
}
