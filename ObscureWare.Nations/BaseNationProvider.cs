using System;
using System.Collections.Generic;
using System.Linq;
using ObscureWare.D20Common;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.CharacterCreation;
using ObscureWare.ModernD20.Resources;

namespace ObscureWare.Nations
{
    public abstract class BaseNationProvider : INationProvider
    {
        private readonly INationResourceProvider _resourceProvider;

        protected readonly Lazy<List<string>> _maleNames;
        protected readonly Lazy<List<string>> _femaleNames;
        protected readonly Lazy<List<string>> _surnames;
        protected readonly NationInfoPOCO _nationData;

        protected BaseNationProvider(INationResourceProvider resourceProvider)
        {
            this._resourceProvider = resourceProvider;
            this._nationData = resourceProvider.GetNationInfo(this.Id);
            this._maleNames = new Lazy<List<string>>(() => this._resourceProvider.GetStringTable($"Names.{this.NationResourceName}.Male").ToList());
            this._femaleNames = new Lazy<List<string>>(() => this._resourceProvider.GetStringTable($"Names.{this.NationResourceName}.Female").ToList());
            this._surnames = new Lazy<List<string>>(() => this._resourceProvider.GetStringTable($"Surnames.{this.NationResourceName}").ToList());
        }

        public string NationResourceName
        {
            get { return this._nationData.NationResourceName; }
        }

        public abstract Guid Id { get; }

        public virtual string GetRandomName(Gender gender, IRoller roller)
        {
            switch (gender)
            {
                case Gender.Male:
                    return this._maleNames.Value[roller.CoreGenerator.NextInt(0, this._maleNames.Value.Count - 1)];
                case Gender.Female:
                    return this._femaleNames.Value[roller.CoreGenerator.NextInt(0, this._femaleNames.Value.Count - 1)];
                default:
                    throw new ArgumentOutOfRangeException(nameof(gender), gender, null);
            }
        }

        public virtual string GetRandomSurname(Gender gender, IRoller roller)
        {
            // both female & male surnames are written same way - no sex-declination
            return this._surnames.Value[roller.CoreGenerator.NextInt(0, this._surnames.Value.Count - 1)];
        }

        public Guid GetNationMainLanguage()
        {
            return this._nationData.MainLanguageId;
        }

        public IEnumerable<Guid> GetNationOfficialLanguages()
        {
            return this._nationData.OfficialLanguages;
        }
    }
}
