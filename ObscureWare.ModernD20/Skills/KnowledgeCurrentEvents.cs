using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Recent happenings in the news, sports, politics, entertainment, and foreign affairs.
    /// </summary>
    public class KnowledgeCurrentEvents : Knowledge
    {
        private readonly Guid _id = new Guid(@"{773CA016-C06F-4308-B42F-C2753F82908E}");

        public KnowledgeCurrentEvents(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}