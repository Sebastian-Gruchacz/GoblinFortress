using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class Climb : Skill
    {
        private readonly Guid _id = new Guid(@"{04A56EAD-4734-4CBD-97E5-2598B7AB2C5B}");

        public Climb(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}
