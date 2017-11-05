using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    /// <summary>
    /// The hero is frozen in fear, loses his or her Dexterity bonus, and can take no actions.
    /// In addition, the character takes a –2 penalty to his or her Defense.
    /// The condition typically lasts 10 rounds.
    /// </summary>
    public class CoveringEffectBuilder : BaseEffectBuilder
    {
        private readonly int _penaltyRounds;

        public CoveringEffectBuilder(int penaltyRounds) : base(EffectTimeFrameEnum.Temporary, EffectStackModeEnum.Stackable) // TODO: select bigger stacking?
        {
            this._penaltyRounds = penaltyRounds;
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            var effectRuntime = new DisableAbilityBonusCharacterEffect(this, AbilityEnum.Dexterity, this._penaltyRounds);
            this.CreateExtraEffects(effectRuntime, state);

            return effectRuntime;
        }

        private void CreateExtraEffects(AppliedCharacterEffect effectRuntime, GlobalState state)
        {
            effectRuntime.AddExtraEfect(new UnableToActEffectBuilder(EffectTimeFrameEnum.Temporary, this._penaltyRounds).GetCharacterEntry(state));
            effectRuntime.AddExtraEfect(new PenalizeDefenseEffectBuilder(EffectTimeFrameEnum.Temporary, this._penaltyRounds, 2).GetCharacterEntry(state));
        }

        public static readonly int DEFAULT_PENALTY_ROUNDS = 10;
    }

    [Serializable]
    public class DisableAbilityBonusCharacterEffect : AppliedCharacterEffect, IAbilityBonusTargettedCharacterEffect
    {
        public DisableAbilityBonusCharacterEffect(BaseEffectBuilder effectBuilder, AbilityEnum affectedAbility, int penaltyRounds) : base(effectBuilder)
        {
            this.AffectedAbility = affectedAbility;
            this.Value = 0;
        }

        public AbilityEnum AffectedAbility { get; }

        public EffectVectorEnum EffectVector { get { return EffectVectorEnum.Replace; } }
    }
}