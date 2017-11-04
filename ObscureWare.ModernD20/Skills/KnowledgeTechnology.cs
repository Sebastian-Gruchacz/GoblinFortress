using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Current developments in cutting-edge devices, as well as the background necessary to identify various technological devices.
    /// </summary>
    public class KnowledgeTechnology : Knowledge
    {
        private readonly Guid _id = new Guid(@"{5C8293FD-6215-4EF7-BDEC-2F53BA4D15EB}");

        public KnowledgeTechnology(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}