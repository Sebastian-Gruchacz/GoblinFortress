using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Animals;

public sealed class AnimalSpeciesCatalog : IAnimalSpeciesCatalog
{
    private const string ContentPath = "content/animal-species.json";
    private readonly IReadOnlyDictionary<AnimalKind, AnimalSpeciesDefinition> byKind;
    private readonly IReadOnlyDictionary<ContentId, AnimalSpeciesDefinition> byId;

    public AnimalSpeciesCatalog(IEnumerable<AnimalSpeciesDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var source = definitions.ToArray();
        Validate(source);
        var all = source.Select(definition => definition with
        {
            Behavior = definition.Behavior with
            {
                Enemies = Array.AsReadOnly(definition.Behavior.Enemies.ToArray()),
            },
            Harvest = definition.Harvest with
            {
                Byproducts = Array.AsReadOnly(
                    definition.Harvest.Byproducts.ToArray()),
            },
            Visual = definition.Visual with
            {
                Palette = new ReadOnlyDictionary<string, string>(
                    definition.Visual.Palette.ToDictionary(
                        color => color.Key,
                        color => color.Value,
                        StringComparer.Ordinal)),
            },
        }).ToArray();

        All = Array.AsReadOnly(all);
        byKind = new ReadOnlyDictionary<AnimalKind, AnimalSpeciesDefinition>(
            all.ToDictionary(definition => definition.LegacyKind));
        byId = new ReadOnlyDictionary<ContentId, AnimalSpeciesDefinition>(
            all.ToDictionary(definition => definition.Id));
    }

    public static AnimalSpeciesCatalog Core { get; } = LoadCore();
    private static AnimalSpeciesCatalog current = Core;

    public static AnimalSpeciesCatalog Current => Volatile.Read(ref current);

    public IReadOnlyList<AnimalSpeciesDefinition> All { get; }

