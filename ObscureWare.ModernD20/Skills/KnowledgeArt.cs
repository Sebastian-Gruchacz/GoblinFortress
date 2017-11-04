using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Fine arts and graphic arts, including art history and artistic techniques.Antiques, modern art, photography, and performance art forms such as music and dance, among others.
    /// </summary>
    public class KnowledgeArt : Knowledge
    {
        private readonly Guid _id = new Guid(@"{DF4C214D-AC13-45BB-A10B-C0BC201253EE}");

        public KnowledgeArt(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}