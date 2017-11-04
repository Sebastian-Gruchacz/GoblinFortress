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
            _resourceProvider = resourceProvider;
            _nationData = resourceProvider.GetNationInfo(this.Id);
            _maleNames = new Lazy<List<string>>(() => _resourceProvider.GetStringTable($"Names.{NationResourceName}.Male").ToList());
            _femaleNames = new Lazy<List<string>>(() => _resourceProvider.GetStringTable($"Names.{NationResourceName}.Female").ToList());
            _surnames = new Lazy<List<string>>(() => _resourceProvider.GetStringTable($"Surnames.{NationResourceName}").ToList());
        }

        public string NationResourceName
        {
            get { return _nationData.NationResourceName; }
        }

        public abstract Guid Id { get; }

        public virtual string GetRandomName(Gender gender, IRoller roller)
        {
            switch (gender)
            {
                case Gender.Male:
                    return _maleNames.Value[roller.CoreGenerator.NextInt(0, _maleNames.Value.Count - 1)];
                case Gender.Female:
                    return _femaleNames.Value[roller.CoreGenerator.NextInt(0, _femaleNames.Value.Count - 1)];
                default:
                    throw new ArgumentOutOfRangeException(nameof(gender), gender, null);
            }
        }

        public virtual string GetRandomSurname(Gender gender, IRoller roller)
        {
            // both female & male surnames are written same way - no sex-declination
            return _surnames.Value[roller.CoreGenerator.NextInt(0, _surnames.Value.Count - 1)];
        }

        public Guid GetNationMainLanguage()
        {
            return _nationData.MainLanguageId;
        }

        public IEnumerable<Guid> GetNationOfficialLanguages()
        {
            return _nationData.OfficialLanguages;
        }
    }
}
