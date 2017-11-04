using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class Jump : Skill
    {
        private readonly Guid _id = new Guid(@"{ECE05425-2DCB-4F2B-A804-458F7E4AE95D}");

        public Jump(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}