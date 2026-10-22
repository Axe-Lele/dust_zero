// Shop: purchase limits, cooldowns and delivery.
//
// Ported from the 10.15-h build's shopSt/prodUsed/prodLock/deliver. Cash and ads are the
// original's test providers: no store SDK is involved, and nothing here claims otherwise.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    public enum PurchaseBlock { None, Locked, SoldOut, TooPoor, Cooling }

    public sealed partial class PlayerProfile
    {
        /// <summary>The shop block, with the daily and weekly buckets rolled over on read.</summary>
        public ShopState Shop
        {
            get
            {
                var st = Data.shop ??= new ShopState();
                if (st.day != SaveClock.TodayKey()) { st.day = SaveClock.TodayKey(); st.dc.Clear(); }
                if (st.week != SaveClock.WeekKey()) { st.week = SaveClock.WeekKey(); st.wc.Clear(); }
                return st;
            }
        }

        int LimitType(string name) => Db.Enum("LimitType", name);
        int PayKind(string name) => Db.Enum("PayKind", name);

        public bool IsCashProduct(ShopProductRow p) => p != null && p.payKind == PayKind("Iap");
        public bool IsAdProduct(ShopProductRow p) => p != null && p.payKind == PayKind("Ad");

        /// <summary>
        /// 첫 구매 보너스를 이미 받았는가. 원본의 `st.first[p.id]` 와 같은 값이다.
        /// 카드의 빨간 '첫 구매 2배' 배지는 아직 안 받았을 때만 뜬다.
        /// </summary>
        public bool FirstBonusTaken(ShopProductRow p)
            => p != null && Shop.first != null
               && Shop.first.TryGetValue(p.id.ToString(), out var had) && had;
        public bool IsCrystalProduct(ShopProductRow p) => p != null && p.payKind == PayKind("Crystal");

        static int Count(Dictionary<string, int> map, int id)
            => map != null && map.TryGetValue(id.ToString(), out var v) ? v : 0;

        static void Bump(Dictionary<string, int> map, int id)
            => map[id.ToString()] = Count(map, id) + 1;

        public int PurchasesUsed(ShopProductRow p)
        {
            if (p == null || p.limitType == 0) return 0;
            if (p.limitType == LimitType("Daily")) return Count(Shop.dc, p.id);
            if (p.limitType == LimitType("Weekly")) return Count(Shop.wc, p.id);
            if (p.limitType == LimitType("Account")) return Count(Shop.ac, p.id);
            return 0;
        }

        /// <summary>Purchases left, or int.MaxValue when the product has no limit.</summary>
        public int PurchasesLeft(ShopProductRow p)
            => p == null || p.limitType == 0 ? int.MaxValue : p.limitCount - PurchasesUsed(p);

        /// <summary>Seconds until an ad product may be watched again.</summary>
        public double CooldownLeft(ShopProductRow p)
        {
            if (p == null || !Shop.cd.TryGetValue(p.id.ToString(), out var until)) return 0d;
            return Math.Max(0d, (until - SaveClock.NowUnixMs()) / 1000d);
        }

        /// <summary>Why this product cannot be bought right now, if it cannot.</summary>
        public PurchaseBlock BlockOn(ShopProductRow p)
        {
            if (p == null) return PurchaseBlock.Locked;
            if (!ConditionMet(p.unlockCond)) return PurchaseBlock.Locked;
            if (p.special == "pass:season" && PassPaid) return PurchaseBlock.SoldOut;
            if (p.special == "pass:noads" && Shop.noAds != 0) return PurchaseBlock.SoldOut;
            if (PurchasesLeft(p) <= 0) return PurchaseBlock.SoldOut;
            if (CooldownLeft(p) > 0d) return PurchaseBlock.Cooling;
            if (IsCrystalProduct(p) && Crystal < p.price) return PurchaseBlock.TooPoor;
            return PurchaseBlock.None;
        }

        public bool CanBuy(ShopProductRow p) => BlockOn(p) == PurchaseBlock.None;

        /// <summary>
        /// Pays for and delivers a product. Cash and ad products are test providers, exactly
        /// as in the original: the purchase succeeds locally and no store or ad network is
        /// contacted.
        /// </summary>
        public List<GrantedReward> Buy(ShopProductRow p)
        {
            var got = new List<GrantedReward>();
            if (!CanBuy(p)) return got;

            if (IsCrystalProduct(p)) Data.AddItem(CrystalItemId, -p.price);

            var st = Shop;
            if (p.rewardGroup != 0) got.AddRange(Grant(p.rewardGroup));
            if (p.firstBonusGroup != 0 && !(st.first.TryGetValue(p.id.ToString(), out var had) && had))
                got.AddRange(Grant(p.firstBonusGroup));
            st.first[p.id.ToString()] = true;

            if (p.special == "pass:season") Pass.paid = 1;
            if (p.special == "pass:noads") st.noAds = 1;
            else if (p.special != null && p.special.StartsWith("idle:")) got.AddRange(GrantIdleHours(p.special));
            else if (p.special != null && p.special.StartsWith("shard:")) got.AddRange(GrantShardOfDay(p.special));

            if (p.limitType == LimitType("Daily")) Bump(st.dc, p.id);
            else if (p.limitType == LimitType("Weekly")) Bump(st.wc, p.id);
            else if (p.limitType == LimitType("Account")) Bump(st.ac, p.id);

            if (p.cooldown > 0) st.cd[p.id.ToString()] = SaveClock.NowUnixMs() + p.cooldown * 1000L;
            if (IsAdProduct(p)) AddPassXp(_cfg.passXpAd);

            MarkDirty();
            return got;
        }

        /// <summary>
        /// "idle:&lt;hours&gt;" (자동 청소 즉시): the stated number of hours at the current idle
        /// rate, granted directly rather than fed through the idleSince/idleBank timer - the
        /// product is a one-off top-up, not a change to how idle accrual itself works.
        /// </summary>
        List<GrantedReward> GrantIdleHours(string special)
        {
            var got = new List<GrantedReward>();
            var st = IdleRateStage;
            if (st == null) return got;
            if (!float.TryParse(special.AsSpan(5), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var hours) || hours <= 0f)
                return got;

            int coin = (int)Math.Floor(st.IdleCoin * hours);
            int part = (int)Math.Floor(st.IdlePart * hours);
            int crystal = (int)Math.Floor(st.IdleCrystal * hours);
            if (coin > 0) { Data.AddItem(CoinItemId, coin); got.Add(new GrantedReward(CoinItemId, coin)); }
            if (crystal > 0) { Data.AddItem(CrystalItemId, crystal); got.Add(new GrantedReward(CrystalItemId, crystal)); }
            if (part > 0)
                foreach (var partId in Db.PartOfFamily.Values)
                {
                    Data.AddItem(partId, part);
                    got.Add(new GrantedReward(partId, part));
                }
            return got;
        }

        /// <summary>
        /// "shard:&lt;rarity&gt;:&lt;count&gt;" (오늘의 조각): rarity 0 is one of the player's own
        /// appliances, 1-3 is R/SR/SSR. Which appliance is "today's" is seeded from the
        /// calendar day so every purchase of the same product on the same day lands on the
        /// same unit, matching the product text's "매일 바뀜".
        /// </summary>
        List<GrantedReward> GrantShardOfDay(string special)
        {
            var got = new List<GrantedReward>();
            var parts = special.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var rarity) || !int.TryParse(parts[2], out var count))
                return got;

            List<string> pool = rarity == 0
                ? Db.UnitOrder.Where(Data.Owns).ToList()
                : Db.UnitOrder.Where(id => (int)Db.Units[id].Rarity == rarity - 1).ToList();
            if (pool.Count == 0) return got;

            string seed = SaveClock.TodayKey() + ":" + special;
            int hash = 0;
            foreach (char c in seed) hash = hash * 31 + c;
            string unitId = pool[((hash % pool.Count) + pool.Count) % pool.Count];

            if (!Db.ShardOfUnit.TryGetValue(unitId, out var shardItem) || count <= 0) return got;
            Data.AddItem(shardItem, count);
            got.Add(new GrantedReward(shardItem, count));
            return got;
        }

        /// <summary>What a product hands over, written out for the card face.</summary>
        public string DescribeProduct(ShopProductRow p)
            => p == null ? string.Empty : DescribeGroup(p.rewardGroup);

        /// <summary>
        /// A reward group written out without rolling it. Used by shop and pass cells, which
        /// must show what is on offer before the player commits.
        /// </summary>
        public string DescribeGroup(int groupId)
        {
            if (groupId == 0 || !Db.RewardGroups.TryGetValue(groupId, out var rows))
                return string.Empty;

            int kindUnit = Db.Enum("RewardKind", "Unit");
            var parts = new List<string>();
            foreach (var r in rows)
            {
                var id = r.refId.AsString();
                if (r.kind == kindUnit)
                {
                    parts.Add(Db.Units.TryGetValue(id, out var u) ? u.Name : id);
                    continue;
                }
                if (!int.TryParse(id, out var itemId)) continue;
                parts.Add($"{ItemName(itemId)} {r.max:N0}");
            }
            return string.Join(" · ", parts);
        }

        /// <summary>One reward chip: an 18px icon and the number (or name) beside it.</summary>
        public readonly struct RewardChip
        {
            /// <summary>Art/Icon file name, as SpriteLibrary indexes them.</summary>
            public readonly string Icon;
            public readonly string Text;
            public RewardChip(string icon, string text) { Icon = icon; Text = text; }
        }

        /// <summary>
        /// 원본 `ITEM_ICON` 과 같은 표: 재화·돌·정수는 아이콘 이름이 표에 없어 코드가 들고 있다.
        /// 부품은 item.family, 조각은 item.unitId 로 **표에서** 끌어낸다.
        /// </summary>
        static readonly string[] PartIcons = { "part_filter", "part_coil", "part_chip", "part_motor" };

        static string IconNameFor(ItemRow item)
        {
            if (item == null) return "star";
            if (item.family > 0 && item.family <= PartIcons.Length) return PartIcons[item.family - 1];
            if (!string.IsNullOrEmpty(item.unitId)) return "shard_" + item.unitId;
            return item.id switch
            {
                1 => "coin",
                2 => "crystal",
                401 => "stone",
                402 => "essence",
                _ => "star",
            };
        }

        /// <summary>
        /// 원본 `rgChips`: 보상 묶음을 아이콘+숫자 칩으로 쓴다. 글자 나열이 아니다.
        /// 부품 네 종이 모두 같은 수면 `부품 각 N` 칩 하나로 묶는다 — 원본과 같은 규칙이다.
        /// </summary>
        public List<RewardChip> GroupChips(int groupId)
        {
            var chips = new List<RewardChip>();
            if (groupId == 0 || !Db.RewardGroups.TryGetValue(groupId, out var rows)) return chips;

            int kindUnit = Db.Enum("RewardKind", "Unit");
            var parts = new List<(ItemRow item, int n)>();

            foreach (var r in rows)
            {
                var id = r.refId.AsString();
                if (r.kind == kindUnit)
                {
                    string name = Db.Units.TryGetValue(id, out var u) ? u.Name : id;
                    chips.Add(new RewardChip("shard_" + id, name));
                    continue;
                }
                if (!int.TryParse(id, out var itemId)) continue;
                Db.Items.TryGetValue(itemId, out var item);
                if (item != null && item.family > 0) { parts.Add((item, r.max)); continue; }
                chips.Add(new RewardChip(IconNameFor(item), $"{r.max:N0}"));
            }

            if (parts.Count == PartIcons.Length && parts.TrueForAll(p => p.n == parts[0].n))
                chips.Add(new RewardChip("parts", $"각 {parts[0].n:N0}"));
            else
                foreach (var p in parts)
                    chips.Add(new RewardChip(IconNameFor(p.item), $"{p.n:N0}"));

            return chips;
        }
    }
}
