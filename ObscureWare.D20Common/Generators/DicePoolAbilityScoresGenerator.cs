using System.Collections.Generic;
using System.Linq;
using ObscureWare.Common;

namespace ObscureWare.D20Common.Generators
{
    /// <summary>
    /// Dice Pool: Each character has a pool of 24d6 to assign to his statistics.
    /// Before the dice are rolled, the player selects the number of dice to roll for each score, with a minimum of 3d6 for each ability.
    /// Once the dice have been assigned, the player rolls each group and totals the result of the three highest dice.
    /// For more high-powered games, the GM should increase the total number of dice to 28.
    /// This method generates characters of a similar power to the Standard method.</summary>
    public sealed class DicePoolAbilityScoresGenerator : BaseAbilityScoreGenerator
    {
        private readonly uint _poolSize;

        public DicePoolAbilityScoresGenerator(bool useHeroicPoolSize)
        {
            this._poolSize = useHeroicPoolSize ? 28u : 24u;
        }

        public override IEnumerable<uint> GenerateScores(IRoller roller)
        {
            uint[] numberOfDicesPerScore = new uint[ScoresCount];
            numberOfDicesPerScore.Fill(3u);

            for (uint i = this._poolSize - (3*ScoresCount); i <= this._poolSize; ++i)
            {
                numberOfDicesPerScore[roller.CoreGenerator.NextInt(0, (int) ScoresCount - 1)] += 1;
            }

            for (uint i = 0; i < ScoresCount; ++i)
            {
                yield return (uint)roller.RollMany(DieEnum.D6, numberOfDicesPerScore[i]).OrderByDescending(score => score).Take(3).Sum(score => score);
            }
        }
    }
}