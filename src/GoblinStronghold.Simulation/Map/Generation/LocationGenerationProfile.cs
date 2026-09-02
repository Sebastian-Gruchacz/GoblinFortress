using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Map.Generation;

public sealed record RiverGenerationProfile(
    double StartY,
    double Slope,
    double MeanderAmplitude,
    double MinimumHalfWidth,
    double HalfWidthRatio,
    double DeepWaterRatio,
    double BankNoiseScale,
    double BranchJunctionX,
    double BranchEndY,
    double BranchHalfWidthScale,
    double BranchMeanderAmplitude);

public sealed record WetlandGenerationProfile(
    double LeftBoundary,
    double BottomBoundary,
    double BottomRange,
    double BoundaryNoiseScale,
    double BoundaryWarpX,
    double BoundaryWarpY,
    double TerrainWarpX,
    double MoistureWarpY,
    double InfluenceWeight,
    double MoistureWeight,
    double DeepInfluenceThreshold,
    double DeepWetnessThreshold,
    double DeepTerrainThreshold,
    double ShallowInfluenceThreshold,
    double ShallowWetnessThreshold,
    double MudInfluenceThreshold,
    double MudWetnessThreshold);

public sealed record SettlementGenerationProfile(
    double NormalizedX,
    double NormalizedY,
    int PadWidth,
    int PadHeight,
    byte Moisture,
    byte Fertility);

public sealed record RoadGenerationProfile(
    double NorthEntryX,
    double SouthEntryX,
    double MeanderAmplitude,
    double JunctionY,
    double JunctionEndX,
    int HalfWidth);

public sealed record ReliefGenerationProfile(
    double DeepFloorThreshold,
    double ValleyWidthRatio,
    double UplandStart,
    double UplandRange,
    double FoothillCenterX,
    double FoothillCenterY,
    double FoothillRadiusX,
    double FoothillRadiusY,
    double BroadWeight,
    double RidgeWeight,
    double UplandWeight,
    double FoothillWeight,
    double ValleyPenalty,
    double HighThreshold,
    double RaisedThreshold,
    double DepressionThreshold,
    double MinimumDepressionRiverDistance);

public sealed record LocationGenerationProfile(
    ContentId Id,
    ContentId ClimateProfileId,
    string Character,
    int DefaultDimension,
    int MinimumDimension,
    int MaximumDimension,
    RiverGenerationProfile River,
    RoadGenerationProfile Road,
    WetlandGenerationProfile Wetland,
    SettlementGenerationProfile GoblinSettlement,
    SettlementGenerationProfile HumanSettlement,
    ReliefGenerationProfile Relief);
