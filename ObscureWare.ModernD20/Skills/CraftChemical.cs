using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftChemical : Craft
    {
        private readonly Guid _id = new Guid(@"{709F3969-3EA2-4B81-9805-59259612FE6E}");

        public CraftChemical(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}