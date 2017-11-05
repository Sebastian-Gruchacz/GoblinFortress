using System.Linq;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.Logic
{
    /// <summary>
    /// Action points provide characters with the means to affect game play in significant ways.
    /// A character always has a limited amount of action points, and while the character replenishes this supply with every new level he or she attains,
    /// the character must use them wisely. A character can spend 1 action point to do one of these things:
    ///     - Alter a single d20 roll used to make an attack, a skill check, an ability check, a level check, or a saving throw.
    ///     - Use a class talent or class feature during your turn for which the expenditure of 1 action point is required.
    /// 
    /// When a character spends 1 action point to improve a d20 roll, add 1d6 to the d20 roll to help meet or exceed the target number.
    /// A character can declare the use of 1 action point to alter a d20 roll after the roll is made—but only before the GM reveals the result of that roll
    /// (whether the attack or check or saving throw suc­ceeded or failed).
    ///  A character can’t use an action point on a skill check or ability check when he or she is taking 10 or taking 20.
    /// 
    /// When a character spends 1 action point to use a class feature, he or she gains the benefit of the feature but doesn’t roll a d6.In this case,
    ///  the action point is not a bonus to a d20 roll.
    /// 
    /// A character can only spend 1 action point in a round. If a character spends a point to use a class feature,
    ///  he or she can’t spend another one in the same round to improve a die roll, and vice versa.
    /// 

    /// </summary>
    /// <remarks>SG: ANyway, most of this logic must be done much higher in game engine, especially because player input is required.
    /// On the other hand - I can't expect to ask player each round whether he or she would like to spend AP... Gone design something else.
    /// And another part are NPCs and monsters...</remarks>
    public class ActionPointsLogic : LogicCore
    {
        public ActionPointsLogic(ModernD20Library library) : base(library)
        {
        }

        /// <summary>
        /// Depending on the hero’s character level (see the table below), he or she may be able to roll more than one d6 when spending 1 action point.
        /// If the character does so, apply the highest result and disregard the other rolls.
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        public uint GetD6RollsAvailbleForActionPointRoll(Character character)
        {
            if (character.Level <= 7)
                return 1;
            else if (character.Level <= 14)
                return 2;
            else
                return 3;
        }

        public uint GetD6ActionPointsRoll(Character character, IRoller roller)
        {
            uint rollsNumber = this.GetD6RollsAvailbleForActionPointRoll(character);
            if (rollsNumber == 1)
            {
                return roller.Roll(DieEnum.D6);
            }

            return roller.RollMany(DieEnum.D6, rollsNumber).Max();
        }
    }
}
