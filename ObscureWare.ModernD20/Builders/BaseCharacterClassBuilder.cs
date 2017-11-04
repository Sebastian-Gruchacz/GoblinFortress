using System;
using System.Collections.Generic;
using System.Linq;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Descriptors;
using ObscureWare.ModernD20.Localization;

namespace ObscureWare.ModernD20.Builders
{
    public class BaseCharacterClassBuilder
    {
        private readonly BaseCharacterClassDefinition _def;
        private readonly ITranslatedResourceProvider _translatedResourceProvider;
        private readonly GlobalDefinitions _globals;

        public BaseCharacterClassBuilder(BaseCharacterClassDefinition def, ITranslatedResourceProvider translatedResourceProvider, GlobalDefinitions globals)
        {
            _def = def;
            _translatedResourceProvider = translatedResourceProvider;
            _globals = globals;
        }

        public Guid GetId()
        {
            return _def.Id;
        }

        public IEnumerable<Skill> GetClassSkillsIdentifiers()
        {
            return _def.ClassSkills.Select(id => _globals.FindSkill(id));
        }

        public ILocalizedDescriptor GetDescriptor()
        {
            return _translatedResourceProvider.GetDescriptor(CoreTranslationsGroup.BaseCharacterClass.ToString(), _def.Id);
        }

        public DieRollDescriptor GetHitDieDescriptor()
        {
            return new DieRollDescriptor(_def.HitDieDescription);
        }

        public IEnumerable<Feat> GetStartingFeats()
        {
            return _def.StartingFeats.Select(id => _globals.FindFeat(id));
        }

        public IEnumerable<BaseCharacterClassTableRow> GetClassTableRows()
        {
            return _def.BaseCharacterClassTableRows;
        }

        public IEnumerable<ClassFeature> GetClassFeatures()
        {
            return _def.ClassFeatures.Select(id => _globals.FindClassFeature(id));
        }

        public AbilityEnum GetGoverningAbility()
        {
            return _def.GoverningAbility;
        }

        public IEnumerable<Guid> GetAvailableTalentTrees()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Feat> GetAvailableBonusFeats()
        {
            return _def.BonusFeats.Select(id => _globals.FindFeat(id));
        }
    }
}