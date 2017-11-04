using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Psychology, sociology, and criminology.
    /// </summary>
    public class KnowledgeBehavioralSciences : Knowledge
    {
        private readonly Guid _id = new Guid(@"{0969AE3D-B8C8-488C-BFB2-A2C0AE95A291}");

        public KnowledgeBehavioralSciences(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}