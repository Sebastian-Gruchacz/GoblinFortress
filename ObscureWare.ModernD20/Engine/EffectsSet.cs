using System;
using System.Collections.Generic;
using System.Linq;
using ObscureWare.Common;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.EffectBuilders;

namespace ObscureWare.ModernD20.Engine
{
    public class EffectsSet
    {
        private readonly Character _character;
        private readonly Dictionary<Guid, AppliedCharacterEffect> _appliedCharacterEffects = new Dictionary<Guid, AppliedCharacterEffect>();

        public EffectsSet(Character character)
        {
            this._character = character;
        }

        internal int? GetAppliedEffectValue(Guid effectId)
        {
            AppliedCharacterEffect effect;
            if (this._appliedCharacterEffects.TryGetValue(effectId, out effect))
            {
                return effect.Value;
            }

            return null;
        }

        internal bool HasEffectApplied(Guid effectId)
        {
            return this._appliedCharacterEffects.ContainsKey(effectId);
        }

        internal void ApplyEffect(AppliedCharacterEffect effect)
        {
            AppliedCharacterEffect currentEffect;
            if (this._appliedCharacterEffects.TryGetValue(effect.EffectId, out currentEffect))
            {
                if (effect.ReplacePrevious)
                {
                    this._appliedCharacterEffects[effect.EffectId] = effect;
                }
                else
                {
                    currentEffect.StackWith(effect);
                }
            }
            else
            {
                this._appliedCharacterEffects.Add(effect.EffectId, effect);
            }
        }

        internal void RemoveEffect(AppliedCharacterEffect effect)
        {
            this._appliedCharacterEffects.Remove(effect.EffectId);
        }

        internal uint? ApplyEffectsToAbilityScore(AbilityEnum affectedAbility, uint? score)
        {
            // Characters without Ability score will also ignore any Ability modifiers that would normally apply to it
            if (score == null)
            {
                return null;
            }

            // 1. find worst replacing effect first, and apply if any
            var worseReplacingEffect = this._appliedCharacterEffects.Values.Where(e =>
                e.GetType().Implements(typeof (IAbilityTargettedCharacterEffect)) &&
                ((IAbilityTargettedCharacterEffect) e).AffectedAbility == affectedAbility &&
                ((IAbilityTargettedCharacterEffect) e).EffectVector == EffectVectorEnum.Replace)
                .OrderBy(e => e.Value)
                .FirstOrDefault();
            if (worseReplacingEffect != null)
            {
                return (uint)worseReplacingEffect.Value;
            }

            // 2. get all other effects - will sum final efefct later
            var otherApplyingEffects = this._appliedCharacterEffects.Values.Where(e =>
                e.GetType().Implements(typeof (IAbilityTargettedCharacterEffect)) &&
                ((IAbilityTargettedCharacterEffect) e).AffectedAbility == affectedAbility &&
                ((IAbilityTargettedCharacterEffect) e).EffectVector != EffectVectorEnum.Replace);

            foreach (var otherEffect in otherApplyingEffects)
            {
                if (((IAbilityTargettedCharacterEffect) otherEffect).EffectVector == EffectVectorEnum.Positive)
                {
                    score += (uint)otherEffect.Value;
                }
                else
                {
                    score -= (uint)otherEffect.Value;
                }
            }

            return score;
        }

        internal int ApplyEffectsToAbilityBaseModifier(AbilityEnum ability, int baseModifier)
        {
            // 1. find worst replacing effect first, and apply if any
            var worseReplacingEffect = this._appliedCharacterEffects.Values.Where(e =>
                e.GetType().Implements(typeof(IAbilityBonusTargettedCharacterEffect)) &&
                ((IAbilityBonusTargettedCharacterEffect)e).AffectedAbility == ability &&
                ((IAbilityBonusTargettedCharacterEffect)e).EffectVector == EffectVectorEnum.Replace)
                .OrderBy(e => e.Value)
                .FirstOrDefault();
            if (worseReplacingEffect != null)
            {
                return (int)worseReplacingEffect.Value;
            }

            // 2. get all other effects - will sum final efefct later
            var otherApplyingEffects = this._appliedCharacterEffects.Values.Where(e =>
                e.GetType().Implements(typeof(IAbilityBonusTargettedCharacterEffect)) &&
                ((IAbilityBonusTargettedCharacterEffect)e).AffectedAbility == ability &&
                ((IAbilityBonusTargettedCharacterEffect)e).EffectVector != EffectVectorEnum.Replace);

            foreach (var otherEffect in otherApplyingEffects)
            {
                if (((IAbilityBonusTargettedCharacterEffect)otherEffect).EffectVector == EffectVectorEnum.Positive)
                {
                    baseModifier += (int)otherEffect.Value;
                }
                else
                {
                    baseModifier -= (int)otherEffect.Value;
                }
            }

            return baseModifier;
        }
    }
}