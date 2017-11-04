using System;
using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    public class CraftStructural : Craft
    {
        private readonly Guid _id = new Guid(@"{B1B5CADB-BE0C-46DD-8186-2D419F804A80}");

        public CraftStructural(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}