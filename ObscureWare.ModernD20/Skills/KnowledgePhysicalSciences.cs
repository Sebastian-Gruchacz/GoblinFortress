using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Astronomy, chemistry, mathematics, physics, and engineering.
    /// </summary>
    public class KnowledgePhysicalSciences : Knowledge
    {
        private readonly Guid _id = new Guid(@"{C9075AB4-984F-4BAC-83A1-87147D1D8068}");

        public KnowledgePhysicalSciences(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}