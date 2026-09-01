using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.ContentPacks;

public static class ContentPackRuntime
{
    private static RuntimeState state = CreateState([]);

    public static IReadOnlyList<ContentPack> ActivePacks =>
        Volatile.Read(ref state).ActivePacks;

    public static IReadOnlyList<ContentPack> ExternalPacks =>
        Volatile.Read(ref state).ExternalPacks;

    public static IReadOnlyList<string> ActivePackIds =>
        Volatile.Read(ref state).ActivePackIds;

    public static bool TryGetPack(string packageId, out ContentPack? pack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return Volatile.Read(ref state).ById.TryGetValue(packageId, out pack);
    }

    public static void Configure(IEnumerable<ContentPack> externalPacks)
    {
        ArgumentNullException.ThrowIfNull(externalPacks);
        var next = CreateState(externalPacks);
        Volatile.Write(ref state, next);
    }

    public static void ResetToCorePack() => Configure([]);

    private static RuntimeState CreateState(IEnumerable<ContentPack> externalPacks)
    {
        var external = externalPacks.ToArray();
        if (external.Any(pack => pack is null))
        {
            throw new ArgumentException("The active content pack list contains null.",
                nameof(externalPacks));
        }
        if (external.Any(pack => string.Equals(
                pack.Manifest.Id,
                ContentId.CoreNamespace,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The embedded core pack cannot be replaced by an external package.");
        }

        var duplicate = external
            .GroupBy(pack => pack.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Active content pack ID '{duplicate.Key}' is duplicated.");
        }

        var active = new[] { CoreContentPack.Pack }.Concat(external).ToArray();
        return new RuntimeState(
            Array.AsReadOnly(active),
            Array.AsReadOnly(external),
            Array.AsReadOnly(active.Select(pack => pack.Manifest.Id).ToArray()),
            new ReadOnlyDictionary<string, ContentPack>(active.ToDictionary(
                pack => pack.Manifest.Id,
                StringComparer.OrdinalIgnoreCase)));
    }

    private sealed record RuntimeState(
        IReadOnlyList<ContentPack> ActivePacks,
        IReadOnlyList<ContentPack> ExternalPacks,
        IReadOnlyList<string> ActivePackIds,
        IReadOnlyDictionary<string, ContentPack> ById);
}
