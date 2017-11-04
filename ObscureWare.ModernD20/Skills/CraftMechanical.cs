using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftMechanical : Craft
    {
        private readonly Guid _id = new Guid(@"{20D29B4A-3DAB-446C-9E4F-CACB14661783}");


        public CraftMechanical(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}