using System;
using ObscureWare.ModernD20;

namespace ObscureWare.D20Common
{
    public class DefaultRandomizer : IRandomizer
    {
        private readonly Random _rnd;

        public DefaultRandomizer()
        {
            _rnd = new Random();
        }

        public DefaultRandomizer(int baseSeed)
        {
            _rnd = new Random(baseSeed);
        }

        public int NextInt(int minInclusiveValue, int maxInclusiveValue)
        {
            return _rnd.Next(minInclusiveValue, (int)Math.Min(Int32.MaxValue, (long)maxInclusiveValue + 1));
        }
    }
}