namespace ObscureWare.ModernD20
{
    /// <summary>
    /// Available saving throw types
    /// </summary>
    public enum SavingThrowEnum : byte
    {
        /// <summary>
        /// These saves measure the character’s ability to stand up to massive physical punishment or attacks against his or her vitality and health such as poison and paralysis.
        /// </summary>
        Fortitude = 1,

        /// <summary>
        /// These saves test the character’s ability to dodge massive attacks such as explosions or car wrecks. 
        /// (Often, when damage is inevitable, the character gets to make a Reflex save to take only half damage.) 
        /// </summary>
        Reflex = 2,

        /// <summary>
        /// These saves reflect the character’s resistance to mental influence and domination as well as to many magical effects. 
        /// </summary>
        Will = 3
    }
}