namespace ObscureWare.D20Common
{
    public enum EffectTimeFrameEnum
    {
        /// <summary>
        /// Will be erased after particular time (mostly in rounds...)
        /// </summary>
        Temporary,

        /// <summary>
        /// Will be erased when restored (by time or rest meanings)
        /// </summary>
        Restorable,

        /// <summary>
        /// The effect is permanenet, can be removed only by counter-effect
        /// </summary>
        Permanent
    }
}