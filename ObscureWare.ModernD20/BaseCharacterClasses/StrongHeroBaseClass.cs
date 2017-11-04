using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Builders;
using ObscureWare.ModernD20.Descriptors;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.BaseCharacterClasses
{
    public class StrongHeroBaseClass : BaseCharacterClass
    {
        public StrongHeroBaseClass(BaseCharacterClassBuilder builder) : base(builder)
        {
            // TODO: languages will be done separately (if at all)
        }

        public override bool IsAdvanced { get { return false; } }

        /// <summary>
        /// 3 + Int modifier.
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        public override uint GetNextLevelSkillPointsGained(Character character)
        {
            return (uint) (3 + character.Abilities.GetCoreAbilityModifier(AbilityEnum.Intelligence));
        }
    }
}
