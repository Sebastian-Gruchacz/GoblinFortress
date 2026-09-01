using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Map.Generation;

public enum RiverGenerationMode : byte
{
    Absent = 0,
    SingleChannel = 1,
    BranchingChannels = 2,
}

public readonly record struct LocationGenerationRequest(
    ContentId ProfileId,
    WorldSeed Seed,
    int Width,
    int Height,
    int GeneratorVersion,
    RiverGenerationMode RiverMode)
{
    public static LocationGenerationRequest CreateDefault(
        WorldSeed seed,
        int width,
        int height,
        int generatorVersion = SwampMapGenerator.CurrentVersion) =>
        new(
            SwampMapGenerator.DefaultProfileId,
            seed,
            width,
            height,
            generatorVersion,
            RiverGenerationMode.SingleChannel);
}
