using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class HandleAnimal : Skill
    {
        private readonly Guid _id = new Guid(@"{169C35E5-14F9-4D5C-96AC-08F46621476A}");

        public HandleAnimal(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}