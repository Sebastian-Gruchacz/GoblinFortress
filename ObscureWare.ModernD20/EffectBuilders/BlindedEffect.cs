using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;
using ObscureWare.ModernD20.Skills;

namespace ObscureWare.ModernD20.EffectBuilders
{
    /// <summary>
    /// The hero can’t see at all, and thus everything has total concealment to him or her.
    /// 
    /// TODO: The character has a 50% chance to miss in combat.
    /// 
    /// Furthermore, the blinded character has an effective Dexterity of 3,
    ///     - along with a –4 penalty on the use of Strength-based and Dexterity-based skills.
    /// TODO: This –4 penalty also applies to Search checks and any other skill checks for which the GM deems sight to be important.
    /// TODO: The character can’t make Spot checks or perform any other activity (such as reading) that requires vision.
    /// 
    /// WON'T IMPLEMENT: Heroes who are blind long-term (from birth or early in life) grow accustomed to these drawbacks and can overcome some of them (at the GM’s discretion).
    /// </summary>
    public class BlindedEffectBuilder : BaseEffectBuilder
    {
        public BlindedEffectBuilder() : base(EffectTimeFrameEnum.Permanent, EffectStackModeEnum.NonStackable)
        {
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            var effectRuntime =  new BlindedCharacterEffect(this);
            this.CreateExtraEffects(effectRuntime, state);

            return effectRuntime;
        }

        private void CreateExtraEffects(AppliedCharacterEffect effectRuntime, GlobalState state)
        {
            effectRuntime.AddExtraEfect(new PenalizeAbilityBasedSkillsEffectBuilder(AbilityEnum.Strength, EffectTimeFrameEnum.Permanent, 4).GetCharacterEntry(state));
            effectRuntime.AddExtraEfect(new PenalizeAbilityBasedSkillsEffectBuilder(AbilityEnum.Dexterity, EffectTimeFrameEnum.Permanent, 4).GetCharacterEntry(state));

            // TODO: penalize sight-based/improved actions

            effectRuntime.AddExtraEfect(new DisabledSkillEffectBuilder(typeof (Spot), EffectTimeFrameEnum.Permanent).GetCharacterEntry(state));
            // TODO: disbale reading skills, navigation etc
        }
    }

    [Serializable]
    public class BlindedCharacterEffect : AppliedCharacterEffect, IAbilityTargettedCharacterEffect
    {
        public BlindedCharacterEffect(BaseEffectBuilder effectBuilder) : base(effectBuilder)
        {
            this.Value = 3;
            
        }

        public AbilityEnum AffectedAbility
        {
            get { return AbilityEnum.Dexterity; }
        }

        public EffectVectorEnum EffectVector { get { return EffectVectorEnum.Replace; } }
    }
}