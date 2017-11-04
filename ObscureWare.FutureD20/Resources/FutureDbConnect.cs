using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.D20Common;

namespace ObscureWare.FutureD20.Resources
{
    public class FutureDbConnect : BaseDbConnect, IFutureDatabase
    {
        public FutureDbConnect(string dbPath) : base(dbPath)
        {
            
        }
    }
}
