using System;
using ObscureWare.D20Common;

namespace ObscureWare.ModernD20.Descriptors
{
    [Serializable]
    public class AbilityDescriptor
    {
        public AbilityEnum Value { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string Description { get; set; }
    }

    /*
    :
Strength (STR)
Dexterity (DEX)
Constitution (CON)
Intelligence (INT)
Wisdom (WIS)
Charisma (CHA)     


Changing Ability Scores
Ability scores can increase with no limit. 
Poisons, diseases, and other effects can cause temporary ability damage. Ability points lost to damage return naturally, typically at a rate of 1 point per day for each affected ability.
As a character ages, some ability scores go up and others go down. 
When an ability score changes, the modifier associated with that score also changes.
*/
}
