using System;
using System.Collections.Generic;
using System.Linq;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Builders;
using ObscureWare.ModernD20.Descriptors;
using ObscureWare.ModernD20.Engine;
using ObscureWare.ModernD20.Localization;

namespace ObscureWare.ModernD20.BaseCharacterClasses
{
    public abstract class BaseCharacterClass
    {
        protected BaseCharacterClass(BaseCharacterClassBuilder builder)
        {
            Id = builder.GetId();
            GoverningAbility = builder.GetGoverningAbility();
            ClassSkills = builder.GetClassSkillsIdentifiers().ToList();
            Descriptor = builder.GetDescriptor();
            HitDie = builder.GetHitDieDescriptor();
            StartingFeats = builder.GetStartingFeats().ToList();
            ClassTable = builder.GetClassTableRows().ToList();
            AvailableClassFeatures = builder.GetClassFeatures().ToList();
            AvailableTalents = new List<Guid>(builder.GetAvailableTalentTrees());
            AvailableBonusFeats = builder.GetAvailableBonusFeats().ToList();
        }

        /// <summary>
        /// Base Character Class Identifier
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Advanced classes are available only to Heroes
        /// </summary>
        public abstract bool IsAdvanced { get; }

        /// <summary>
        /// Description used on UI
        /// </summary>
        public ILocalizedDescriptor Descriptor { get; private set; }

        /// This entry tells which ability is typically associated with that class.
        public AbilityEnum GoverningAbility { get; private set; }

        /// <summary>
        /// The die type used by characters of the class to determine the number of hit points gained per level.
        /// A player rolls one die of the given type each time his or her character gains a new level. The character’s Constitution modifier is applied to the roll.
        /// Add the result to the character’s hit point total. Even if the result is 0 or lower, the character always gains at least 1 hit point.
        /// A 1st-level character gets the maximum hit points rather than rolling (although the Constitution modifier is still applied).
        /// </summary>
        public DieRollDescriptor HitDie { get; private set; }

        /// <summary>
        /// The number of action points gained per level.
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        public virtual uint GetNextLevelActionPointsGained(Character character)
        {
            // Strong heroes gain a number of action points equal to 5 + one-half their character level, rounded down, at 1st level and every time they attain a new level in this class.

            return 5 + (uint)Math.Max(0, GlobalOperators.Round(character.Level / 2m));
        }

        /// <summary>
        /// This section of a class description provides a list of class skills and also gives the number of skill points the character starts with at 1st level
        /// and the number of skill points gained each level thereafter.
        /// The maximum ranks a character can have in a class skill is the character’s level +3.
        /// A character can also buy skills from other classes’ skill lists.Each skill point buys a half rank in these cross-class skills,
        /// and a character can only buy up to half the maximum ranks of a class skill.
        /// </summary>

        /// <summary>
        /// List of skill identifiers the character can choose from.
        /// </summary>
        public IReadOnlyList<Skill> ClassSkills { get; }

        /// <summary>
        /// In General - a 1st-level character starts with 4 times the number of skill points he or she receives upon attaining each level beyond 1st.
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        public virtual uint GetFirstLevelSkillPointsGained(Character character)
        {
            return GetNextLevelSkillPointsGained(character) * 4;
        }

        /// A character’s Intelligence modifier is applied to determine the total skill points gained each level 
        /// (but always at least 1 point per level, even for a character with an Intelligence penalty).
        public abstract uint GetNextLevelSkillPointsGained(Character character);

        /// <summary>
        /// The feats gained at 1st level in the class.
        /// </summary>
        public IReadOnlyList<Feat> StartingFeats { get; private set; }

        /// <summary>
        /// This table details how a character improves as he or she attains higher levels in the class.
        /// </summary>
        public IReadOnlyList<BaseCharacterClassTableRow> ClassTable { get; private set; }

        /// <summary>
        /// This entry details special characteristics of the class, including bonus feats and unique talents, that are gained as a character attains higher levels in the class.
        /// </summary>
        public IReadOnlyList<ClassFeature> AvailableClassFeatures { get; private set; }

        /// <summary>
        /// Every basic class offers a selection of talents to choose from. A character gains a talent upon attaining each odd-numbered level in a class (including 1st level).
        /// Talents are considered to be extraordinary abilities. Some talents have prerequisites that must be met before a character can select them.
        /// </summary>
        public IReadOnlyList<Guid> AvailableTalents { get; private set; }

        /// <summary>
        /// Every basic class offers a selection of bonus feats to choose from. A character gains a bonus feat upon attaining each even-numbered level in a class.
        /// These bonus feats are in addition to the feats that all characters receive as they attain new levels.
        /// Some feats have prerequisites that must be met before a character can select them.
        /// </summary>
        public IReadOnlyList<Feat> AvailableBonusFeats{ get; private set; }

        /// <summary>
        /// For NPC characters choosing which Ability Score shall be increased on every 4th level shall be determined by advanced class
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        /// <remarks>By default - GoverningAbility is being used</remarks>
        public virtual AbilityEnum SelectAutomatedAbilityAdvancement(Character character)
        {
            return this.GoverningAbility;
        }
    }
}
