using System;
using System.Collections.Generic;

namespace ObscureWare.ModernD20
{
    public static class GlobalOperators
    {
        /// <summary>
        /// Default D20 rounding.
        /// </summary>
        /// <param name="valueToRound"></param>
        /// <returns></returns>
        public static int Round(decimal valueToRound)
        {
            return (int)Math.Floor(valueToRound);
        }

        /// <summary>
        /// Sometimes a special rule makes you multiply a number or a die roll. As long as you’re applying a single multiplier, multiply the number normally.
        /// When two or more multipliers apply, however, combine them into a single multiple, with each extra multiple adding 1 less than its value to the first multiple.
        /// Thus, a double (x2) and a double (x2) applied to the same number results in a triple (x3, because 2 + 1 = 3).
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        public static int ComplexMultiplier(params uint[] values)
        {
            int baseMultiplier = (int)values[0];

            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] > 0)
                {
                    baseMultiplier += (int)values[i] - 1;
                }
            }

            return baseMultiplier;
        }

        ///// <summary>
        ///// Choose max
        ///// </summary>
        ///// <param name="elements"></param>
        ///// <returns></returns>
        //public static uint Max(IEnumerable<uint> elements)
        //{
        //    uint max = uint.MinValue;
        //    foreach (uint element in elements)
        //    {
        //        if (element > max)
        //            max = element;
        //    }

        //    return max;
        //}
    }
}
