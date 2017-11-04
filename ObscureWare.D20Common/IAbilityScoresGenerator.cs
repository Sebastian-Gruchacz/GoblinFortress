using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.ModernD20;

namespace ObscureWare.D20Common
{
    public interface IAbilityScoresGenerator
    {
        IEnumerable<uint> GenerateScores(IRoller roller);
    }
}
