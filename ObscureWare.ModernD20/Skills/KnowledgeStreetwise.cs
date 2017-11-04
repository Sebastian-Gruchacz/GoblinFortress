using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Street and urban culture, local underworld personalities and events.
    /// </summary>
    public class KnowledgeStreetwise : Knowledge
    {
        private readonly Guid _id = new Guid(@"{FE4A45BB-8418-4D1C-A760-E824546D3129}");

        public KnowledgeStreetwise(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}