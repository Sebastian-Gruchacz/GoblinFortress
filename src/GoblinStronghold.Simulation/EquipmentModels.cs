using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public enum EquipmentSlot : byte
{
    Head = 1,
    Torso = 2,
    MainHand = 3,
    RangedWeapon = 4,
    Ammunition = 5,
    RingLeft = 6,
    RingRight = 7,
    Amulet = 8,
    Belt1 = 9,
    Belt2 = 10,
    Belt3 = 11,
    Belt4 = 12,
    Waterskin = 13,
    Backpack = 14,
    Utility = 15,
}

public readonly record struct EquipmentItemDefinition(
    PersonalEquipment Equipment,
    EquipmentSlot Slot,
    ResourceVariant Variant,
    int Weight,
    int Quality);

public readonly record struct EquippedItemSnapshot(
    PersonalEquipment Equipment,
    EquipmentSlot Slot,
    ResourceVariant Variant,
    int Weight);

public sealed class EquipmentLoadoutSnapshot : IEquatable<EquipmentLoadoutSnapshot>
{
    public EquipmentLoadoutSnapshot(
        IEnumerable<EquippedItemSnapshot> items,
        int packWeight,
        int carriedCargoWeight,
        int carryingCapacity)
    {
        Items = new ReadOnlyCollection<EquippedItemSnapshot>(items.ToArray());
        EquipmentWeight = Items.Sum(item => item.Weight);
        PackWeight = packWeight;
        CarriedCargoWeight = carriedCargoWeight;
        CarryingCapacity = carryingCapacity;
    }

    public IReadOnlyList<EquippedItemSnapshot> Items { get; }

    public int EquipmentWeight { get; }

    public int PackWeight { get; }

    public int CarriedCargoWeight { get; }

    public int TotalWeight => EquipmentWeight + PackWeight + CarriedCargoWeight;

    public int CarryingCapacity { get; }

    public bool Equals(EquipmentLoadoutSnapshot? other) =>
        other is not null &&
        EquipmentWeight == other.EquipmentWeight &&
        PackWeight == other.PackWeight &&
        CarriedCargoWeight == other.CarriedCargoWeight &&
        CarryingCapacity == other.CarryingCapacity &&
        Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) =>
        obj is EquipmentLoadoutSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EquipmentWeight);
        hash.Add(PackWeight);
        hash.Add(CarriedCargoWeight);
        hash.Add(CarryingCapacity);
        foreach (var item in Items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}

public static class EquipmentCatalog
{
    private static readonly EquipmentItemDefinition[] Definitions =
    [
        new(PersonalEquipment.RagClothes, EquipmentSlot.Torso,
            ResourceVariant.EquipmentRagClothes, 1, 1),
        new(PersonalEquipment.HideClothes, EquipmentSlot.Torso,
            ResourceVariant.EquipmentHideClothes, 2, 3),
        new(PersonalEquipment.ReedClothes, EquipmentSlot.Torso,
            ResourceVariant.EquipmentReedClothes, 1, 2),
        new(PersonalEquipment.BoneKnife, EquipmentSlot.MainHand,
            ResourceVariant.EquipmentBoneKnife, 1, 30),
        new(PersonalEquipment.WoodenAxe, EquipmentSlot.MainHand,
            ResourceVariant.EquipmentWoodenAxe, 3, 100),
        new(PersonalEquipment.PrimitivePickaxe, EquipmentSlot.MainHand,
            ResourceVariant.EquipmentPrimitivePickaxe, 4, 110),
        new(PersonalEquipment.ReinforcedPickaxe, EquipmentSlot.MainHand,
            ResourceVariant.EquipmentReinforcedPickaxe, 5, 160),
        new(PersonalEquipment.FightingStick, EquipmentSlot.MainHand,
            ResourceVariant.EquipmentFightingStick, 2, 80),
        new(PersonalEquipment.StoneClub, EquipmentSlot.MainHand,
            ResourceVariant.EquipmentStoneClub, 4, 140),
        new(PersonalEquipment.PrimitiveSling, EquipmentSlot.RangedWeapon,
            ResourceVariant.EquipmentPrimitiveSling, 1, 1),
        new(PersonalEquipment.PrimitiveWaterskin, EquipmentSlot.Waterskin,
            ResourceVariant.EquipmentPrimitiveWaterskin, 1, 1),
        new(PersonalEquipment.WoodenBucket, EquipmentSlot.Utility,
            ResourceVariant.EquipmentWoodenBucket, 2, 1),
    ];

    public static IReadOnlyList<EquipmentItemDefinition> GetDefinitions(
        PersonalEquipment equipment) => Definitions
        .Where(definition => equipment.HasFlag(definition.Equipment))
        .ToArray();

    public static EquipmentItemDefinition? FindDefinition(PersonalEquipment equipment)
    {
        var definition = Definitions.FirstOrDefault(item => item.Equipment == equipment);
        return definition.Equipment == PersonalEquipment.None ? null : definition;
    }

    public static EquipmentItemDefinition? FindDefinition(ResourceVariant variant)
    {
        var definition = Definitions.FirstOrDefault(item => item.Variant == variant);
        return definition.Equipment == PersonalEquipment.None ? null : definition;
    }

    public static bool IsUpgrade(
        PersonalEquipment currentEquipment,
        PersonalEquipment candidateEquipment)
    {
        if (FindDefinition(candidateEquipment) is not { } candidate)
        {
            return false;
        }

        var current = GetDefinitions(currentEquipment)
            .Where(item => item.Slot == candidate.Slot)
            .ToArray();
        return current.Length == 0 || current.All(item => candidate.Quality > item.Quality);
    }

    public static IReadOnlyList<EquipmentItemDefinition> GetReplacedDefinitions(
        PersonalEquipment currentEquipment,
        PersonalEquipment candidateEquipment)
    {
        if (FindDefinition(candidateEquipment) is not { } candidate)
        {
            return [];
        }

        return GetDefinitions(currentEquipment)
            .Where(item => item.Slot == candidate.Slot &&
                item.Equipment != candidateEquipment)
            .ToArray();
    }
}
