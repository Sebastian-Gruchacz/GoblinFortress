using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Techniques and strategies for disposing and maneuvering forces in combat.
    /// </summary>
    public class KnowledgeTactics : Knowledge
    {
        private readonly Guid _id = new Guid(@"{731FCC23-15DD-4AD3-B4B0-EB450C8F8506}");


        public KnowledgeTactics(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}