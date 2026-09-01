using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Civilizations.Naming;

public readonly record struct NameGenerationRequest(
    WorldSeed WorldSeed,
    ulong SubjectId,
    int Ordinal,
    IReadOnlySet<string> ExistingNames,
    ActorSex Sex = ActorSex.Sexless);

public interface INameGenerator
{
    ContentId Id { get; }

    string Generate(NameGenerationRequest request);
}
