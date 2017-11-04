using ObscureWare.ModernD20.Builders;

namespace ObscureWare.ModernD20.Skills
{
    /// <summary>
    /// The fourteen Knowledge categories, and the topics each one encompasses are in derrived classes.
    /// </summary>
    public abstract class Knowledge : Skill
    {
        protected Knowledge(BaseSkillBuilder builder) : base(builder)
        {
        }
    }
}