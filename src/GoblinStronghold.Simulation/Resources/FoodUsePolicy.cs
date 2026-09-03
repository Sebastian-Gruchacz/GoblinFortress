namespace GoblinStronghold.Simulation.Resources;

public static class FoodUsePolicy
{
    public static bool CanBePacked(FoodKind kind) => kind != FoodKind.CampSoup;

    public static bool CanBeStored(FoodKind kind) => kind != FoodKind.CampSoup;
}
