using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// Law, legislation, litigation, and legal rights and obligations.Political and governmental institutions and processes.
    /// </summary>
    public class KnowledgeCivics : Knowledge
    {
        private readonly Guid _id = new Guid(@"{BB3D1445-59A9-4B39-9300-EA8BCCEC6884}");


        public KnowledgeCivics(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}