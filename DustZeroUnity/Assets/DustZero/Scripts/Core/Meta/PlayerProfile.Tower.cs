// 먼지 타워의 저장 기록과 층 보상.
//
// 원본 `twSave` / `twReward` 를 옮긴 것이다. 층마다 **한 번씩만** 준다 — 첫 클리어와
// 보너스 목표를 따로 센다. 재도전은 공짜이고 보상이 없다.
using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    public sealed partial class PlayerProfile
    {
        public TowerRecord TowerState
        {
            get
            {
                Data.tower ??= new TowerRecord();
                Data.tower.cleared ??= new Dictionary<string, bool>();
                Data.tower.bonus ??= new Dictionary<string, bool>();
                Data.tower.who ??= new Dictionary<string, List<string>>();
                Data.tower.form ??= new List<int>();
                return Data.tower;
            }
        }

        /// <summary>도전할 수 있는 가장 높은 층. 깬 층 + 1 이고 마지막 층을 넘지 않는다.</summary>
        public int TowerTopFloor()
            => Math.Min(Math.Max(1, Db.TowerFloors.Count), TowerState.best + 1);

        public bool TowerCleared(int floor)
            => TowerState.cleared.TryGetValue(floor.ToString(), out var v) && v;

        public bool TowerBonusDone(int floor)
            => TowerState.bonus.TryGetValue(floor.ToString(), out var v) && v;

        /// <summary>그 층을 깼을 때 데리고 간 가전(최대 셋). 층 목록의 창 그림이 쓴다.</summary>
        public IReadOnlyList<string> TowerWho(int floor)
            => TowerState.who.TryGetValue(floor.ToString(), out var list) && list != null
                ? list : System.Array.Empty<string>();

        /// <summary>
        /// 지난 편성. (칸, 가전) 짝으로 저장해 두고 다음 층에서 되살린다.
        /// 저장은 칸과 가전 id 를 번갈아 담은 한 줄이다.
        /// </summary>
        public IEnumerable<(int Cell, string Type)> TowerFormation()
        {
            var form = TowerState.form;
            var types = TowerState.formTypes ?? new List<string>();
            for (int i = 0; i < form.Count && i < types.Count; i++)
                yield return (form[i], types[i]);
        }

        public void SaveTowerFormation(IEnumerable<(int Cell, string Type)> placed)
        {
            var sv = TowerState;
            sv.form = new List<int>();
            sv.formTypes = new List<string>();
            foreach (var (cell, type) in placed)
            {
                sv.form.Add(cell);
                sv.formTypes.Add(type);
            }
        }

        /// <summary>
        /// 층을 처음 깼을 때의 보상. 이미 깼으면 아무것도 주지 않고 false 를 돌려준다.
        /// </summary>
        public bool ClaimTowerFloor(TowerFloorRow floor, List<string> who, List<string> got)
        {
            if (floor == null || TowerCleared(floor.floor)) return false;

            var sv = TowerState;
            var key = floor.floor.ToString();
            sv.cleared[key] = true;
            sv.best = Math.Max(sv.best, floor.floor);
            if (who != null) sv.who[key] = new List<string>(who);

            if (floor.crystal > 0)
            {
                Data.AddItem(CrystalItemId, floor.crystal);
                got.Add($"크리스탈 {floor.crystal}");
            }
            if (floor.coin > 0)
            {
                Data.AddItem(CoinItemId, floor.coin);
                got.Add($"코인 {floor.coin:N0}");
            }
            if (floor.part > 0)
            {
                foreach (var family in Db.PartOfFamily.Keys)
                    Data.AddItem(Db.PartOfFamily[family], floor.part);
                got.Add($"부품 {floor.part}씩");
            }
            if (floor.stone > 0)
            {
                Data.AddItem(GearStoneItem, floor.stone);
                got.Add($"장비 강화석 {floor.stone}");
            }
            if (!string.IsNullOrEmpty(floor.shard) && floor.shardN > 0
                && Db.ShardOfUnit.TryGetValue(floor.shard, out var shardItem))
            {
                Data.AddItem(shardItem, floor.shardN);
                var name = Db.Units.TryGetValue(floor.shard, out var u) ? u.Name : floor.shard;
                got.Add($"{name} 조각 {floor.shardN}");
            }
            return true;
        }

        /// <summary>보너스 목표를 처음 채웠을 때의 크리스탈.</summary>
        public bool ClaimTowerBonus(TowerFloorRow floor, bool ok, List<string> got)
        {
            if (floor == null || !ok || TowerBonusDone(floor.floor)) return false;

            TowerState.bonus[floor.floor.ToString()] = true;
            if (floor.bonusCrystal > 0)
            {
                Data.AddItem(CrystalItemId, floor.bonusCrystal);
                got.Add($"보너스 크리스탈 {floor.bonusCrystal}");
            }
            return true;
        }
    }
}
