using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftElectronic : Craft
    {
        private readonly Guid _id = new Guid(@"{A023B2D5-B7B6-47D7-AC47-29C241CF1A6A}");

        public CraftElectronic(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}