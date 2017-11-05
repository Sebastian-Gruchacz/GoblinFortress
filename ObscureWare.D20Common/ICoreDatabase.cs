using System;
using System.Collections.Generic;
using LiteDB;
using ObscureWare.ModernD20.Resources;

namespace ObscureWare.D20Common
{
    public interface ICoreDatabase
    {
        IEnumerable<GameLanguage> GetGameLanguages(string name);

        Version GetLibraryVersion(string name, Version currentLibraryVersion);
    }

    public interface ICoreDatabaseUpdate
    {
        void UpdateGameLanguages(string name, IEnumerable<GameLanguage> languages);
    }

    public interface ICoreDatabaseEdit : ICoreDatabase, ICoreDatabaseUpdate
    {
        
    }

    public abstract class BaseDbConnect : ICoreDatabaseEdit, IDisposable
    {
        private readonly string _dbPath;
        private LiteDatabase _db;

        protected BaseDbConnect(string dbPath)
        {
            if (String.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("Argument is null or whitespace", nameof(dbPath));

            this._dbPath = dbPath;
            this._db = new LiteDatabase(this._dbPath);
        }

        public void Dispose()
        {
            this._db?.Dispose();
        }

        public string DbPath
        {
            get { return this._dbPath; }
        }

        public Version GetLibraryVersion(string libraryName, Version currentLibraryVersion)
        {
            var versioning = this._db.GetCollection<VersionInfo>(@"VERSIONS");
            VersionInfo v = versioning.FindOne(version => version.Target == libraryName);
            if (v == null)
            {
                v = new VersionInfo(libraryName, currentLibraryVersion.ToString());
                versioning.Insert(v);
            }
            return Version.Parse(v.Version);
        }

        public IEnumerable<GameLanguage> GetGameLanguages(string libraryName)
        {
            return this._db.GetCollection<GameLanguage>(typeof(GameLanguage).Name + "_" + libraryName).FindAll();
        }

        public void UpdateGameLanguages(string libraryName, IEnumerable<GameLanguage> languages)
        {
            var collection = this._db.GetCollection<GameLanguage>(typeof(GameLanguage).Name + "_" + libraryName);
            foreach (var lang in collection.FindAll())
            {
                collection.Delete((gl) => gl.Id == lang.Id);
            }

            collection.Insert(languages);
        }
    }
}