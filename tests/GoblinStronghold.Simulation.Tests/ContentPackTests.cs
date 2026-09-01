using System.IO.Compression;
using System.Text;
using GoblinStronghold.Simulation.Animals;
using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Localization;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

[Collection(TranslationCatalogCollection.Name)]
public sealed class ContentPackTests
{
    [Fact]
    public void CorePackExposesCurrentCatalogsAndTranslations()
    {
        var pack = CoreContentPack.Pack;

        Assert.Equal("core", pack.Manifest.Id);
        Assert.Equal("core", pack.Manifest.Type);
        Assert.True(pack.Contains("content/animal-species.json"));
        Assert.True(pack.Contains("content/civilizations.json"));
        Assert.True(pack.Contains("content/materials.json"));
        Assert.True(pack.Contains("content/crafting-recipes.json"));
        Assert.True(pack.Contains("content/workshops.json"));
        Assert.True(pack.Contains("localization/en/interface.json"));
        Assert.True(pack.Contains("localization/pl/interface.json"));
    }

    [Theory]
    [InlineData("oak-wood", "core:oak-wood")]
    [InlineData("marshes:bog-iron", "marshes:bog-iron")]
    public void ContentIdsCanonicalizeLegacyCoreAndNamespacedValues(
        string source,
        string expected)
    {
        Assert.Equal(expected, ContentId.Parse(source).Value);
    }

    [Theory]
    [InlineData("Marshes:bog-iron")]
    [InlineData("marshes:")]
    [InlineData(":bog-iron")]
    [InlineData("marshes:bog/iron")]
    public void ContentIdsRejectNonPortableValues(string source)
    {
        Assert.False(ContentId.TryParse(source, out _));
    }

    [Fact]
    public void RuntimeRegistryAlwaysStartsWithCoreAndPreservesLoadOrder()
    {
        using var firstArchive = CreateArchive(
            ("manifest.json", ValidManifest("first", "content")));
        using var secondArchive = CreateArchive(
            ("manifest.json", ValidManifest("second", "language", "de")));
        var first = ContentPackArchiveLoader.Load(firstArchive, "first.gobmod");
        var second = ContentPackArchiveLoader.Load(secondArchive, "second.goblang");

        try
        {
            ContentPackRuntime.Configure([second, first]);

            Assert.Equal(["core", "second", "first"], ContentPackRuntime.ActivePackIds);
            Assert.Equal([second, first], ContentPackRuntime.ExternalPacks);
            Assert.True(ContentPackRuntime.TryGetPack("CORE", out var core));
            Assert.Same(CoreContentPack.Pack, core);
            Assert.True(ContentPackRuntime.TryGetPack("second", out var activeSecond));
            Assert.Same(second, activeSecond);
            Assert.False(ContentPackRuntime.TryGetPack("missing", out _));
        }
        finally
        {
            ContentPackRuntime.ResetToCorePack();
        }
    }

