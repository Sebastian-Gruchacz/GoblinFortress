using System;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.FutureD20.Skills
{
    public class Repair : Skill
    {
        private readonly Guid _id = new Guid(@"{AA9807F8-53DD-4FAD-909B-137CE08DE330}");

        public Repair(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}