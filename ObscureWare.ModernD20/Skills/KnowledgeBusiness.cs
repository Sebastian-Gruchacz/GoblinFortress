using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Business procedures, investment strategies, and corporate structures.Bureaucratic procedures and how to navigate them.
    /// </summary>
    public class KnowledgeBusiness : Knowledge
    {
        private readonly Guid _id = new Guid(@"{71777E21-FB06-4293-92BE-D1792CC93D86}");


        public KnowledgeBusiness(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}