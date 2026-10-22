// The partner's roster, pretending to be the player's while the bot places something.
//
// 원본은 `coopAsBot` 에서 `S.owned` 와 `G.power` 를 통째로 바꿔치기했다 가 되돌린다.
// 같은 일을 전역 교체 없이 한다 — 봇이 놓는 동안만 스위치를 켠다.
using DustZero.Core.Defense;
using DustZero.Data;

namespace DustZero.Core.Coop
{
    /// <summary>
    /// Wraps the player's own power source and, while <see cref="BotTurn"/> is on, answers
    /// with the partner's roster instead.
    ///
    /// 파트너는 보유 가전이 따로 없다. 내 상위 6대의 평균 레벨·★ 을 그대로 쓴다 —
    /// 그래서 계정이 자라면 파트너도 같이 자라고, 초반 계정에 과한 파트너가 붙지 않는다.
    /// </summary>
    public sealed class CoopPowerSource : IUnitPowerSource
    {
        readonly IUnitPowerSource _player;
        readonly ConfigRow _cfg;
        readonly int _partnerLevel;
        readonly int _partnerStar;

        /// <summary>While true, every lookup answers as the partner.</summary>
        public bool BotTurn { get; set; }

        public int PartnerLevel => _partnerLevel;
        public int PartnerStar => _partnerStar;

        public CoopPowerSource(IUnitPowerSource player, ConfigRow cfg, int partnerLevel, int partnerStar)
        {
            _player = player;
            _cfg = cfg;
            _partnerLevel = partnerLevel;
            _partnerStar = partnerStar;
        }

        public float PowerOf(string unitId)
            => BotTurn
                ? 1f + _cfg.lvBonus * (_partnerLevel - 1) + _cfg.starBonus * _partnerStar
                : _player.PowerOf(unitId);

        public int LevelOf(string unitId) => BotTurn ? _partnerLevel : _player.LevelOf(unitId);

        public float SupportPowerOf(string unitId)
        {
            if (!BotTurn) return _player.SupportPowerOf(unitId);
            // 지원 가전은 자기 전용 계수가 있을 때만 자란다. 없으면 1 — 본편과 같은 규칙이다.
            if (!_cfg.TryNum(unitId + "LvBonus", out float lvBonus)) return 1f;
            _cfg.TryNum(unitId + "StarBonus", out float starBonus);
            return 1f + lvBonus * (_partnerLevel - 1) + starBonus * _partnerStar;
        }

        // 파트너는 장비를 끼지 않는다. 원본도 `S.excl={}` 로 비우고 놓는다.
        public float GearAtkMul(string unitId) => BotTurn ? 1f : _player.GearAtkMul(unitId);
        public float GearHeatMul(string unitId) => BotTurn ? 1f : _player.GearHeatMul(unitId);
        public float GearChargeMul(string unitId) => BotTurn ? 1f : _player.GearChargeMul(unitId);
        public float GearSkillDuration(string unitId) => BotTurn ? 0f : _player.GearSkillDuration(unitId);

        public float SkillParam(string unitId, string key, float raw)
            => BotTurn ? raw : _player.SkillParam(unitId, key, raw);
    }
}
