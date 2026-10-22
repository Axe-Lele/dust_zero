// Appliance growth beyond levels: shard recruitment and limit-break.
//
// Ported from the 10.15-h build's recruitNeed/canStar/starUp/recruit. Shards are ordinary
// items, one id per appliance, so nothing new is persisted - the counts already live in the
// item wallet.
using System;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    public sealed partial class PlayerProfile
    {
        /// <summary>The item id of this appliance's shard, or 0 when it has none.</summary>
        public int ShardItemOf(string unitId)
            => unitId != null && Db.ShardOfUnit.TryGetValue(unitId, out var id) ? id : 0;

        public long ShardCount(string unitId)
        {
            int id = ShardItemOf(unitId);
            return id == 0 ? 0 : ItemCount(id);
        }

        /// <summary>Shards needed to recruit an appliance the player does not own.</summary>
        public int RecruitCost(string unitId)
        {
            var costs = _cfg.shardRecruit;
            if (costs == null || costs.Length == 0) return int.MaxValue;
            if (!Db.Units.TryGetValue(unitId, out var def)) return int.MaxValue;
            int rank = (int)def.Rarity;
            return costs[Math.Clamp(rank, 0, costs.Length - 1)];
        }

        public bool CanRecruit(string unitId)
            => !Data.Owns(unitId) && ShardItemOf(unitId) != 0
               && ShardCount(unitId) >= RecruitCost(unitId);

        /// <summary>Spends shards to add an unowned appliance to the roster.</summary>
        public bool Recruit(string unitId)
        {
            if (!CanRecruit(unitId)) return false;
            Data.AddItem(ShardItemOf(unitId), -RecruitCost(unitId));
            AddUnit(unitId);
            MarkDirty();
            return true;
        }

        /// <summary>The limit-break step this appliance would take next, or null at the cap.</summary>
        public UnitStarRow NextStarStep(string unitId)
        {
            var owned = Data.Own(unitId);
            return owned == null ? null : Db.StarStep(owned.star + 1);
        }

        public bool CanStarUp(string unitId)
        {
            var next = NextStarStep(unitId);
            return next != null && ShardCount(unitId) >= next.shards && Coin >= next.coin;
        }

        /// <summary>Spends shards and coin to raise the limit-break, which raises the level cap.</summary>
        public bool StarUp(string unitId)
        {
            var next = NextStarStep(unitId);
            if (next == null || !CanStarUp(unitId)) return false;

            Data.AddItem(ShardItemOf(unitId), -next.shards);
            Data.AddItem(CoinItemId, -next.coin);
            Data.Own(unitId).star = next.star;
            MarkDirty();
            return true;
        }

        /// <summary>The level ceiling this appliance's current limit-break allows.</summary>
        public int LevelCapOf(string unitId)
        {
            var owned = Data.Own(unitId);
            return owned == null ? Db.LevelCap(0) : Db.LevelCap(owned.star);
        }
    }
}
