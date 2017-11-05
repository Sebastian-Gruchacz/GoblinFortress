using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.D20Common;
using ObscureWare.FutureD20.Resources;
using ObscureWare.ModernD20;

namespace ObscureWare.FutureD20
{
    /// <summary>
    /// This class is now only to recognize Assembly. Later it might contain more information and interfaces.
    /// </summary>
    public class FutureD20Library : BaseLibrary
    {
        private readonly ModernD20Library _moderLogicRef;
        private readonly IFutureDatabase _futureDb;
        private IList<GameLanguage> _wordLanguages;

        public static Version LibVersion = new Version(0, 0, 1, 0);

        public override Version LibraryVersion { get { return LibVersion; } }

        public FutureD20Library(ModernD20Library moderLogicRef, IFutureDatabase futureDb) : base (futureDb)
        {
            this._moderLogicRef = moderLogicRef;
            this._futureDb = futureDb;
            this._moderLogicRef.GlobalDefinitions.RegisterLibrary(this.GetType().Assembly);
        }

        /// <summary>
        /// For now, later it might be some decorator class over it
        /// </summary>
        public GlobalDefinitions GlobalDefinitions { get { return this._moderLogicRef.GlobalDefinitions; } }



        // TODO: other subsystems - overloaded or not 
        public bool UpgradeDB()
        {
            throw new NotImplementedException();
        }

        public IList<GameLanguage> WordLanguages
        {
            get { return this._wordLanguages; }
        }
    }
}
