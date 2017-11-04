using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Biology, botany, genetics, geology, and paleontology. Medicine and forensics.
    /// </summary>
    public class KnowledgeEarthAndLifeSciences : Knowledge
    {
        private readonly Guid _id = new Guid(@"{ABAA0D6F-E938-42AC-8087-53B7A95F47FB}");

        public KnowledgeEarthAndLifeSciences(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}