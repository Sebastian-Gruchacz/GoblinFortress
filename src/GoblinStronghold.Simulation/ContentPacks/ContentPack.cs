using System.Collections.ObjectModel;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoblinStronghold.Simulation.ContentPacks;

public sealed partial class ContentPack
{
    public const string ManifestPath = "manifest.json";
    public const int SupportedManifestSchemaVersion = 1;
    public const int SupportedContentSchemaVersion = 1;
    public const string ExpectedFormat = "goblin-pack";

    private readonly IReadOnlyDictionary<string, byte[]> files;

    private ContentPack(
        string sourceName,
        ContentPackManifest manifest,
        IReadOnlyDictionary<string, byte[]> files)
    {
        SourceName = sourceName;
        Manifest = manifest;
        this.files = files;
        FilePaths = Array.AsReadOnly(files.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public string SourceName { get; }
    public ContentPackManifest Manifest { get; }
    public IReadOnlyList<string> FilePaths { get; }

    public bool Contains(string path) => files.ContainsKey(NormalizeLookupPath(path));

    public Stream OpenRead(string path)
    {
        var normalized = NormalizeLookupPath(path);
        return files.TryGetValue(normalized, out var contents)
            ? new MemoryStream(contents, writable: false)
            : throw new FileNotFoundException(
                $"Content pack '{Manifest.Id}' does not contain '{normalized}'.",
                normalized);
    }

    public string ReadAllText(string path)
    {
        using var stream = OpenRead(path);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static ContentPack Create(
        string sourceName,
        IDictionary<string, byte[]> sourceFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, contents) in sourceFiles)
        {
            var normalized = ValidatePackagePath(path);
            ArgumentNullException.ThrowIfNull(contents);
            if (!files.TryAdd(normalized, contents.ToArray()))
            {
                throw new InvalidDataException(
                    $"Content pack '{sourceName}' contains duplicate path '{normalized}'.");
            }
        }

        if (!files.TryGetValue(ManifestPath, out var manifestBytes))
        {
            throw new InvalidDataException(
                $"Content pack '{sourceName}' does not contain root manifest.json.");
        }

        ContentPackManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ContentPackManifest>(manifestBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException(
                    $"Content pack '{sourceName}' has an empty manifest.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Content pack '{sourceName}' has an invalid manifest.", exception);
        }

        ValidateManifest(sourceName, manifest, files);
        return new ContentPack(
            sourceName,
            manifest,
            new ReadOnlyDictionary<string, byte[]>(files));
    }

    internal static string ValidatePackagePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains(':') ||
            path.Contains('\0'))
        {
            throw new InvalidDataException($"Unsafe content pack path '{path}'.");
        }

        var segments = path.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidDataException($"Unsafe content pack path '{path}'.");
        }

        return string.Join('/', segments);
    }

    private static string NormalizeLookupPath(string path) => ValidatePackagePath(path);

    private static void ValidateManifest(
        string sourceName,
        ContentPackManifest manifest,
        IReadOnlyDictionary<string, byte[]> files)
    {
        if (!string.Equals(manifest.Format, ExpectedFormat, StringComparison.Ordinal) ||
            manifest.SchemaVersion != SupportedManifestSchemaVersion ||
            manifest.ContentSchemaVersion != SupportedContentSchemaVersion ||
            !PackageIdPattern().IsMatch(manifest.Id) ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64 ||
            !IsSafeOptionalMetadata(manifest.Title) ||
            !IsSafeOptionalMetadata(manifest.Author) ||
            !IsSafeOptionalMetadata(manifest.LocaleDisplayName) ||
            manifest.Authors is null || manifest.Authors.Count > 32 ||
            manifest.Authors.Any(author => !IsSafeRequiredMetadata(author)) ||
            !IsSafeContactEmail(manifest.ContactEmail) ||
            (manifest.Locale is not null && !LocalePattern().IsMatch(manifest.Locale)) ||
            manifest.Type is not ("core" or "language" or "content") ||
            manifest.Type == "language" && string.IsNullOrWhiteSpace(manifest.Locale))
        {
            throw new InvalidDataException(
                $"Content pack '{sourceName}' has an unsupported or invalid manifest.");
        }

        if (manifest.ReadmePath is not null)
        {
            ValidatePackagePath(manifest.ReadmePath);
            if (!files.ContainsKey(manifest.ReadmePath))
            {
                throw new InvalidDataException(
                    $"Content pack '{sourceName}' declares missing README " +
                    $"'{manifest.ReadmePath}'.");
            }
        }

        ValidatePackageIds(sourceName, "dependency", manifest.Dependencies);
        ValidatePackageIds(sourceName, "loadAfter", manifest.LoadAfter);
        ValidatePackageIds(sourceName, "loadBefore", manifest.LoadBefore);
    }

    private static bool IsSafeOptionalMetadata(string? value) =>
        value is null || value.Length <= 256 && !value.Any(char.IsControl);

    private static bool IsSafeRequiredMetadata(string value) =>
        !string.IsNullOrWhiteSpace(value) && IsSafeOptionalMetadata(value);

    private static bool IsSafeContactEmail(string? value) =>
        value is null || value.Length <= 254 &&
        MailAddress.TryCreate(value, out var address) &&
        string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);

    private static void ValidatePackageIds(
        string sourceName,
        string field,
        IReadOnlyCollection<string>? ids)
    {
        if (ids is null || ids.Any(id => !PackageIdPattern().IsMatch(id)) ||
            ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
        {
            throw new InvalidDataException(
                $"Content pack '{sourceName}' has invalid {field} package IDs.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(
        "^(?=.{1,64}$)[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();
}
