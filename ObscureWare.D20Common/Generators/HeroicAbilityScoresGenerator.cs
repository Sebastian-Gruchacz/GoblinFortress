using System.Collections.Generic;
using System.Linq;

namespace ObscureWare.D20Common.Generators
{
    /// <summary>
    /// Roll 2d6 and add 6 to the sum of the dice.
    /// Record this total and repeat the process until six numbers are generated.
    /// Assign these totals to your ability scores as you see fit.
    /// This is less random than the Standard method and generates characters with mostly above-average scores.
    /// </summary>
    public sealed class HeroicAbilityScoresGenerator : BaseAbilityScoreGenerator
    {
        public override IEnumerable<uint> GenerateScores(IRoller roller)
        {
            for (uint i = 0; i < ScoresCount; ++i)
            {
                yield return (uint)roller.RollMany(DieEnum.D6, 2).Sum(score => score) + 6;
            }
        }
    }
}