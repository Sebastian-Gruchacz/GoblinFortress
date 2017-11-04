using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    /// <summary>
    /// The character has lost 1 or more ability score points. The loss is permanent. (Until restored with particular spell)
    /// </summary>
    public class AbilityDrainedEffectBuilder : BaseEffectBuilder
    {
        private readonly AbilityEnum _affectedAbility;
        private readonly uint _newDamagedPoints;

        public AbilityDrainedEffectBuilder(AbilityEnum affectedAbility, uint newDamagedPoints) : base(EffectTimeFrameEnum.Permanent, EffectStackModeEnum.Stackable)
        {
            _affectedAbility = affectedAbility;
            _newDamagedPoints = newDamagedPoints;
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            return new AbilityDrainedCharacterEffect(this, _affectedAbility, _newDamagedPoints);
        }
    }

    [Serializable]
    public class AbilityDrainedCharacterEffect : AppliedCharacterEffect, IAbilityTargettedCharacterEffect
    {
        private readonly AbilityEnum _affectedAbility;

        public AbilityDrainedCharacterEffect(BaseEffectBuilder effectBuilder, AbilityEnum affectedAbility, uint newDamagedPoints) : base(effectBuilder)
        {
            _affectedAbility = affectedAbility;
            Value = (int)newDamagedPoints;
        }

        public AbilityEnum AffectedAbility
        {
            get { return _affectedAbility; }
        }

        public EffectVectorEnum EffectVector { get { return EffectVectorEnum.Negative; } }

        protected override void UpdateRound(ModernD20Library environment, Character character)
        {
            // nothing, this does not change during round
        }

        protected override void UpdateWithRest(ModernD20Library environment, Character character, RestTypeEnum restType)
        {
            // nothing, does not restore during rest
        }
    }
}