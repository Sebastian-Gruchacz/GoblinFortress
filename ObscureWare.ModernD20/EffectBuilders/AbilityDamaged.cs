﻿using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    /// <summary>
    /// The character has lost 1 or more ability score points. 
    /// The loss is temporary, and these points return at a rate of 1 per evening of rest.
    /// This differs from “effective” ability loss, which is an effect that goes away when the condition causing it goes away.
    /// </summary>
    public class AbilityDamagedEffectBuilder : BaseEffectBuilder
    {
        private readonly AbilityEnum _affectedAbility;
        private readonly uint _newDamagedPoints;

        public AbilityDamagedEffectBuilder(AbilityEnum affectedAbility, uint newDamagedPoints) : base(EffectTimeFrameEnum.Restorable, EffectStackModeEnum.Stackable)
        {
            _affectedAbility = affectedAbility;
            _newDamagedPoints = newDamagedPoints;
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            return new AbilityDamagedCharacterEffect(this, _affectedAbility, _newDamagedPoints);
        }
    }

    [Serializable]
    public class AbilityDamagedCharacterEffect : AppliedCharacterEffect, IAbilityTargettedCharacterEffect
    {
        private readonly AbilityEnum _affectedAbility;

        public AbilityDamagedCharacterEffect(BaseEffectBuilder effectBuilder, AbilityEnum affectedAbility, uint newDamagedPoints) : base(effectBuilder)
        {
            _affectedAbility = affectedAbility;
            this.ReplacePrevious = false;
            Value = (int) newDamagedPoints;

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
            switch (restType)
            {
                case RestTypeEnum.Single8Hours:
                case RestTypeEnum.Full24Hours:
                {
                    Value--;
                    environment.CoreNotifications.ReportCharacterAbilityRestored(character, AffectedAbility);
                    if (Value <= 0)
                    {
                        environment.RestorationLogic.RemoveEffect(character, this);
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(restType), restType, null);
            }
        }
    }
}
