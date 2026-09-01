using System.Text.Json;
using System.Text.Json.Nodes;
using GoblinStronghold.GodotClient.Application.Profiles;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal sealed class GameSessionPreferences
{
    private const string SavePropertyName = "clientPreferences";
    private readonly Dictionary<string, ResourceVariant> _constructionMaterials =
        new(StringComparer.OrdinalIgnoreCase);

    internal GameSessionPreferences(string? profileName = null)
    {
        ProfileName = GameProfileName.TryNormalize(profileName, out var normalized)
            ? normalized
            : string.Empty;
    }

    internal string ProfileName { get; }

    internal bool TryGetConstructionMaterial(string group, out ResourceVariant variant) =>
        _constructionMaterials.TryGetValue(group, out variant);

    internal void SetConstructionMaterial(string group, ResourceVariant variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        if (variant == ResourceVariant.None)
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }
        _constructionMaterials[group] = variant;
    }

    internal string AddToSave(string simulationJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(simulationJson);
        var root = JsonNode.Parse(simulationJson)?.AsObject()
            ?? throw new JsonException("Simulation save root is missing.");
        root[SavePropertyName] = new JsonObject
        {
            ["profileName"] = ProfileName,
            ["constructionMaterials"] = new JsonObject(
                _constructionMaterials
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => KeyValuePair.Create<string, JsonNode?>(
                        item.Key,
                        JsonValue.Create(item.Value.ToString())))),
        };
        return root.ToJsonString();
    }

    internal static GameSessionPreferences FromSave(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(SavePropertyName, out var client) ||
            client.ValueKind != JsonValueKind.Object)
        {
            return new GameSessionPreferences();
        }

        var profileName = client.TryGetProperty("profileName", out var profileNameValue) &&
            profileNameValue.ValueKind == JsonValueKind.String
                ? profileNameValue.GetString()
                : null;
        var preferences = new GameSessionPreferences(profileName);
        if (!client.TryGetProperty("constructionMaterials", out var materials) ||
            materials.ValueKind != JsonValueKind.Object)
        {
            return preferences;
        }

        foreach (var property in materials.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                Enum.TryParse<ResourceVariant>(
                    property.Value.GetString(), ignoreCase: true, out var variant) &&
                variant != ResourceVariant.None)
            {
                preferences._constructionMaterials[property.Name] = variant;
            }
        }
        return preferences;
    }
}
