using System.Collections.ObjectModel;
using System.Text.Json;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Map.Generation;

public sealed class LocationGenerationCatalog
{
    private const string ContentPath = "content/location-profiles.json";
    private readonly IReadOnlyDictionary<ContentId, LocationGenerationProfile> byId;

    public LocationGenerationCatalog(IEnumerable<LocationGenerationProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var all = profiles.ToArray();
        Validate(all);
        All = Array.AsReadOnly(all);
        byId = new ReadOnlyDictionary<ContentId, LocationGenerationProfile>(
            all.ToDictionary(profile => profile.Id));
    }

    public static LocationGenerationCatalog Core { get; } = LoadCore();

    public IReadOnlyList<LocationGenerationProfile> All { get; }

    public LocationGenerationProfile Get(ContentId id) =>
        byId.TryGetValue(id, out var profile)
            ? profile
            : throw new KeyNotFoundException($"Unknown location profile ID '{id}'.");

    private static LocationGenerationCatalog LoadCore() =>
        new(ReadDocument(CoreContentPack.Pack));

    private static IReadOnlyList<LocationGenerationProfile> ReadDocument(ContentPack pack)
    {
        using var stream = pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new ContentIdJsonConverter() },
        };
        LocationGenerationCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LocationGenerationCatalogDocument>(
                stream,
                options) ?? throw new InvalidDataException(
                    $"Location profile catalog in '{pack.Manifest.Id}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Location profile catalog in '{pack.Manifest.Id}' is invalid.",
                exception);
        }
        if (document.SchemaVersion != 3)
        {
            throw new InvalidDataException(
                $"Unsupported location profile catalog schema {document.SchemaVersion}.");
        }
        return document.Profiles;
    }

    private static void Validate(IReadOnlyList<LocationGenerationProfile> profiles)
    {
        if (profiles.Count == 0 ||
            profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count ||
            profiles.Any(IsInvalid))
        {
            throw new InvalidDataException(
                "The location profile catalog is empty or contains invalid definitions.");
        }
    }

    private static bool IsInvalid(LocationGenerationProfile profile)
    {
        var river = profile.River;
        var road = profile.Road;
        var wetland = profile.Wetland;
        var relief = profile.Relief;
        return !ContentId.TryParse(profile.Id.Value, out _) ||
            !ContentId.TryParse(profile.ClimateProfileId.Value, out _) ||
            string.IsNullOrWhiteSpace(profile.Character) ||
            profile.MinimumDimension < 16 ||
            profile.DefaultDimension < profile.MinimumDimension ||
            profile.MaximumDimension < profile.DefaultDimension ||
            !IsUnit(river.StartY) ||
            !IsUnit(river.Slope) ||
            river.StartY - river.Slope < 0d ||
            river.MeanderAmplitude < 0d ||
            river.MinimumHalfWidth <= 0d ||
            river.HalfWidthRatio <= 0d ||
            !IsUnit(river.DeepWaterRatio) ||
            river.BankNoiseScale < 0d ||
            !IsUnit(river.BranchJunctionX) ||
            river.BranchJunctionX >= 1d ||
            !IsUnit(river.BranchEndY) ||
            river.BranchHalfWidthScale is <= 0d or > 1d ||
            river.BranchMeanderAmplitude < 0d ||
            !IsUnit(road.NorthEntryX) ||
            !IsUnit(road.SouthEntryX) ||
            road.MeanderAmplitude < 0d ||
            road.MeanderAmplitude > 0.25d ||
            !IsUnit(road.JunctionY) ||
            road.JunctionY is <= 0d or >= 1d ||
            !IsUnit(road.JunctionEndX) ||
            road.HalfWidth is < 0 or > 2 ||
            wetland.LeftBoundary <= 0d ||
            !IsUnit(wetland.LeftBoundary) ||
            wetland.BottomBoundary < 0d ||
            wetland.BottomBoundary >= 1d ||
            wetland.BottomRange <= 0d ||
            Math.Abs(wetland.BottomBoundary + wetland.BottomRange - 1d) > 0.0001d ||
            wetland.BoundaryNoiseScale <= 0d ||
            wetland.BoundaryWarpX < 0d ||
            wetland.BoundaryWarpY < 0d ||
            wetland.TerrainWarpX < 0d ||
            wetland.MoistureWarpY < 0d ||
            !IsUnit(wetland.InfluenceWeight) ||
            !IsUnit(wetland.MoistureWeight) ||
            Math.Abs(wetland.InfluenceWeight + wetland.MoistureWeight - 1d) > 0.0001d ||
            !IsUnit(wetland.DeepTerrainThreshold) ||
            !AreOrderedThresholds(
                wetland.DeepInfluenceThreshold,
                wetland.ShallowInfluenceThreshold,
                wetland.MudInfluenceThreshold) ||
            !AreOrderedThresholds(
                wetland.DeepWetnessThreshold,
                wetland.ShallowWetnessThreshold,
                wetland.MudWetnessThreshold) ||
            IsInvalidSettlement(profile.GoblinSettlement) ||
            IsInvalidSettlement(profile.HumanSettlement) ||
            !IsUnit(relief.DeepFloorThreshold) ||
            relief.ValleyWidthRatio <= 0d ||
            relief.UplandRange <= 0d ||
            relief.FoothillRadiusX <= 0d ||
            relief.FoothillRadiusY <= 0d ||
            relief.BroadWeight < 0d ||
            relief.RidgeWeight < 0d ||
            relief.UplandWeight < 0d ||
            relief.FoothillWeight < 0d ||
            relief.ValleyPenalty < 0d ||
            !IsUnit(relief.HighThreshold) ||
            !IsUnit(relief.RaisedThreshold) ||
            relief.HighThreshold <= relief.RaisedThreshold ||
            !IsUnit(relief.DepressionThreshold) ||
            relief.MinimumDepressionRiverDistance < 0d;
    }

    private static bool IsInvalidSettlement(SettlementGenerationProfile settlement) =>
        !IsUnit(settlement.NormalizedX) ||
        !IsUnit(settlement.NormalizedY) ||
        settlement.PadWidth < 1 ||
        settlement.PadHeight < 1 ||
        settlement.Moisture > 100 ||
        settlement.Fertility > 100;

    private static bool AreOrderedThresholds(double high, double medium, double low) =>
        IsUnit(high) && IsUnit(medium) && IsUnit(low) && high >= medium && medium >= low;

    private static bool IsUnit(double value) => value is >= 0d and <= 1d;

    private sealed class LocationGenerationCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<LocationGenerationProfile> Profiles { get; init; } = [];
    }
}
