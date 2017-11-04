using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class Swim : Skill
    {
        private readonly Guid _id = new Guid(@"{5EE6ECAD-4DFA-44BA-B435-4EF61A236376}");

        public Swim(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}