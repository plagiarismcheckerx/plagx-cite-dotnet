using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Plagx.Cite;

/// <summary>Name parsing and initials.</summary>
internal static class Names
{
    public sealed record Name(string? Literal, string Family = "", string Given = "", string? Suffix = null);

    /// <summary>
    /// Accepts a CSL name ({family, given, suffix} or {literal}) or a string
    /// ("Smith, John" / "John Smith").
    /// </summary>
    public static Name Parts(JsonNode? name)
    {
        if (name is JsonValue v && v.TryGetValue<string>(out var s)) return ParseString(s);
        if (name is not JsonObject o) return new Name(null);
        if (Json.Filled(o["literal"])) return new Name(Json.Str(o["literal"]));
        return new Name(null, Json.Text(o, "family"), Json.Text(o, "given"), Json.Filled(o["suffix"]) ? Json.Str(o["suffix"]) : null);
    }

    private static Name ParseString(string value)
    {
        var s = value.Trim();
        var comma = s.IndexOf(',');
        if (comma >= 0) return new Name(null, s[..comma].Trim(), s[(comma + 1)..].Trim());
        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return new Name(null, s);
        return new Name(null, parts[^1], string.Join(" ", parts[..^1]));
    }

    private static readonly Regex DottedLetter = new(@"\p{L}\.");

    /// <summary>"John Ronald" -> "J. R.", "J.R.R." -> "J. R. R.", "Jean-Paul" -> "J.-P."</summary>
    public static string Initials(string given)
    {
        var words = given.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var dotted = DottedLetter.Matches(part).Select(m => m.Value).ToList();
            if (dotted.Count > 0 && string.Concat(dotted) == part) return string.Join(" ", dotted.Select(d => d.ToUpperInvariant()));
            return string.Join("-", part.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(seg => char.ToUpperInvariant(seg[0]) + "."));
        });
        return string.Join(" ", words);
    }

    /// <summary>Family name (or literal) used for in-text citations and sorting.</summary>
    public static string FamilyName(JsonNode? name)
    {
        var n = Parts(name);
        return n.Literal ?? n.Family;
    }
}
