using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20
{
    public abstract class Skill
    {
        protected Skill(BaseSkillBuilder builder)
        {
            this.KeyAbility = builder.GetGoverningAttribute();
            this.Id = builder.GetId();
            this.TrainedOnly = builder.GetTrainedOnly();
        }

        public Guid Id { get; }

        public AbilityEnum KeyAbility { get; }

        public bool TrainedOnly { get; }

        public virtual bool CanTryAgain()
        {
            return true;
        }
    }
}
