using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MUMUsic.Desktop;

/// <summary>JSON options shared by the API, the bridge and the files in the user folder, and helpers to read request
/// bodies without a class per route.</summary>
internal static class Json
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static JsonSerializerOptions Indented { get; } = new(Options) { WriteIndented = true };

    public static byte[] Bytes(object? value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T ReadFile<T>(string path, T fallback)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    /// <summary>Writes through a temp file so a crash mid-write never leaves a truncated file behind.</summary>
    public static void WriteFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Indented));
        File.Move(temp, path, overwrite: true);
    }

    public static string? Str(this JsonObject body, string key) =>
        body[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static bool? Bool(this JsonObject body, string key) =>
        body[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    /// <summary>Items of a list as strings (numbers too: old exports and the UI send ids either way).</summary>
    public static List<string> StrList(this JsonObject body, string key) =>
        body[key] is JsonArray list ? list.Select(Text).OfType<string>().ToList() : [];

    public static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.GetValueKind() is JsonValueKind.Number => value.ToJsonString(),
        _ => null,
    };
}
