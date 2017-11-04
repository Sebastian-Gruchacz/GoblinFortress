using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class Profession : Skill
    {
        private readonly Guid _id = new Guid(@"{13C03AFF-EB0B-41AF-A9CD-72C380CC94D7}");

        public Profession(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}