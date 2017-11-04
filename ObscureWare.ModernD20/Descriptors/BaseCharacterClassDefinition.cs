using System;
using System.Collections.Generic;
using ObscureWare.D20Common;

namespace ObscureWare.ModernD20.Descriptors
{
    [Serializable]
    public class BaseCharacterClassDefinition
    {
        public Guid Id { get; set; }
        public string ImplementationClassName { get; set; }
        public AbilityEnum GoverningAbility { get; set; }

        public string HitDieDescription { get; set; }
        public List<Guid> ClassSkills { get; set; }
        public List<Guid> StartingFeats { get; set; }
        public List<BaseCharacterClassTableRow> BaseCharacterClassTableRows { get; set; }
        public List<Guid> BonusFeats { get; set; }
        public List<Guid> ClassFeatures { get; set; }
    }
}