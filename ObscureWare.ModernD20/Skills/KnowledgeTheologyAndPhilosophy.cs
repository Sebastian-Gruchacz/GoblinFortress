using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Liberal arts, ethics, philosophical concepts, and the study of religious faith, practice, and experience.
    /// </summary>
    public class KnowledgeTheologyAndPhilosophy : Knowledge
    {
        private readonly Guid _id = new Guid(@"{FC595355-B53F-4C52-9997-6671DC0FA536}");

        public KnowledgeTheologyAndPhilosophy(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}