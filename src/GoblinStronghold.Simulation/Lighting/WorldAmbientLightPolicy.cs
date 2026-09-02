namespace GoblinStronghold.Simulation.Lighting;

public readonly record struct WorldAmbientLight(
    float Darkness,
    float Red,
    float Green,
    float Blue);

public static class WorldAmbientLightPolicy
{
    public static WorldAmbientLight Underground { get; } =
        new(0.74f, 0.008f, 0.012f, 0.016f);

    public static WorldAmbientLight ResolveSurface(SimulationCalendarSnapshot calendar)
    {
        if (calendar.IsNight)
        {
            var nightTick = calendar.TickOfDay - calendar.DaylightTicks;
            var twilightTicks = Math.Max(1, Math.Min(600, calendar.NightTicks / 3));
            var fadeIn = Math.Clamp((double)nightTick / twilightTicks, 0d, 1d);
            var fadeOut = Math.Clamp(
                (double)(calendar.NightTicks - nightTick) / twilightTicks,
                0d,
                1d);
            return new WorldAmbientLight(
                (float)(0.48d * Math.Min(fadeIn, fadeOut)),
                0.025f,
                0.055f,
                0.12f);
        }

        var daylightProgress = (float)calendar.TickOfDay / calendar.DaylightTicks;
        var morning = 1f - Math.Clamp(daylightProgress / 0.18f, 0f, 1f);
        var afternoon = Math.Clamp((daylightProgress - 0.78f) / 0.22f, 0f, 1f);
        if (morning >= afternoon && morning > 0f)
        {
            return new WorldAmbientLight(
                0.10f * morning,
                0.42f,
                0.25f,
                0.035f);
        }

        return new WorldAmbientLight(
            0.12f * afternoon,
            0.48f,
            0.16f,
            0.025f);
    }
}
