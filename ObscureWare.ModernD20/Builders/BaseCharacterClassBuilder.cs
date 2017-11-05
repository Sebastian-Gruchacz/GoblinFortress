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
            this._def = def;
            this._translatedResourceProvider = translatedResourceProvider;
            this._globals = globals;
        }

        public Guid GetId()
        {
            return this._def.Id;
        }

        public IEnumerable<Skill> GetClassSkillsIdentifiers()
        {
            return this._def.ClassSkills.Select(id => this._globals.FindSkill(id));
        }

        public ILocalizedDescriptor GetDescriptor()
        {
            return this._translatedResourceProvider.GetDescriptor(CoreTranslationsGroup.BaseCharacterClass.ToString(), this._def.Id);
        }

        public DieRollDescriptor GetHitDieDescriptor()
        {
            return new DieRollDescriptor(this._def.HitDieDescription);
        }

        public IEnumerable<Feat> GetStartingFeats()
        {
            return this._def.StartingFeats.Select(id => this._globals.FindFeat(id));
        }

        public IEnumerable<BaseCharacterClassTableRow> GetClassTableRows()
        {
            return this._def.BaseCharacterClassTableRows;
        }

        public IEnumerable<ClassFeature> GetClassFeatures()
        {
            return this._def.ClassFeatures.Select(id => this._globals.FindClassFeature(id));
        }

        public AbilityEnum GetGoverningAbility()
        {
            return this._def.GoverningAbility;
        }

        public IEnumerable<Guid> GetAvailableTalentTrees()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Feat> GetAvailableBonusFeats()
        {
            return this._def.BonusFeats.Select(id => this._globals.FindFeat(id));
        }
    }
}