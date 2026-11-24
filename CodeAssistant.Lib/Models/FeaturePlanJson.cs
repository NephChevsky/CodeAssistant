using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAssistant.Lib.Models
{
    /// <summary>Canonical JSON settings for <see cref="FeaturePlan"/>: camelCase, enums as strings, indented.</summary>
    public static class FeaturePlanJson
    {
        public static JsonSerializerOptions Options { get; } = CreateOptions();

        public static string Serialize(FeaturePlan plan)
        {
            return JsonSerializer.Serialize(plan, Options);
        }

        public static FeaturePlan Deserialize(string json)
        {
            return JsonSerializer.Deserialize<FeaturePlan>(json, Options) ?? throw new JsonException("Plan JSON is empty.");
        }

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }
    }
}
