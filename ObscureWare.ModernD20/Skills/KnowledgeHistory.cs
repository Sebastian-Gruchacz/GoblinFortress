using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Events, personalities, and cultures of the past.Archaeology and antiquities.
    /// </summary>
    public class KnowledgeHistory : Knowledge
    {
        private readonly Guid _id = new Guid(@"{52ECAA76-5D60-4CB0-AB19-182FDD73DA4E}");

        public KnowledgeHistory(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}