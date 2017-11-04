using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftVisualArts : Craft
    {
        private readonly Guid _id = new Guid(@"{2728F36F-9ECE-40D8-90B8-852D2331D82B}");


        public CraftVisualArts(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}