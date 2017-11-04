using System;

namespace ObscureWare.ModernD20.Descriptors
{
    [Serializable]
    public class BaseCharacterClassTableRow
    {
        /// <summary>
        /// The character’s level in the class.
        /// </summary>
        public uint ClassLevel { get; set; }

        /// <summary>
        /// The character’s base attack bonus and number of attacks.
        /// </summary>
        public uint BaseAttackBonus { get; set; }

        /// <summary>
        /// The base save bonus for Fortitude saving throws. The character’s Constitution modifier also applies.
        /// </summary>
        public uint FortitudeSave { get; set; }

        /// <summary>
        /// The base save bonus for Reflex saving throws. The character’s Dexterity modifier also applies.
        /// </summary>
        public uint ReflexSave { get; set; }

        /// <summary>
        /// The base save bonus for Will saving throws. The character’s Wisdom modifier also applies.
        /// </summary>
        public uint WillSave { get; set; }

        /// <summary>
        /// Level-dependent class features, each explained in the section that follows.
        /// </summary>
        public BaseClassFeatureSelectEnum ClassFeatures { get; set; }

        /// <summary>
        /// The character’s bonus to Defense. The character’s Dexterity modifier and equipment bonus also applies.
        /// </summary>
        public uint DefenseBonus { get; set; }

        /// <summary>
        /// The character’s base Reputation bonus. 
        /// </summary>
        public uint ReputationBonus { get; set; }
    }
}