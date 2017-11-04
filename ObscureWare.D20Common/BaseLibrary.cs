using System;
using System.Collections.Generic;
using System.Linq;

namespace ObscureWare.D20Common
{
    public abstract class BaseLibrary : ILibrary
    {
        protected readonly ICoreDatabase _db;
        private readonly string _libraryName;

        public abstract Version LibraryVersion { get; }

        protected BaseLibrary(ICoreDatabase dbConnect)
        {
            _db = dbConnect;
            _libraryName = this.GetType().Name;
        }

        public IList<GameLanguage> GameWorldLanguages
        {
            get { return _db.GetGameLanguages(_libraryName).OrderBy(l => l.Name).ToList(); }
        }

        public string LibraryName
        {
            get { return _libraryName; }
        }

        //public void UpdateGameWorldLanguages(IEnumerable<GameLanguage> languages)
        //{
        //    _db.UpdateGameLanguages(_libraryName, languages);
        //}

        public Version GetDbVersion()
        {
            return _db.GetLibraryVersion(_libraryName, this.LibraryVersion);
        }
    }
}