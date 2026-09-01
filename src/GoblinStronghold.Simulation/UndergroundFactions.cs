using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.Civilizations.Polities;

namespace GoblinStronghold.Simulation;

public enum UndergroundFactionKind : byte
{
    DarkDwarves = 1,
}

public enum UndergroundFactionDirective : byte
{
    Dormant = 0,
    SecureFortress = 1,
    GatherProvisions = 2,
    ExpandMines = 3,
    WageWar = 4,
}

public enum UndergroundFactionRelationKind : byte
{
    Neutral = 1,
    Wary = 2,
    Hostile = 3,
}

public readonly record struct UndergroundFactionSnapshot(
    ulong Id,
    UndergroundFactionKind Kind,
    int BandIndex,
    int TopLevel,
    int BottomLevel,
    int FortressLevel,
    bool IsActive,
    int Population,
    int Fighters,
    int Provisions,
    int OreStock,
    int Fortification,
    UndergroundFactionDirective Directive,
    ulong TargetFactionId,
    int LastUpdatedDay)
{
    public PolityId PolityId { get; init; }
}

public readonly record struct UndergroundFactionRelationSnapshot(
    ulong FirstFactionId,
    ulong SecondFactionId,
    UndergroundFactionRelationKind Kind);

internal sealed class UndergroundFactionSaveModel
{
    public ulong Id { get; set; }

    public string PolityId { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int Population { get; set; }

    public int Fighters { get; set; }

    public int Provisions { get; set; }

    public int OreStock { get; set; }

    public int Fortification { get; set; }

    public UndergroundFactionDirective Directive { get; set; }

    public ulong TargetFactionId { get; set; }

    public int LastUpdatedDay { get; set; }
}

public sealed class UndergroundFactionDirector
{
    public const int FirstFactionLevel = -6;
    public const int DepthBandSize = 10;

    private readonly WorldSeed _worldSeed;
    private readonly UndergroundCivilizationBehaviorDefinition _behavior;
    private readonly SortedDictionary<ulong, UndergroundFactionState> _factions;
    private readonly ReadOnlyCollection<UndergroundFactionRelationSnapshot> _relations;

    private UndergroundFactionDirector(
        WorldSeed worldSeed,
        CivilizationDefinition civilization,
        IEnumerable<UndergroundFactionState> factions)
    {
        _worldSeed = worldSeed;
        _ = civilization.UndergroundGeneration ?? throw new ArgumentException(
            "The underground civilization requires generation parameters.",
            nameof(civilization));
        _behavior = civilization.UndergroundBehavior ?? throw new ArgumentException(
            "The underground civilization requires behavior parameters.",
            nameof(civilization));
        _factions = new SortedDictionary<ulong, UndergroundFactionState>(
            factions.ToDictionary(faction => faction.Id));
        _relations = Array.AsReadOnly(CreateRelations(_factions.Values));
    }

    public static UndergroundFactionDirector Create(WorldSeed worldSeed, int minimumWorldLevel)
    {
        var civilization = CivilizationCatalog.Current.Get(
            CivilizationLegacyRole.DeepDwarfClan);
        return Create(worldSeed, minimumWorldLevel, civilization);
    }

