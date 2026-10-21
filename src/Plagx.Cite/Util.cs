using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Plagx.Cite;

/// <summary>Date, text and page helpers shared by the styles.</summary>
internal static class Util
{
    public static readonly string[] Months =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    public static readonly string[] MonthsMla =
    {
        "Jan.", "Feb.", "Mar.", "Apr.", "May", "June",
        "July", "Aug.", "Sept.", "Oct.", "Nov.", "Dec.",
    };

    public readonly record struct Date(int? Year, int? Month, int? Day);

    /// <summary>Year, month and day from a CSL <c>issued</c> date or a plain <c>year</c> field.</summary>
    public static Date GetDate(JsonObject reference)
    {
        if (reference["issued"] is JsonObject issued && issued["date-parts"] is JsonArray dp && dp.Count > 0
            && dp[0] is JsonArray parts && parts.Count > 0 && Json.Filled(parts[0]))
        {
            int? Part(int i) => parts.Count > i ? Json.Int(parts[i]) : null;
            return new Date(Json.Int(parts[0]), Part(1), Part(2));
        }
        if (Json.Filled(reference["year"])) return new Date(Json.Int(reference["year"]), null, null);
        return default;
    }

    public static string? GetYear(JsonObject reference)
    {
        var year = GetDate(reference).Year;
        return year is int y && y != 0 ? y.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static readonly Regex Combining = new("[̀-ͯ]");
    private static readonly Regex NonAlnum = new("[^a-z0-9]+");

    /// <summary>Lowercase, strip accents and punctuation; used for comparisons and keys.</summary>
    public static string NormalizeText(string? value)
    {
        var s = Combining.Replace((value ?? "").Normalize(NormalizationForm.FormKD), "").ToLowerInvariant();
        return NonAlnum.Replace(s, " ").Trim();
    }

    private static readonly Regex Terminal = new("[.?!][\"”’]?\\z");

    /// <summary>Period to append unless the text already ends with terminal punctuation.</summary>
    public static string Dot(string value) => Terminal.IsMatch(value.Trim()) ? "" : ".";

    private static readonly Regex Dashes = new(@"\s*[-–—]+\s*");

    public static string PageRange(string page, string dash = "–") => Dashes.Replace(page.Trim(), dash);

    public static bool IsPageRange(string page) => page.IndexOfAny(new[] { '-', '–', '—' }) >= 0;

    public static string Ordinal(JsonNode? value)
    {
        var s = Json.Str(value);
        int n;
        if (value is JsonValue v && v.GetValueKind() == JsonValueKind.Number)
        {
            var d = Json.Number(v);
            if (d != Math.Floor(d)) return s;
            n = (int)d;
        }
        else if (s.Trim().Length > 0 && s.Trim().All(char.IsAsciiDigit)) n = int.Parse(s.Trim(), CultureInfo.InvariantCulture);
        else return s;

        if (n % 100 is >= 11 and <= 13) return $"{n}th";
        return n + ((n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    /// <summary>Preferred link for a reference: a valid DOI first, then URL.</summary>
    public static string Link(JsonObject reference)
    {
        var doi = Json.Filled(reference["DOI"]) ? Doi.Normalize(Json.Str(reference["DOI"])) : null;
        if (doi != null) return "https://doi.org/" + doi;
        return Json.Text(reference, "URL");
    }
}
