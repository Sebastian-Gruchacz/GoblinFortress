using System;
using System.Collections.Generic;

namespace ObscureWare.D20Common.Generators
{
    /// <summary>
    /// Each character has six ability scores that represent his character's most basic attributes.
    /// They are his raw talent and prowess.
    /// While a character rarely rolls a check using just an ability score, these scores, and the modifiers they create,
    ///  affect nearly every aspect of a character's skills and abilities.
    /// Each ability score generally ranges from 3 to 18, although racial bonuses and penalties can alter this; an average ability score is 10.
    /// </summary>
    /// <remarks>Racial modifiers (adjustments made to your ability scores due to your character's race) are applied after the scores are generated.</remarks>
    public abstract class BaseAbilityScoreGenerator : IAbilityScoresGenerator
    {
        protected static readonly uint ScoresCount = (uint)Enum.GetValues(typeof(AbilityEnum)).Length;

        public abstract IEnumerable<uint> GenerateScores(IRoller roller);
    }


    // The last method does not apply to NPC characters - it requires manual operation of player that can hardly be randomized, so is reserved for himself.

    // Purchase: Each character receives a number of points to spend on increasing his basic attributes.
    // In this method, all attributes start at a base of 10.
    // A character can increase an individual score by spending some of his points.
    // Likewise, he can gain more points to spend on other scores by decreasing one or more of his ability scores.
    // No score can be reduced below 7 or raised above 18 using this method.
    // See Table: Ability Score Costs for the costs of each score.
    // After all the points are spent, apply any racial modifiers the character might have.

    /*
    
    Table: Ability Score Costs
    Score	Points
    7	    –4
    8	    –2
    9	    –1
    10	    0
    11	    1
    12	    2
    13	    3
    14	    5
    15	    7
    16	    10
    17	    13
    18	    17

    Table: Ability Score Points
    Campaign Type       Points
    Low Fantasy         10
    Standard Fantasy	15
    High Fantasy	    20
    Epic Fantasy	    25

    */
}