    internal static UndergroundFactionDirector Create(
        WorldSeed worldSeed,
        int minimumWorldLevel,
        CivilizationDefinition civilization)
    {
        ArgumentNullException.ThrowIfNull(civilization);
        var generation = civilization.UndergroundGeneration ?? throw new InvalidDataException(
            $"Civilization '{civilization.Id}' has no underground generation parameters.");
        var factions = new List<UndergroundFactionState>();
        for (var bandIndex = 0;
             GetBandTopLevel(generation, bandIndex) >= minimumWorldLevel;
             bandIndex++)
        {
            if (DeterministicRandom.NextInt(
                    worldSeed,
                    RandomDomain.UndergroundFactions,
                    EntityId.None,
                    SimulationTick.Zero,
                    sampleKey: checked(0x464F5254UL + (ulong)bandIndex),
                    minimumInclusive: 0,
                    maximumExclusive: 100) >= generation.PresencePercent)
            {
                continue;
            }

            var topLevel = GetBandTopLevel(generation, bandIndex);
            var bottomLevel = Math.Max(
                minimumWorldLevel,
                topLevel - generation.DepthBandSize + 1);
            var id = checked(0x554E4400UL + (ulong)bandIndex + 1UL);
            var subject = new EntityId(id);
            var population = DeterministicRandom.NextInt(
                worldSeed,
                RandomDomain.UndergroundFactions,
                subject,
                SimulationTick.Zero,
                sampleKey: 1,
                minimumInclusive: generation.BaseMinimumPopulation +
                    (bandIndex * generation.MinimumPopulationPerBand),
                maximumExclusive: generation.BaseMaximumPopulationExclusive +
                    (bandIndex * generation.MaximumPopulationPerBand));
            factions.Add(new UndergroundFactionState(
                id,
                generation.LegacyKind,
                bandIndex,
                topLevel,
                bottomLevel,
                DeterministicRandom.NextInt(
                    worldSeed,
                    RandomDomain.UndergroundFactions,
                    subject,
                    SimulationTick.Zero,
                    sampleKey: 2,
                    minimumInclusive: bottomLevel,
                    maximumExclusive: topLevel + 1),
                population,
                fighters: Math.Max(
                    generation.MinimumFighters,
                    population / generation.FighterPopulationDivisor),
                provisions: checked(population * generation.ProvisionsPerCapita),
                oreStock: checked(
                    (bandIndex + generation.OrePerCapitaBandOffset) * population),
                fortification: generation.BaseFortification +
                    (bandIndex * generation.FortificationPerBand)));
        }
        return new UndergroundFactionDirector(worldSeed, civilization, factions);
    }

    public IReadOnlyList<UndergroundFactionSnapshot> CreateSnapshot() =>
        Array.AsReadOnly(_factions.Values.Select(faction => faction.ToSnapshot()).ToArray());

    public IReadOnlyList<UndergroundFactionRelationSnapshot> Relations => _relations;

    public bool HasFactions => _factions.Count > 0;

    internal IReadOnlyList<UndergroundFactionSaveModel> CreateSaveModels() =>
        Array.AsReadOnly(_factions.Values.Select(faction => faction.ToSaveModel()).ToArray());

    internal void Restore(IReadOnlyList<UndergroundFactionSaveModel> models)
    {
        if (models.Count != _factions.Count || models.Select(model => model.Id).Distinct().Count() !=
            models.Count)
        {
            throw new InvalidDataException("The save contains an invalid underground faction set.");
        }
        foreach (var model in models)
        {
            if (!_factions.TryGetValue(model.Id, out var faction))
            {
                throw new InvalidDataException("The save contains an unknown underground faction.");
            }
            faction.Restore(model, _factions.Keys);
        }
    }