    public AnimalSpeciesDefinition Get(AnimalKind kind) =>
        byKind.TryGetValue(kind, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown animal kind '{kind}'.");

    public AnimalSpeciesDefinition Get(ContentId id) =>
        byId.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown animal species ID '{id}'.");

    public static AnimalSpeciesCatalog Compose(IEnumerable<ContentPack> externalPacks)
    {
        ArgumentNullException.ThrowIfNull(externalPacks);
        var definitions = Core.All.ToDictionary(
            definition => definition.Id,
            definition => definition);
        foreach (var pack in externalPacks.Where(pack =>
                     pack.Manifest.Type == "content" && pack.Contains(ContentPath)))
        {
            foreach (var definition in ReadDocument(pack))
            {
                if (definition.Id.PackageId != ContentId.CoreNamespace &&
                    !string.Equals(
                        definition.Id.PackageId,
                        pack.Manifest.Id,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Content pack '{pack.Manifest.Id}' cannot define animal species " +
                        $"owned by '{definition.Id.PackageId}'.");
                }
                definitions[definition.Id] = definition;
            }
        }
        return new AnimalSpeciesCatalog(definitions.Values);
    }

    public static void Activate(AnimalSpeciesCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Volatile.Write(ref current, catalog);
    }

    public static void ResetToCore() => Activate(Core);

    private static AnimalSpeciesCatalog LoadCore()
    {
        return new AnimalSpeciesCatalog(ReadDocument(CoreContentPack.Pack));
    }

    private static IReadOnlyList<AnimalSpeciesDefinition> ReadDocument(ContentPack pack)
    {
        using var stream = pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(),
                new ContentIdJsonConverter(),
            },
        };
        AnimalSpeciesCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<AnimalSpeciesCatalogDocument>(
                stream,
                options) ?? throw new InvalidDataException(
                    $"Animal species catalog in '{pack.Manifest.Id}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Animal species catalog in '{pack.Manifest.Id}' is invalid.",
                exception);
        }
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported animal species catalog schema {document.SchemaVersion}.");
        }
        return document.Species;
    }

    private static void Validate(IReadOnlyList<AnimalSpeciesDefinition> definitions)
    {
        if (definitions.Any(definition =>
                !Enum.IsDefined(definition.LegacyKind) ||
                definition.Id.PackageId != ContentId.CoreNamespace ||
                definition.Vitals is null ||
                definition.Vitals.MaximumHealth < 1 ||
                definition.Vitals.MaximumFatigue < 1 ||
                definition.Vitals.MovementFatigue < 1 ||
                definition.Vitals.RestRecovery < 1 ||
                definition.Habitat is null ||
                !Enum.IsDefined(definition.Habitat.Kind) ||
                definition.Habitat.MinimumDepthBelowSurface < 0 ||
                (definition.Habitat.Kind == AnimalHabitatKind.Cave) !=
                    (definition.Habitat.MinimumDepthBelowSurface > 0) ||
                definition.Behavior is null ||
                !Enum.IsDefined(definition.Behavior.Disposition) ||
                !AnimalDispositionPolicy.Supports(definition.Behavior.ModelId) ||
                definition.Behavior.Aggression is < 0 or > 100 ||
                definition.Behavior.DetectionRadius < 1 ||
                definition.Behavior.ForageHungerThreshold < 1 ||
                definition.Behavior.StarvationHungerThreshold <=
                    definition.Behavior.ForageHungerThreshold ||
                definition.Behavior.RoamingInterval < 1 ||
                definition.Behavior.Enemies is null ||
                definition.Behavior.Enemies.Count == 0 ||
                definition.Behavior.Enemies.Any(enemy =>
                    !Enum.IsDefined(enemy.Kind) ||
                    !ContentId.TryParse(enemy.Id.Value, out _)) ||
                definition.Behavior.Enemies.Distinct().Count() !=
                    definition.Behavior.Enemies.Count ||
                definition.Spawn is null ||
                !Enum.IsDefined(definition.Spawn.Mode) ||
                definition.Spawn.Order < 0 ||
                definition.Spawn.MinimumDepth < 0 ||
                definition.Spawn.MaximumDepth < definition.Spawn.MinimumDepth ||
                definition.Spawn.MinimumPopulation < 1 ||
                definition.Spawn.MapCellsPerAnimal < 0 ||
                definition.Spawn.PopulationIncreaseDepth <
                    definition.Spawn.MinimumDepth ||
                definition.Spawn.PopulationIncrease < 0 ||
                !Enum.IsDefined(definition.EcologyProfile) ||
                definition.Attack is null ||
                definition.Attack.BaseDamage < 0 ||
                definition.Attack.DamagePerDepth < 0 ||
                definition.Harvest is null ||
                definition.Harvest.RawMeat < 0 ||
                definition.Harvest.Hide < 0 ||
                definition.Harvest.Bone < 0 ||
                definition.Harvest.ForagingExperience < 0 ||
                definition.Harvest.HunterMeleeDamage < 1 ||
                definition.Harvest.Byproducts is null ||
                definition.Harvest.Byproducts.Any(item =>
                    !Enum.IsDefined(item.Resource) || item.Resource == ResourceKind.Any ||
                    !Enum.IsDefined(item.Variant) || item.Variant == ResourceVariant.None ||
                    item.Quantity < 1) ||
                definition.Harvest.Byproducts.Select(item => (item.Resource, item.Variant))
                    .Distinct().Count() != definition.Harvest.Byproducts.Count ||
                definition.DebugVisionRadius < 1 ||
                definition.Visual is null ||
                !ContentId.TryParse(definition.Visual.RendererId.Value, out _) ||
                (definition.Visual.AtlasId is { } atlasId &&
                    !ContentId.TryParse(atlasId.Value, out _)) ||
                (definition.Visual.SpriteId is { } spriteId &&
                    !ContentId.TryParse(spriteId.Value, out _)) ||
                definition.Visual.Palette is null ||
                definition.Visual.Palette.Count == 0 ||
                definition.Visual.Palette.Any(color =>
                    string.IsNullOrWhiteSpace(color.Key) || !IsColor(color.Value))) ||
            definitions.Select(definition => definition.Id).Distinct().Count() !=
                definitions.Count ||
            definitions.Select(definition => definition.LegacyKind).Distinct().Count() !=
                definitions.Count ||
            !definitions.Select(definition => definition.LegacyKind).Order().SequenceEqual(
                Enum.GetValues<AnimalKind>().Order()))
        {
            throw new InvalidDataException(
                "The core animal species catalog must define every legacy kind exactly once.");
        }
    }

    private static bool IsColor(string value) =>
        value is not null && value.Length == 7 && value[0] == '#' &&
        value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF".AsSpan()) == false;

    private sealed class AnimalSpeciesCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<AnimalSpeciesDefinition> Species { get; init; } = [];
    }
}
