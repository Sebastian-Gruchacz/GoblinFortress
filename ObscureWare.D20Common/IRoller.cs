using System.Collections.Generic;
using ObscureWare.ModernD20;

namespace ObscureWare.D20Common
{
    public interface IRoller
    {
        /// <summary>
        /// Rolls one specific dice and returns result
        /// </summary>
        /// <param name="dice"></param>
        /// <returns></returns>
        uint Roll(DieEnum dice);

        /// <summary>
        /// Rolls Dices from descriptor and return total result (with eventuall modifiers)
        /// </summary>
        /// <param name="descriptor"></param>
        /// <returns></returns>
        uint Roll(DieRollDescriptor descriptor);

        /// <summary>
        /// Rolls specifdic amount of one dice type and returns all results
        /// </summary>
        /// <param name="dice"></param>
        /// <param name="diceCount"></param>
        /// <returns></returns>
        IEnumerable<uint> RollMany(DieEnum dice, uint diceCount);

        /// <summary>
        /// Get direct access to underlying Random Number Generator for nonm-rolled random numbers
        /// </summary>
        IRandomizer CoreGenerator { get; }
    }
}