    public void Advance(int deepestGoblinLevel, int absoluteDay)
    {
        foreach (var faction in _factions.Values.Where(faction =>
                     !faction.IsActive && deepestGoblinLevel <= faction.TopLevel))
        {
            faction.IsActive = true;
            faction.Directive = UndergroundFactionDirective.SecureFortress;
        }

        var active = _factions.Values.Where(faction => faction.IsActive && faction.Population > 0)
            .ToArray();
        var advancedDay = false;
        foreach (var faction in active.Where(faction => faction.LastUpdatedDay < absoluteDay))
        {
            advancedDay = true;
            faction.LastUpdatedDay = absoluteDay;
            var enemy = active.FirstOrDefault(candidate => candidate.Id != faction.Id &&
                Math.Abs(candidate.BandIndex - faction.BandIndex) <= 1 &&
                GetRelation(faction.Id, candidate.Id) == UndergroundFactionRelationKind.Hostile);
            if (enemy is not null)
            {
                faction.Directive = UndergroundFactionDirective.WageWar;
                faction.TargetFactionId = enemy.Id;
            }
            else if (faction.Provisions <
                     faction.Population * _behavior.LowProvisionPopulationMultiplier)
            {
                faction.Directive = UndergroundFactionDirective.GatherProvisions;
                faction.TargetFactionId = 0;
                faction.Provisions = checked(faction.Provisions + Math.Max(
                    1,
                    faction.Population / _behavior.ProvisionGatherPopulationDivisor));
            }
            else if (faction.OreStock <
                     faction.Population *
                     (faction.BandIndex + _behavior.OreTargetBandOffset))
            {
                faction.Directive = UndergroundFactionDirective.ExpandMines;
                faction.TargetFactionId = 0;
                faction.OreStock = checked(faction.OreStock + Math.Max(
                    1,
                    faction.Population / _behavior.OreGatherPopulationDivisor));
            }
            else
            {
                faction.Directive = UndergroundFactionDirective.SecureFortress;
                faction.TargetFactionId = 0;
                faction.Fortification = checked(faction.Fortification + 1);
            }
            faction.Provisions = Math.Max(0, faction.Provisions - Math.Max(
                1,
                faction.Population / _behavior.ProvisionConsumptionPopulationDivisor));
        }

        if (!advancedDay || absoluteDay <= 0 ||
            absoluteDay % _behavior.ConflictIntervalDays != 0)
        {
            return;
        }
        foreach (var relation in _relations.Where(relation =>
                     relation.Kind == UndergroundFactionRelationKind.Hostile))
        {
            if (!_factions.TryGetValue(relation.FirstFactionId, out var first) ||
                !_factions.TryGetValue(relation.SecondFactionId, out var second) ||
                !first.IsActive || !second.IsActive || first.Population <= 0 || second.Population <= 0)
            {
                continue;
            }
            if (Math.Abs(first.BandIndex - second.BandIndex) > 1)
            {
                continue;
            }
            ResolveConflict(first, second, absoluteDay);
        }
    }

    private UndergroundFactionRelationKind GetRelation(ulong firstId, ulong secondId)
    {
        var low = Math.Min(firstId, secondId);
        var high = Math.Max(firstId, secondId);
        return _relations.First(relation =>
            relation.FirstFactionId == low && relation.SecondFactionId == high).Kind;
    }

    private void ResolveConflict(
        UndergroundFactionState first,
        UndergroundFactionState second,
        int absoluteDay)
    {
        var firstLoss = DeterministicRandom.NextInt(
            _worldSeed,
            RandomDomain.UndergroundFactions,
            new EntityId(first.Id),
            new SimulationTick(absoluteDay),
            sampleKey: second.Id,
            minimumInclusive: 1,
            maximumExclusive: Math.Max(
                2,
                second.Fighters / _behavior.ConflictLossFighterDivisor + 1));
        var secondLoss = DeterministicRandom.NextInt(
            _worldSeed,
            RandomDomain.UndergroundFactions,
            new EntityId(second.Id),
            new SimulationTick(absoluteDay),
            sampleKey: first.Id,
            minimumInclusive: 1,
            maximumExclusive: Math.Max(
                2,
                first.Fighters / _behavior.ConflictLossFighterDivisor + 1));
        ApplyLoss(first, firstLoss);
        ApplyLoss(second, secondLoss);
    }

    private static void ApplyLoss(UndergroundFactionState faction, int loss)
    {
        faction.Population = Math.Max(0, faction.Population - loss);
        faction.Fighters = Math.Min(faction.Population, Math.Max(0, faction.Fighters - loss));
    }

