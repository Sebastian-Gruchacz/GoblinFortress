using System.Collections.Generic;
using System.Linq;

namespace ObscureWare.D20Common.Generators
{
    /// <summary>
    /// Roll 3d6 and add the dice together. Record this total and repeat the process until you generate six numbers.
    /// Assign these results to your ability scores as you see fit.
    /// This method is quite random, and some characters will have clearly superior abilities.
    /// This randomness can be taken one step further, with the totals applied to specific ability scores in the order they are rolled.
    /// Characters generated using this method are difficult to fit to predetermined concepts, as their scores might not support given classes or personalities,
    ///  and instead are best designed around their ability scores.
    /// </summary>
    public sealed class ClassicAbilityScoresGenerator : BaseAbilityScoreGenerator
    {
        public override IEnumerable<uint> GenerateScores(IRoller roller)
        {
            for (uint i = 0; i < ScoresCount; ++i)
            {
                yield return (uint)roller.RollMany(DieEnum.D6, 3).Sum(score => score);
            }
        }
    }
}