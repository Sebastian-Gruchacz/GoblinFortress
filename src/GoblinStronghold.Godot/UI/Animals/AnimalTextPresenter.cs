using System.Globalization;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Localization;

namespace GoblinStronghold.GodotClient.UI.Animals;

internal static class AnimalTextPresenter
{
    public static string Describe(string locale, AnimalSnapshot animal)
    {
        var attackDamage = AnimalCombatPolicy.GetAttackDamage(animal.Kind, animal.Position);
        var threat = attackDamage > 0
            ? Format(locale, "attack", attackDamage)
            : string.Empty;

        return Format(
            locale,
            "summary",
            Text(locale, "animal-kinds", animal.Kind.ToString()),
            Text(locale, "animal-sexes", animal.Sex.ToString()),
            Text(locale, "animal-ages", animal.IsAdult ? "adult" : "young"),
            Text(locale, "animal-activities", animal.Activity.ToString()),
            animal.Health,
            animal.MaximumHealth,
            threat);
    }

    private static string Text(string locale, string subsection, string key) =>
        TranslationCatalog.Get(locale, "interface", subsection, key);

    private static string Format(string locale, string key, params object?[] arguments) =>
        string.Format(
            GetCulture(locale),
            Text(locale, "animal-description", key),
            arguments);

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
