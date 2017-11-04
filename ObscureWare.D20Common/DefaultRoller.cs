using System;
using System.Collections.Generic;
using ObscureWare.ModernD20;

namespace ObscureWare.D20Common
{
    public class DefaultRoller : IRoller
    {
        private readonly IRandomizer _rnd;

        public DefaultRoller(IRandomizer rnd)
        {
            _rnd = rnd;
        }

        public uint Roll(DieEnum dice)
        {
            return (uint)_rnd.NextInt(1, (int) dice);
        }

        public uint Roll(DieRollDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<uint> RollMany(DieEnum dice, uint diceCount)
        {
            for (int i = 0; i < diceCount; i++)
            {
                yield return Roll(dice);
            }
        }

        public IRandomizer CoreGenerator { get { return _rnd; } }
    }
}