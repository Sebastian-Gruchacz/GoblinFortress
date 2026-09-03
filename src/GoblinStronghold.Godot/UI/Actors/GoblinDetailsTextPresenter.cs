using System.Globalization;
using System.Text;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Localization;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.Actors;

internal static class GoblinDetailsTextPresenter
{
    public static string Describe(
        string locale,
        ActorSnapshot actor,
        int daysPerYear,
        int absoluteMaximumHealth,
        IReadOnlyList<string> logisticsDuty,
        Func<EquipmentSlot, string> describeSlot,
        Func<ResourceVariant, string> describeItem,
        Func<ActorJobSnapshot, string> describeJob)
    {
        var equipment = actor.Loadout.Items.Count == 0
            ? Text(locale, "none")
            : string.Join(", ", actor.Loadout.Items.Select(item => Format(
                locale,
                "equipment-item",
                describeSlot(item.Slot),
                describeItem(item.Variant),
                item.Weight)));
        var text = new StringBuilder()
            .AppendLine(Format(locale, "identity", actor.Name, actor.Id))
            .AppendLine(Format(locale, "position", actor.Position))
            .AppendLine(DescribeAge(locale, actor, daysPerYear, absoluteMaximumHealth))
            .AppendLine(actor.BleedingTicksRemaining > 0
                ? Format(locale, "bleeding", actor.BleedingTicksRemaining)
                : Text(locale, "not-bleeding"))
            .AppendLine()
            .AppendLine(Format(locale, "known-skills", DescribeSkills(locale, actor.KnownSkills)))
            .AppendLine(Format(locale, "experience", DescribeExperience(locale, actor.Experience)))
            .AppendLine(Format(
                locale,
                "work-preferences",
                DescribePreference(locale, actor.WorkPreferences.Foraging),
                DescribePreference(locale, actor.WorkPreferences.Hauling),
                DescribePreference(locale, actor.WorkPreferences.Building)))
            .AppendLine(Format(locale, "known-traits", DescribeTraits(locale, actor.KnownTraits)))
            .AppendLine(Format(locale, "mana", actor.Mana, actor.MaximumMana))
            .AppendLine(Format(locale, "equipment", equipment))
            .AppendLine(Format(
                locale,
                "encumbrance",
                actor.Loadout.EquipmentWeight,
                actor.Loadout.PackWeight,
                actor.Loadout.CarriedCargoWeight,
                actor.Loadout.TotalWeight,
                actor.Loadout.CarryingCapacity))
            .AppendLine(Format(
                locale,
                "logistics-duty",
                logisticsDuty.Count == 0
                    ? Text(locale, "no-assignment")
                    : string.Join(", ", logisticsDuty)))
            .AppendLine(Format(
                locale,
                "tactical-order",
                DescribeTacticalOrder(locale, actor.TacticalOrder)))
            .AppendLine()
            .AppendLine(Text(locale, "plan-heading"));

        if (actor.Plan.Count == 0)
        {
            text.AppendLine(Text(locale, "plan-empty"));
        }
        else
        {
            for (var index = 0; index < actor.Plan.Count; index++)
            {
                text.AppendLine(Format(
                    locale,
                    "plan-line",
                    index + 1,
                    DescribePlanEntry(locale, actor.Plan[index])));
            }
        }

        return text.AppendLine()
            .AppendLine(Text(locale, "current-job-heading"))
            .AppendLine(describeJob(actor.Job))
            .AppendLine(Format(
                locale,
                "job-phase-stage",
                JobPhase(locale, actor.Job.Phase),
                JobStage(locale, actor.Job.Stage)))
            .AppendLine(Format(
                locale,
                "job-target-route",
                actor.Job.Target,
                actor.Job.RemainingRouteSteps))
            .AppendLine(Format(locale, "job-work-left", actor.Job.RemainingWorkTicks))
            .AppendLine(Format(
                locale,
                "job-source-destination",
                actor.Job.SourceStackId,
                actor.Job.DestinationZoneId))
            .Append(Format(locale, "job-reservation", actor.Job.ReservedQuantity))
            .ToString();
    }

    public static string DescribeSkills(string locale, GoblinSkill skills) => JoinFlags(
        locale,
        "skills",
        Enum.GetValues<GoblinSkill>()
            .Where(skill => skill != GoblinSkill.None && skills.HasFlag(skill))
            .Select(skill => skill.ToString()));

