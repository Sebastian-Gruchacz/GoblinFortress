﻿using System;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;
using ObscureWare.ModernD20.Logic;
using ObscureWare.ModernD20.Resources;

namespace ObscureWare.ModernD20
{
    /// <summary>
    /// This class is now only to recognize Assembly. Later it might contain more information and interfaces.
    /// </summary>
    public class ModernD20Library : BaseLibrary
    {
        private readonly IModernDatabase _database;

        public ICoreNotifications CoreNotifications { get; private set; }

        public static Version LibVersion = new Version(0, 0, 1, 0);

        public ModernD20Library(ICoreNotifications coreNotifications, IModernDatabase database) : base(database)
        {
            _database = database;
            CoreNotifications = coreNotifications;
            GlobalState = new GlobalState(this);

            FightRoller = new DefaultRoller(new DefaultRandomizer()); // TODO: expand rollers tree -> or move to global state
            GlobalDefinitions = new GlobalDefinitions(_database);
            GlobalDefinitions.RegisterLibrary(this.GetType().Assembly);

            SavingThrowsLogic = new SavingThrowsLogic(this);
            ActionPointsLogic = new ActionPointsLogic(this);

            RestorationLogic = new RestorationLogic(this);
            // DamageLogic = new DamageLogic(this);
        }

        public RestorationLogic RestorationLogic { get; private set; }

        public GlobalState GlobalState { get; private set; }

        public IRoller FightRoller { get; private set; }

        public DamageLogic DamageLogic { get; }

        public SavingThrowsLogic SavingThrowsLogic { get; }

        public ActionPointsLogic ActionPointsLogic { get; private set; }

        public GlobalDefinitions GlobalDefinitions { get; private set; }

        public IModernDatabase Db
        {
            get { return _database; }
        }

        

        public bool UpgradeDB()
        {
            throw new NotImplementedException();
        }


        public override Version LibraryVersion { get { return LibVersion; } }
    }
}
