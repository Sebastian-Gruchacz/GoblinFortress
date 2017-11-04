using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Popular music and personalities, genre films and books, urban legends, comics, science fiction, and gaming, among others.
    /// </summary>
    public class KnowledgePopularCulture : Knowledge
    {
        private readonly Guid _id = new Guid(@"{71FBB504-04F0-45D5-AE4D-20ECBE9C5287}");

        public KnowledgePopularCulture(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}