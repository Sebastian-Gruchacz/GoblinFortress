using System.Collections.Generic;
using System.Linq;

namespace ObscureWare.D20Common.Generators
{
    /// <summary>
    /// Roll 4d6, discard the lowest die result, and add the three remaining results together.
    /// Record this total and repeat the process until six numbers are generated.
    /// Assign these totals to your ability scores as you see fit.
    /// This method is less random than Classic and tends to create characters with above-average ability scores.
    /// </summary>
    public sealed class StandardAbilityScoresGenerator : BaseAbilityScoreGenerator
    {
        public override IEnumerable<uint> GenerateScores(IRoller roller)
        {
            for (uint i = 0; i < ScoresCount; ++i)
            {
                yield return (uint)roller.RollMany(DieEnum.D6, 4).OrderBy(score => score).Skip(1).Sum(score => score);
            }
        }
    }
}