    [Fact]
    public void ContentPackCanOverrideCoreAnimalParametersWithoutMutatingCoreCatalog()
    {
        var animalJson = CoreContentPack.Pack.ReadAllText("content/animal-species.json")
            .Replace(
                "\"maximumHealth\": 100",
                "\"maximumHealth\": 150",
                StringComparison.Ordinal);
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("harder-wildlife", "content")),
            ("content/animal-species.json", animalJson));
        var pack = ContentPackArchiveLoader.Load(archive, "harder-wildlife.gobmod");

        var composed = AnimalSpeciesCatalog.Compose([pack]);

        Assert.Equal(150, composed.Get(AnimalKind.MarshHare).Vitals.MaximumHealth);
        Assert.Equal(100,
            AnimalSpeciesCatalog.Core.Get(AnimalKind.MarshHare).Vitals.MaximumHealth);
    }

    [Fact]
    public void ContentPackCanOverrideCivilizationParametersWithoutMutatingCoreCatalog()
    {
        var civilizationJson = CoreContentPack.Pack.ReadAllText(
                "content/civilizations.json")
            .Replace(
                "\"presencePercent\": 70",
                "\"presencePercent\": 100",
                StringComparison.Ordinal);
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("deeper-clans", "content")),
            ("content/civilizations.json", civilizationJson));
        var pack = ContentPackArchiveLoader.Load(archive, "deeper-clans.gobmod");

        var composed = CivilizationCatalog.Compose([pack]);
        var role = CivilizationLegacyRole.DeepDwarfClan;

        Assert.Equal(100, composed.Get(role).UndergroundGeneration!.PresencePercent);
        Assert.Equal(70,
            CivilizationCatalog.Core.Get(role).UndergroundGeneration!.PresencePercent);
    }

    [Fact]
    public void InvalidRuntimeRegistryDoesNotReplacePreviousState()
    {
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("marshes", "content")));
        var pack = ContentPackArchiveLoader.Load(archive, "marshes.gobmod");

        try
        {
            ContentPackRuntime.Configure([pack]);

            Assert.Throws<InvalidDataException>(() =>
                ContentPackRuntime.Configure([pack, pack]));
            Assert.Equal(["core", "marshes"], ContentPackRuntime.ActivePackIds);
        }
        finally
        {
            ContentPackRuntime.ResetToCorePack();
        }
    }

    [Fact]
    public void ZipPackLoadsIntoMemoryWithoutKeepingSourceOpen()
    {
        using var archiveStream = CreateArchive(
            ("manifest.json", ValidManifest("community.polish", "language", "pl")),
            ("localization/interface.json", "{\"hello\":\"Cześć\"}"));

        var pack = ContentPackArchiveLoader.Load(archiveStream, "polish.goblang");
        archiveStream.Dispose();

        Assert.Equal("community.polish", pack.Manifest.Id);
        Assert.Equal("pl", pack.Manifest.Locale);
        Assert.Contains("Cześć", pack.ReadAllText("localization/interface.json"));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("content/../../outside.json")]
    [InlineData("/absolute.json")]
    [InlineData("C:/absolute.json")]
    [InlineData("content\\windows.json")]
    public void ZipPackRejectsUnsafePaths(string path)
    {
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("test.paths", "content")),
            (path, "{}"));

        Assert.Throws<InvalidDataException>(() =>
            ContentPackArchiveLoader.Load(archive, "unsafe.gobmod"));
    }

    [Fact]
    public void ZipPackRejectsCaseInsensitiveDuplicatePaths()
    {
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("test.duplicates", "content")),
            ("content/item.json", "{}"),
            ("CONTENT/ITEM.JSON", "{}"));

        Assert.Throws<InvalidDataException>(() =>
            ContentPackArchiveLoader.Load(archive, "duplicate.gobmod"));
    }

    [Fact]
    public void ZipPackRejectsExpandedContentOverConfiguredLimit()
    {
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("test.large", "content")),
            ("content/large.json", new string('x', 512)));
        var limits = new ContentPackLoadLimits(
            MaximumFileCount: 10,
            MaximumSingleFileBytes: 256,
            MaximumTotalBytes: 1024);

        Assert.Throws<InvalidDataException>(() =>
            ContentPackArchiveLoader.Load(archive, "large.gobmod", limits));
    }

    [Fact]
    public void LanguagePackAddsLocaleAndFallsBackToCoreEnglishPerKey()
    {
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest(
                "community.german",
                "language",
                "de",
                "Deutsch")),
            ("localization/interface.json", TranslationDocument(
                "de",
                "interface",
                "common",
                "close",
                "Schließen")));
        var pack = ContentPackArchiveLoader.Load(archive, "german.goblang");

        try
        {
            TranslationCatalog.ConfigurePacks([pack]);

            Assert.Equal(["en", "pl", "de"], TranslationCatalog.SupportedLocales);
            Assert.Equal(["community.german"], TranslationCatalog.ConfiguredPackIds);
            Assert.Equal("de", TranslationCatalog.NormalizeLocale("de-DE"));
            Assert.Equal(
                "Schließen",
                TranslationCatalog.Get("de", "interface", "common", "close"));
            Assert.Equal(
                "Keyboard shortcuts",
                TranslationCatalog.Get("de", "interface", "options", "shortcuts"));
            Assert.Equal("Deutsch", TranslationCatalog.GetLocaleDisplayName("en", "de"));
        }
        finally
        {
            TranslationCatalog.ResetToCorePack();
        }
    }

    [Fact]
    public void LocalDiscoveryContinuesAfterBrokenAndMisnamedPackages()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-pack-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using (var valid = CreateArchive(
                ("manifest.json", ValidManifest(
                    "community.german",
                    "language",
                    "de",
                    "Deutsch")),
                ("localization/interface.json", TranslationDocument(
                    "de",
                    "interface",
                    "common",
                    "close",
                    "Schließen"))))
            {
                File.WriteAllBytes(Path.Combine(directory, "valid.goblang"), valid.ToArray());
            }
            using (var wrongType = CreateArchive(
                ("manifest.json", ValidManifest(
                    "community.wrong-extension",
                    "language",
                    "de")),
                ("localization/interface.json", TranslationDocument(
                    "de", "interface", "common", "close", "Schließen"))))
            {
                File.WriteAllBytes(
                    Path.Combine(directory, "wrong-extension.gobmod"),
                    wrongType.ToArray());
            }
            File.WriteAllText(Path.Combine(directory, "broken.gobpack"), "not a ZIP");
            File.WriteAllText(Path.Combine(directory, "ignored.zip"), "not a pack");

            var result = LocalContentPackDiscovery.Discover(directory);

            Assert.Single(result.Packs);
            Assert.Equal("community.german", result.Packs[0].Manifest.Id);
            Assert.Equal(2, result.Failures.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidTranslationPackDoesNotReplaceWorkingCatalogState()
    {
        using var validArchive = CreateArchive(
            ("manifest.json", ValidManifest(
                "community.german",
                "language",
                "de",
                "Deutsch")),
            ("localization/interface.json", TranslationDocument(
                "de", "interface", "common", "close", "Schließen")));
        using var invalidArchive = CreateArchive(
            ("manifest.json", ValidManifest(
                "community.french",
                "language",
                "fr",
                "Français")),
            ("localization/interface.json", TranslationDocument(
                "de", "interface", "common", "close", "Fermer")));
        var validPack = ContentPackArchiveLoader.Load(
            validArchive,
            "german.goblang");
        var invalidPack = ContentPackArchiveLoader.Load(
            invalidArchive,
            "french.goblang");

        try
        {
            TranslationCatalog.ConfigurePacks([validPack]);

            Assert.Throws<InvalidDataException>(() =>
                TranslationCatalog.ConfigurePacks([validPack, invalidPack]));
            Assert.Equal(["en", "pl", "de"], TranslationCatalog.SupportedLocales);
            Assert.Equal(
                "Schließen",
                TranslationCatalog.Get("de", "interface", "common", "close"));
        }
        finally
        {
            TranslationCatalog.ResetToCorePack();
        }
    }

    [Fact]
    public void ModEnglishBaselineLoadsBeforeExternalLanguageOverride()
    {
        using var modArchive = CreateArchive(
            ("manifest.json", ValidManifest("marshes", "content")),
            ("localization/en/marshes.json", TranslationDocument(
                "en", "marshes", "materials", "bog-iron", "bog iron")),
            ("localization/en/marshes-extra.json", TranslationDocument(
                "en", "marshes-extra", "materials", "peat", "peat")));
        using var languageArchive = CreateArchive(
            ("manifest.json", ValidManifest(
                "community.german",
                "language",
                "de",
                "Deutsch")),
            ("localization/marshes.json", TranslationDocument(
                "de", "marshes", "materials", "bog-iron", "Sumpfeisen")));
        var mod = ContentPackArchiveLoader.Load(modArchive, "marshes.gobmod");
        var language = ContentPackArchiveLoader.Load(
            languageArchive,
            "german.goblang");

        try
        {
            TranslationCatalog.ConfigurePacks([language, mod]);

            Assert.Equal(
                "Sumpfeisen",
                TranslationCatalog.Get("de", "marshes", "materials", "bog-iron"));
            Assert.Equal(
                "peat",
                TranslationCatalog.Get("de", "marshes-extra", "materials", "peat"));
        }
        finally
        {
            TranslationCatalog.ResetToCorePack();
        }
    }

    [Fact]
    public void ModTranslationWithoutEmbeddedEnglishFallbackIsRejected()
    {
        using var archive = CreateArchive(
            ("manifest.json", ValidManifest("marshes", "content")),
            ("localization/pl/marshes.json", TranslationDocument(
                "pl", "marshes", "materials", "bog-iron", "żelazo bagienne")));
        var mod = ContentPackArchiveLoader.Load(archive, "marshes.gobmod");

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                TranslationCatalog.ConfigurePacks([mod]));
        }
        finally
        {
            TranslationCatalog.ResetToCorePack();
        }
    }

    [Fact]
    public void UserPreferencesPersistEnabledStateAndLoadOrder()
    {
        using var firstArchive = CreateArchive(
            ("manifest.json", ValidManifest("first", "content")));
        using var secondArchive = CreateArchive(
            ("manifest.json", ValidManifest("second", "content")));
        var first = ContentPackArchiveLoader.Load(firstArchive, "first.gobmod");
        var second = ContentPackArchiveLoader.Load(secondArchive, "second.gobmod");
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-pack-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "mods.json");

        try
        {
            var preferences = ContentPackUserPreferences.Empty();
            preferences.ReplaceVisible(
            [
                new ContentPackPreference("second", Enabled: false),
                new ContentPackPreference("first", Enabled: true),
            ]);
            preferences.Save(path);

            var loaded = ContentPackUserPreferences.Load(path);
            var ordered = loaded.Order([first, second]);

            Assert.Equal(["second", "first"],
                ordered.Select(pack => pack.Manifest.Id));
            Assert.False(loaded.IsEnabled("second"));
            Assert.True(loaded.IsEnabled("first"));
            Assert.True(loaded.IsEnabled("new-pack"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ManifestExposesAuthorsContactAndEmbeddedReadme()
    {
        const string manifest = """
        {
          "format": "goblin-pack",
          "schemaVersion": 1,
          "id": "marshes",
          "type": "content",
          "version": "2.1.0",
          "title": "Marshes",
          "authors": ["Ada Goblin", "Bert Troll"],
          "contactEmail": "mods@example.test",
          "readmePath": "README.md",
          "contentSchemaVersion": 1,
          "dependencies": [],
          "loadAfter": [],
          "loadBefore": []
        }
        """;
        using var archive = CreateArchive(
            ("manifest.json", manifest),
            ("README.md", "# Marshes\nMind the bog."));

        var pack = ContentPackArchiveLoader.Load(archive, "marshes.gobmod");

        Assert.Equal(["Ada Goblin", "Bert Troll"], pack.Manifest.Authors);
        Assert.Equal("mods@example.test", pack.Manifest.ContactEmail);
        Assert.Contains("Mind the bog", pack.ReadAllText(pack.Manifest.ReadmePath!));
    }

    [Fact]
    public void ManifestCannotReferenceMissingReadme()
    {
        const string manifest = """
        {
          "format": "goblin-pack",
          "schemaVersion": 1,
          "id": "marshes",
          "type": "content",
          "version": "2.1.0",
          "readmePath": "README.md",
          "contentSchemaVersion": 1,
          "dependencies": [],
          "loadAfter": [],
          "loadBefore": []
        }
        """;
        using var archive = CreateArchive(("manifest.json", manifest));

        Assert.Throws<InvalidDataException>(() =>
            ContentPackArchiveLoader.Load(archive, "marshes.gobmod"));
    }

    private static MemoryStream CreateArchive(params (string Path, string Contents)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in files)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(
                    entry.Open(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(contents);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string ValidManifest(
        string id,
        string type,
        string? locale = null,
        string? localeDisplayName = null) => $$"""
        {
          "format": "goblin-pack",
          "schemaVersion": 1,
          "id": "{{id}}",
          "type": "{{type}}",
          "version": "1.0.0",
          "locale": {{(locale is null ? "null" : $"\"{locale}\"")}},
          "localeDisplayName": {{(localeDisplayName is null
              ? "null"
              : $"\"{localeDisplayName}\"")}},
          "contentSchemaVersion": 1,
          "dependencies": [],
          "loadAfter": [],
          "loadBefore": []
        }
        """;

    private static string TranslationDocument(
        string locale,
        string section,
        string subsection,
        string key,
        string value) => $$"""
        {
          "schemaVersion": 1,
          "locale": "{{locale}}",
          "section": "{{section}}",
          "subsections": {
            "{{subsection}}": {
              "{{key}}": "{{value}}"
            }
          }
        }
        """;
}
