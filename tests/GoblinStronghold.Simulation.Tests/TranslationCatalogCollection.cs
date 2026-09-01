using Xunit;

namespace GoblinStronghold.Simulation.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TranslationCatalogCollection
{
    public const string Name = "Translation catalog";
}
