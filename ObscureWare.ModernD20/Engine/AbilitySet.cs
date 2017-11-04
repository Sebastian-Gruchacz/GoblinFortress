using System.Diagnostics;
using System.Linq;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20
{
    /// <summary>
    /// The Score of these Abilities ranges from 0 to infinity. A limit, if any, will be specified in the rules.
    /// 
    /// The normal human range is 3 to 18.
    /// 
    /// It is possible for a creature to have a score of "none". A score of "none" is not the same as a score of "0".
    /// A score of "none" means that the creature does not possess the ability at all. The modifier for a score of "none" is +0.
    /// 
    /// Keeping track of negative ability score points is never necessary. A character’s ability score can’t drop below 0. 
    /// </summary>
    public class AbilitySet
    {
        private readonly Character _character;
        private const int ABILITY_SCORE_ARRAY_LENGTH = 7; // position 0 - Control number for save
        private readonly uint?[] _scoreArray; // base score
        // TODO: add effects resulting on score, score modifiers?

        /// <summary>
        /// Default, dead, unanimated character :)
        /// </summary>
        public AbilitySet(Character character)
        {
            _character = character;
            _scoreArray = new uint?[ABILITY_SCORE_ARRAY_LENGTH];
        }

        public AbilitySet(Character character, uint?[] scoreArray)
        {
            _character = character;
            Debug.Assert(scoreArray.Length == ABILITY_SCORE_ARRAY_LENGTH);
            this._scoreArray = (uint?[])scoreArray.Clone();
        }

        public uint? Strength
        {
            get { return _character.Effects.ApplyEffectsToAbilityScore(AbilityEnum.Strength, _scoreArray[(int)AbilityEnum.Strength]); }
        }

        public uint? Dexterity
        {
            get { return _character.Effects.ApplyEffectsToAbilityScore(AbilityEnum.Dexterity, _scoreArray[(int)AbilityEnum.Dexterity]); }
        }

        /// <summary>
        /// A character with a CON of 0 is dead.
        /// </summary>
        public uint? Constitution
        {
            get { return _character.Effects.ApplyEffectsToAbilityScore(AbilityEnum.Constitution, _scoreArray[(int)AbilityEnum.Constitution]); }
        }

        public uint? Intelligence
        {
            get { return _character.Effects.ApplyEffectsToAbilityScore(AbilityEnum.Intelligence, _scoreArray[(int)AbilityEnum.Intelligence]); }
        }

        public uint? Wisdom
        {
            get { return _character.Effects.ApplyEffectsToAbilityScore(AbilityEnum.Wisdom, _scoreArray[(int)AbilityEnum.Wisdom]); }
        }

        public uint? Charisma
        {
            get { return _character.Effects.ApplyEffectsToAbilityScore(AbilityEnum.Charisma, _scoreArray[(int)AbilityEnum.Charisma]); }
        }

        /// <summary>
        /// A 0 in any other score means the character is helpless and cannot move. Or is also Dead if CON = 0
        /// </summary>
        /// <returns></returns>
        public bool CanMove()
        {
            return _scoreArray.Any(score => score != null && score.Value > 0);
        }

        /// <summary>
        /// A character with a CON of 0 is dead.
        /// </summary>
        /// <returns></returns>
        public bool IsAlive()
        {
            return Constitution == null // TODO: Robots?
                   || Constitution > 0;
        }

        /// <summary>
        /// Each ability will have a modifier. The modifier can be calculated using this formula: 
        /// (ability/2) -5 [round result down]
        /// The modifier is the number you add to or subtract from the die roll when your character tries to do something related to that ability.
        /// A positive modifier is called a bonus, and a negative modifier is called a penalty.
        /// </summary>
        /// <param name="ability"></param>
        /// <returns></returns>
        /// <remarks>CORE modifier does not take into account temporary modifiers, it's usefull for character advancement routines.</remarks>
        public int GetCoreAbilityModifier(AbilityEnum ability)
        {
            // even cybernetics only give effects, though "permanent")
            return GlobalOperators.Round((_scoreArray[(int) ability] ?? 0) / 2m - 5m);
        }

        /// <summary>
        /// Each ability will have a modifier. The modifier can be calculated using this formula: 
        /// (ability/2) -5 [round result down]
        /// The modifier is the number you add to or subtract from the die roll when your character tries to do something related to that ability.
        /// A positive modifier is called a bonus, and a negative modifier is called a penalty.
        /// </summary>
        /// <param name="ability"></param>
        /// <returns></returns>
        public int GetAbilityModifier(AbilityEnum ability)
        {
            // TODO: take into account also temporary effects
            return _character.Effects.ApplyEffectsToAbilityBaseModifier(ability, GlobalOperators.Round((_scoreArray[(int)ability] ?? 0) / 2m - 5m));
        }

        //public AbilitySet Clone()
        //{
        //    return new AbilitySet(_scoreArray);
        //}
    }
}