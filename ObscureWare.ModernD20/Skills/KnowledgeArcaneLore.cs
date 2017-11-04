using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// The occult, magic and the supernatural, astrology, numerology, and similar topics.
    /// </summary>
    public class KnowledgeArcaneLore : Knowledge
    {
        private readonly Guid _id = new Guid(@"{589B5AFE-B2AE-4CCA-9EB8-F098FBE8025C}");

        public KnowledgeArcaneLore(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}