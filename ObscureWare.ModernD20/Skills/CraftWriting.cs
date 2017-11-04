using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftWriting : Craft
    {
        private readonly Guid _id = new Guid(@"{FEA57AA9-FAA3-4B1C-BB0B-BF31518CF7F3}");

        public CraftWriting(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}