    private UndergroundFactionRelationSnapshot[] CreateRelations(
        IEnumerable<UndergroundFactionState> factions)
    {
        var ordered = factions.OrderBy(faction => faction.Id).ToArray();
        var relations = new List<UndergroundFactionRelationSnapshot>();
        for (var firstIndex = 0; firstIndex < ordered.Length; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < ordered.Length; secondIndex++)
            {
                var first = ordered[firstIndex];
                var second = ordered[secondIndex];
                var roll = DeterministicRandom.NextInt(
                    _worldSeed,
                    RandomDomain.UndergroundFactions,
                    new EntityId(first.Id),
                    SimulationTick.Zero,
                    sampleKey: second.Id,
                    minimumInclusive: 0,
                    maximumExclusive: 100);
                relations.Add(new UndergroundFactionRelationSnapshot(
                    first.Id,
                    second.Id,
                    roll < _behavior.HostileRelationPercent
                        ? UndergroundFactionRelationKind.Hostile
                        : roll < _behavior.HostileRelationPercent +
                          _behavior.WaryRelationPercent
                            ? UndergroundFactionRelationKind.Wary
                            : UndergroundFactionRelationKind.Neutral));
            }
        }
        return relations.ToArray();
    }

    private static int GetBandTopLevel(
        UndergroundCivilizationGenerationDefinition generation,
        int bandIndex) =>
        checked(generation.FirstLevel - (bandIndex * generation.DepthBandSize));

    private sealed class UndergroundFactionState(
        ulong id,
        UndergroundFactionKind kind,
        int bandIndex,
        int topLevel,
        int bottomLevel,
        int fortressLevel,
        int population,
        int fighters,
        int provisions,
        int oreStock,
        int fortification)
    {
        public ulong Id { get; } = id;
        public UndergroundFactionKind Kind { get; } = kind;
        public int BandIndex { get; } = bandIndex;
        public int TopLevel { get; } = topLevel;
        public int BottomLevel { get; } = bottomLevel;
        public int FortressLevel { get; } = fortressLevel;
        public bool IsActive { get; set; }
        public int Population { get; set; } = population;
        public int Fighters { get; set; } = fighters;
        public int Provisions { get; set; } = provisions;
        public int OreStock { get; set; } = oreStock;
        public int Fortification { get; set; } = fortification;
        public UndergroundFactionDirective Directive { get; set; }
        public ulong TargetFactionId { get; set; }
        public int LastUpdatedDay { get; set; } = -1;

        public UndergroundFactionSnapshot ToSnapshot() => new(
            Id,
            Kind,
            BandIndex,
            TopLevel,
            BottomLevel,
            FortressLevel,
            IsActive,
            Population,
            Fighters,
            Provisions,
            OreStock,
            Fortification,
            Directive,
            TargetFactionId,
            LastUpdatedDay)
        {
            PolityId = CorePolityIds.CaveDwarfClan(Id),
        };

        public UndergroundFactionSaveModel ToSaveModel() => new()
        {
            Id = Id,
            PolityId = CorePolityIds.CaveDwarfClan(Id).Value,
            IsActive = IsActive,
            Population = Population,
            Fighters = Fighters,
            Provisions = Provisions,
            OreStock = OreStock,
            Fortification = Fortification,
            Directive = Directive,
            TargetFactionId = TargetFactionId,
            LastUpdatedDay = LastUpdatedDay,
        };

        public void Restore(
            UndergroundFactionSaveModel model,
            IEnumerable<ulong> knownFactionIds)
        {
            if (!PolityId.TryParse(model.PolityId, out var polityId) ||
                polityId != CorePolityIds.CaveDwarfClan(Id) ||
                model.Population < 0 || model.Fighters < 0 ||
                model.Fighters > model.Population || model.Provisions < 0 ||
                model.OreStock < 0 || model.Fortification < 0 || model.LastUpdatedDay < -1 ||
                !Enum.IsDefined(model.Directive) ||
                model.TargetFactionId != 0 && !knownFactionIds.Contains(model.TargetFactionId))
            {
                throw new InvalidDataException("The save contains invalid underground faction state.");
            }
            IsActive = model.IsActive;
            Population = model.Population;
            Fighters = model.Fighters;
            Provisions = model.Provisions;
            OreStock = model.OreStock;
            Fortification = model.Fortification;
            Directive = model.Directive;
            TargetFactionId = model.TargetFactionId;
            LastUpdatedDay = model.LastUpdatedDay;
        }
    }
}