    public static string DescribeTraits(string locale, GoblinTrait traits) => JoinFlags(
        locale,
        "traits",
        Enum.GetValues<GoblinTrait>()
            .Where(trait => trait != GoblinTrait.None && traits.HasFlag(trait))
            .Select(trait => trait.ToString()));

    private static string DescribeAge(
        string locale,
        ActorSnapshot actor,
        int daysPerYear,
        int absoluteMaximumHealth)
    {
        var years = (double)actor.AgeDays / daysPerYear;
        if (actor.IsJuvenile)
        {
            return Format(locale, "age-juvenile", actor.AgeDays, years);
        }
        return actor.IsElderly
            ? Format(
                locale,
                "age-elderly",
                actor.AgeDays,
                years,
                actor.SenescenceProgress,
                actor.EffectiveMaximumHealth,
                absoluteMaximumHealth)
            : Format(locale, "age-adult", actor.AgeDays, years);
    }

    private static string DescribeExperience(string locale, GoblinExperienceSnapshot experience) =>
        Format(
            locale,
            "experience-values",
            GoblinExperienceSnapshot.GetLevel(experience.Foraging),
            GoblinExperienceSnapshot.GetProgressToNextLevel(experience.Foraging),
            GoblinExperienceSnapshot.GetLevel(experience.Hauling),
            GoblinExperienceSnapshot.GetProgressToNextLevel(experience.Hauling),
            GoblinExperienceSnapshot.GetLevel(experience.Building),
            GoblinExperienceSnapshot.GetProgressToNextLevel(experience.Building));

    private static string DescribePreference(string locale, int preference) =>
        Text(locale, $"preference-{preference}");

    private static string DescribeTacticalOrder(
        string locale,
        ActorTacticalOrderSnapshot order) => order.Kind switch
        {
            ActorTacticalOrderKind.Patrol => Format(
                locale,
                "tactical-patrol",
                order.PatrolPoints.Count,
                order.PatrolPointIndex + 1),
            ActorTacticalOrderKind.AttackArea =>
                Format(locale, "tactical-attack", order.Center, order.Radius),
            ActorTacticalOrderKind.HuntArea =>
                Format(locale, "tactical-hunt", order.Center, order.Radius),
            _ => Text(locale, "tactical-none"),
        };

    private static string DescribePlanEntry(string locale, ActorPlanEntrySnapshot entry)
    {
        var action = entry.Kind switch
        {
            ActorPlanIntentKind.CurrentJob =>
                Format(locale, "plan-current-job", JobKind(locale, entry.JobKind)),
            ActorPlanIntentKind.Eat => Text(locale, "plan-eat"),
            ActorPlanIntentKind.FindFood => Text(locale, "plan-find-food"),
            ActorPlanIntentKind.Drink => Text(locale, "plan-drink"),
            ActorPlanIntentKind.RefillWater => Text(locale, "plan-refill-water"),
            ActorPlanIntentKind.Rest => Text(locale, "plan-rest"),
            ActorPlanIntentKind.ResumeSuspendedJob => Format(
                locale,
                "plan-resume",
                JobKind(locale, entry.JobKind),
                entry.Target),
            ActorPlanIntentKind.NextPublicWork => Format(
                locale,
                "plan-next-work",
                JobKind(locale, entry.JobKind),
                entry.Target,
                entry.WorkOrderId),
            _ => Text(locale, "plan-unknown"),
        };
        return Format(locale, "plan-priority", action, entry.Priority);
    }

    private static string JobKind(string locale, ActorJobKind kind) =>
        TranslationCatalog.Get(locale, "interface", "actor-job-kinds", kind.ToString());

    private static string JobPhase(string locale, ActorJobPhase phase) =>
        TranslationCatalog.Get(locale, "interface", "actor-job-phases", phase.ToString());

    private static string JobStage(string locale, ActorJobStage stage) =>
        TranslationCatalog.Get(locale, "interface", "actor-job-stages", stage.ToString());

    private static string JoinFlags(
        string locale,
        string subsection,
        IEnumerable<string> keys)
    {
        var descriptions = keys
            .Select(key => TranslationCatalog.Get(locale, "interface", subsection, key))
            .ToArray();
        return descriptions.Length == 0
            ? TranslationCatalog.Get(locale, "interface", subsection, "None")
            : string.Join(", ", descriptions);
    }

    private static string Text(string locale, string key) =>
        TranslationCatalog.Get(locale, "interface", "goblin-details", key);

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
