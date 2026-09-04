using System.Globalization;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Localization;

namespace GoblinStronghold.GodotClient.UI.Actors;

internal static class ActorJobTextPresenter
{
    public static string Describe(string locale, ActorJobSnapshot job) => job.Kind switch
    {
        ActorJobKind.Forage when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "forage-travel", job.Target),
        ActorJobKind.Forage when job.Phase == ActorJobPhase.Working =>
            Format(locale, "forage-working", job.RemainingWorkTicks),
        ActorJobKind.Haul when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "haul-collect-travel", job.ReservedQuantity),
        ActorJobKind.Haul when job.Stage == ActorJobStage.Collecting =>
            Format(locale, "haul-collect-working", job.ReservedQuantity, job.RemainingWorkTicks),
        ActorJobKind.Haul when job.Stage == ActorJobStage.Delivering &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "haul-deliver-travel", job.ReservedQuantity),
        ActorJobKind.Haul when job.Stage == ActorJobStage.Delivering =>
            Format(locale, "haul-deliver-working", job.ReservedQuantity, job.RemainingWorkTicks),
        ActorJobKind.SupplyConstruction when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "construction-supply-collect-travel", job.ReservedQuantity),
        ActorJobKind.SupplyConstruction when job.Stage == ActorJobStage.Collecting =>
            Format(locale, "construction-supply-collect-working",
                job.ReservedQuantity, job.RemainingWorkTicks),
        ActorJobKind.SupplyConstruction when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "construction-supply-deliver-travel", job.ReservedQuantity),
        ActorJobKind.SupplyConstruction =>
            Format(locale, "construction-supply-deliver-working", job.RemainingWorkTicks),
        ActorJobKind.BuildConstruction when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "construction-build-travel", job.Target),
        ActorJobKind.BuildConstruction =>
            Format(locale, "construction-build-working", job.RemainingWorkTicks),
        ActorJobKind.DismantleConstruction when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "construction-dismantle-travel", job.Target),
        ActorJobKind.DismantleConstruction =>
            Format(locale, "construction-dismantle-working", job.RemainingWorkTicks),
        ActorJobKind.SupplyCrafting when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "crafting-supply-collect-travel", job.ReservedQuantity),
        ActorJobKind.SupplyCrafting when job.Stage == ActorJobStage.Collecting =>
            Format(locale, "crafting-supply-collect-working", job.ReservedQuantity),
        ActorJobKind.SupplyCrafting when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "crafting-supply-deliver-travel", job.ReservedQuantity),
        ActorJobKind.SupplyCrafting =>
            Format(locale, "crafting-supply-deliver-working", job.RemainingWorkTicks),
        ActorJobKind.Craft when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "craft-travel", job.Target),
        ActorJobKind.Craft => Format(locale, "craft-working", job.RemainingWorkTicks),
        ActorJobKind.ClearConstructionSite when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling => Text(locale, "clear-site-travel"),
        ActorJobKind.ClearConstructionSite when job.Stage == ActorJobStage.Collecting =>
            Format(locale, "clear-site-pickup", job.RemainingWorkTicks),
        ActorJobKind.ClearConstructionSite when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "clear-site-carry", job.ReservedQuantity),
        ActorJobKind.ClearConstructionSite =>
            Format(locale, "clear-site-drop", job.RemainingWorkTicks),
        ActorJobKind.Rest when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "rest-travel", job.Target),
        ActorJobKind.Rest => Format(locale, "rest-working", job.RemainingWorkTicks),
        ActorJobKind.Collapsed => Format(locale, "collapsed", job.RemainingWorkTicks),
        ActorJobKind.Eat when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "eat-travel", job.Target),
        ActorJobKind.Eat => Format(locale, "eat-working", job.RemainingWorkTicks),
        ActorJobKind.Explore => Format(locale, "explore", job.Target),
        ActorJobKind.Move => Format(locale, "move", job.Target),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningFood &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "resupply-food-travel", job.Target),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningFood =>
            Format(locale, "resupply-food-working", job.RemainingWorkTicks),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningWater &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "resupply-water-travel", job.Target),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningWater =>
            Format(locale, "resupply-water-working", job.RemainingWorkTicks),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningAmmo &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "resupply-ammo-travel", job.Target),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningAmmo =>
            Format(locale, "resupply-ammo-working", job.ReservedQuantity, job.RemainingWorkTicks),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningEquipment &&
            job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "resupply-equipment-travel", job.Target),
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningEquipment =>
            Format(locale, "resupply-equipment-working", job.RemainingWorkTicks),
        ActorJobKind.ClearVegetation when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "clear-vegetation-travel", job.Target),
        ActorJobKind.ClearVegetation =>
            Format(locale, "clear-vegetation-working", job.RemainingWorkTicks),
        ActorJobKind.FellTree when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "fell-tree-travel", job.Target),
        ActorJobKind.FellTree => Format(locale, "fell-tree-working", job.RemainingWorkTicks),
        ActorJobKind.QuarryBoulder when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "quarry-boulder-travel", job.Target),
        ActorJobKind.QuarryBoulder =>
            Format(locale, "quarry-boulder-working", job.RemainingWorkTicks),
        ActorJobKind.MineRock when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "mine-rock-travel", job.Target),
        ActorJobKind.MineRock => Format(locale, "mine-rock-working", job.RemainingWorkTicks),
        ActorJobKind.StripFloor when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "strip-floor-travel", job.Target),
        ActorJobKind.StripFloor => Format(locale, "strip-floor-working", job.RemainingWorkTicks),
        ActorJobKind.CarveRamp when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "carve-ramp-travel", job.Target),
        ActorJobKind.CarveRamp => Format(locale, "carve-ramp-working", job.RemainingWorkTicks),
        ActorJobKind.TendBud when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "tend-bud-travel", job.Target),
        ActorJobKind.TendBud => Format(locale, "tend-bud-working", job.RemainingWorkTicks),
        ActorJobKind.HuntAnimal when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "hunt-animal-travel", job.Target),
        ActorJobKind.HuntAnimal => Format(locale, "hunt-animal-working", job.RemainingWorkTicks),
        ActorJobKind.CleanBlood when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "clean-blood-travel", job.Target),
        ActorJobKind.CleanBlood => Format(locale, "clean-blood-working", job.RemainingWorkTicks),
        ActorJobKind.LootRaid when job.Stage == ActorJobStage.Collecting =>
            Format(locale, "loot-raid-collect", job.Target),
        ActorJobKind.LootRaid => Format(locale, "loot-raid-return", job.Target),
        ActorJobKind.RecoverRaidCorpse when job.Stage == ActorJobStage.Collecting =>
            Format(locale, "recover-corpse-collect", job.Target),
        ActorJobKind.RecoverRaidCorpse => Format(locale, "recover-corpse-return", job.Target),
        ActorJobKind.ConsumeRaidCorpse when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "consume-corpse-travel", job.Target),
        ActorJobKind.ConsumeRaidCorpse =>
            Format(locale, "consume-corpse-working", job.RemainingWorkTicks),
        ActorJobKind.GuardWatchtower when job.Phase == ActorJobPhase.Traveling =>
            Format(locale, "watchtower-travel", job.Target),
        ActorJobKind.GuardWatchtower => Format(locale, "watchtower-guarding", job.Target),
        _ => Text(locale, "idle"),
    };

    private static string Text(string locale, string key) =>
        TranslationCatalog.Get(locale, "interface", "actor-jobs", key);

    private static string Format(string locale, string key, params object?[] arguments) =>
        string.Format(GetCulture(locale), Text(locale, key), arguments);

    private static CultureInfo GetCulture(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }
}
