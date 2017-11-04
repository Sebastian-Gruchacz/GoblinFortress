using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftPharmaceutical : Craft
    {
        private readonly Guid _id = new Guid(@"{3631467C-DC58-432F-86CA-A56AD92A46DB}");


        public CraftPharmaceutical(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}