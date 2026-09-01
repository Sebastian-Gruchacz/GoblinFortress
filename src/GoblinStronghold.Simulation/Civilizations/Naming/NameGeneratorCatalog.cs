using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Civilizations.Naming;

public sealed class NameGeneratorCatalog
{
    private const string ContentPath = "content/name-generators.json";
    private readonly IReadOnlyDictionary<ContentId, INameGenerator> byId;

    public NameGeneratorCatalog(IEnumerable<NameGeneratorDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var all = definitions.ToArray();
        Validate(all);
        All = Array.AsReadOnly(all);
        byId = new ReadOnlyDictionary<ContentId, INameGenerator>(all.ToDictionary(
            definition => definition.Id,
            CreateGenerator));
    }

    public static NameGeneratorCatalog Core { get; } = LoadCore();
    private static NameGeneratorCatalog current = Core;

    public static NameGeneratorCatalog Current => Volatile.Read(ref current);

    public IReadOnlyList<NameGeneratorDefinition> All { get; }

    public bool Contains(ContentId id) => byId.ContainsKey(id);

    public INameGenerator Get(ContentId id) =>
        byId.TryGetValue(id, out var generator)
            ? generator
            : throw new KeyNotFoundException($"Unknown name generator ID '{id}'.");

    public static NameGeneratorCatalog Compose(IEnumerable<ContentPack> externalPacks)
    {
        ArgumentNullException.ThrowIfNull(externalPacks);
        var definitions = Core.All.ToDictionary(definition => definition.Id);
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
                        $"Content pack '{pack.Manifest.Id}' cannot define name generator " +
                        $"owned by '{definition.Id.PackageId}'.");
                }
                definitions[definition.Id] = definition;
            }
        }
        return new NameGeneratorCatalog(definitions.Values);
    }

    public static void Activate(NameGeneratorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Volatile.Write(ref current, catalog);
    }

    public static void ResetToCore() => Activate(Core);

    private static NameGeneratorCatalog LoadCore() =>
        new(ReadDocument(CoreContentPack.Pack));

    private static IReadOnlyList<NameGeneratorDefinition> ReadDocument(ContentPack pack)
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
        NameGeneratorCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<NameGeneratorCatalogDocument>(
                stream,
                options) ?? throw new InvalidDataException(
                    $"Name generator catalog in '{pack.Manifest.Id}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Name generator catalog in '{pack.Manifest.Id}' is invalid.",
                exception);
        }
        if (document.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                $"Unsupported name generator catalog schema {document.SchemaVersion}.");
        }
        return document.Generators;
    }

    private static void Validate(IReadOnlyList<NameGeneratorDefinition> definitions)
    {
        if (definitions.Count == 0 ||
            definitions.Select(definition => definition.Id).Distinct().Count() !=
                definitions.Count ||
            definitions.Any(IsInvalid))
        {
            throw new InvalidDataException(
                "The name generator catalog is empty or contains invalid definitions.");
        }
    }

    private static bool IsInvalid(NameGeneratorDefinition definition)
    {
        if (!ContentId.TryParse(definition.Id.Value, out _) ||
            !Enum.IsDefined(definition.Kind) ||
            !Enum.IsDefined(definition.RandomDomain))
        {
            return true;
        }

        var hasValidBeginnings = IsValidUniqueText(definition.Beginnings);
        var hasValidEndings = IsValidUniqueText(definition.Endings);
        var hasValidNames = IsValidUniqueText(definition.Names);
        var hasValidSexPools =
            IsValidOptionalUniqueText(definition.FemaleBeginnings) &&
            IsValidOptionalUniqueText(definition.MaleBeginnings) &&
            IsValidOptionalUniqueText(definition.FemaleEndings) &&
            IsValidOptionalUniqueText(definition.MaleEndings) &&
            IsValidOptionalUniqueText(definition.FemaleNames) &&
            IsValidOptionalUniqueText(definition.MaleNames);
        if (!hasValidSexPools)
        {
            return true;
        }
        return definition.Kind switch
        {
            NameGeneratorKind.SyllableCombination =>
                !hasValidBeginnings || !hasValidEndings || definition.Names.Count != 0 ||
                definition.FemaleNames.Count != 0 || definition.MaleNames.Count != 0,
            NameGeneratorKind.OrderedList =>
                !hasValidNames || definition.Beginnings.Count != 0 ||
                definition.Endings.Count != 0 || definition.FemaleBeginnings.Count != 0 ||
                definition.MaleBeginnings.Count != 0 ||
                definition.FemaleEndings.Count != 0 || definition.MaleEndings.Count != 0,
            NameGeneratorKind.NumericPlaceholder =>
                definition.Beginnings.Count != 0 || definition.Endings.Count != 0 ||
                definition.Names.Count != 0 || definition.FemaleBeginnings.Count != 0 ||
                definition.MaleBeginnings.Count != 0 ||
                definition.FemaleEndings.Count != 0 || definition.MaleEndings.Count != 0 ||
                definition.FemaleNames.Count != 0 || definition.MaleNames.Count != 0,
            _ => true,
        };
    }

    private static bool IsValidUniqueText(IReadOnlyCollection<string> values) =>
        values.Count > 0 &&
        values.All(value =>
            !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
            value.Length <= 40 && value.All(character => !char.IsControl(character))) &&
        values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    private static bool IsValidOptionalUniqueText(IReadOnlyCollection<string> values) =>
        values.Count == 0 || IsValidUniqueText(values);

    private static INameGenerator CreateGenerator(NameGeneratorDefinition definition) =>
        definition.Kind switch
        {
            NameGeneratorKind.SyllableCombination => new SyllableNameGenerator(definition),
            NameGeneratorKind.OrderedList => new OrderedNameGenerator(definition),
            NameGeneratorKind.NumericPlaceholder => new NumericNameGenerator(definition.Id),
            _ => throw new InvalidDataException(
                $"Unsupported name generator kind '{definition.Kind}'."),
        };

    private sealed class SyllableNameGenerator(NameGeneratorDefinition definition)
        : INameGenerator
    {
        public ContentId Id => definition.Id;

        public string Generate(NameGenerationRequest request)
        {
            var beginnings = SelectSexPool(
                request.Sex,
                definition.Beginnings,
                definition.FemaleBeginnings,
                definition.MaleBeginnings);
            var endings = SelectSexPool(
                request.Sex,
                definition.Endings,
                definition.FemaleEndings,
                definition.MaleEndings);
            var subject = new EntityId(request.SubjectId);
            var beginning = DeterministicRandom.NextInt(
                request.WorldSeed,
                definition.RandomDomain,
                subject,
                SimulationTick.Zero,
                definition.FirstSampleKey,
                0,
                beginnings.Count);
            var ending = DeterministicRandom.NextInt(
                request.WorldSeed,
                definition.RandomDomain,
                subject,
                SimulationTick.Zero,
                definition.SecondSampleKey,
                0,
                endings.Count);
            var candidate = beginnings[beginning] + endings[ending];
            return definition.AppendSubjectIdOnCollision &&
                   request.ExistingNames.Contains(candidate)
                ? $"{candidate}-{request.SubjectId}"
                : candidate;
        }
    }

    private sealed class OrderedNameGenerator(NameGeneratorDefinition definition)
        : INameGenerator
    {
        public ContentId Id => definition.Id;

        public string Generate(NameGenerationRequest request)
        {
            var names = SelectSexPool(
                request.Sex,
                definition.Names,
                definition.FemaleNames,
                definition.MaleNames);
            var index = ((request.Ordinal % names.Count) + names.Count) % names.Count;
            var candidate = names[index];
            return definition.AppendSubjectIdOnCollision &&
                   request.ExistingNames.Contains(candidate)
                ? $"{candidate}-{request.SubjectId}"
                : candidate;
        }
    }

    private static IReadOnlyList<string> SelectSexPool(
        ActorSex sex,
        IReadOnlyList<string> fallback,
        IReadOnlyList<string> female,
        IReadOnlyList<string> male) => sex switch
        {
            ActorSex.Female when female.Count > 0 => female,
            ActorSex.Male when male.Count > 0 => male,
            _ => fallback,
        };

    private sealed class NumericNameGenerator(ContentId id) : INameGenerator
    {
        public ContentId Id { get; } = id;

        public string Generate(NameGenerationRequest request) =>
            request.SubjectId.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class NameGeneratorCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<NameGeneratorDefinition> Generators { get; init; } = [];
    }
}
