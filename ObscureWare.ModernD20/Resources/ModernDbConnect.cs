using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteDB;
using ObscureWare.D20Common;

namespace ObscureWare.ModernD20.Resources
{
    // http://www.codeproject.com/Articles/869219/LiteDB-A-NET-NoSQL-Document-Store-in-a-single-data

    public class ModernDbConnect : BaseDbConnect, IModernDatabase
    {
        public ModernDbConnect(string dbPath) : base(dbPath)
        {
            
        }
        
    }
}
