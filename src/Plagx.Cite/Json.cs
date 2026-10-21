using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plagx.Cite;

/// <summary>Reads CSL-JSON fields with JavaScript-style truthiness, so every port gives the same output.</summary>
internal static class Json
{
    /// <summary>False for null, false, "", 0 and empty arrays (the string "0" counts as filled).</summary>
    public static bool Filled(JsonNode? node) => node switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject => true,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>().Length > 0,
            JsonValueKind.Number => Number(v) != 0,
            JsonValueKind.False or JsonValueKind.Null => false,
            _ => true,
        },
        _ => false,
    };

    /// <summary>The node as text: strings as is, numbers and booleans as written.</summary>
    public static string Str(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String) return v.GetValue<string>();
        return node.ToJsonString();
    }

    /// <summary>The field as text when filled, otherwise "".</summary>
    public static string Text(JsonObject obj, string key) => Filled(obj[key]) ? Str(obj[key]) : "";

    public static JsonArray Arr(JsonNode? node) => node as JsonArray ?? new JsonArray();

    /// <summary>A whole number from a number or numeric string, or null.</summary>
    public static int? Int(JsonNode? node)
    {
        if (!Filled(node)) return null;
        var s = Str(node).Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)d : null;
    }

    /// <summary>A number value as double, whether it was parsed from JSON or created in code (int, long, decimal...).</summary>
    public static double Number(JsonValue v) => double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture);

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();
}
