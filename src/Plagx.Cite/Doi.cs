using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Plagx.Cite;

/// <summary>Thrown when a DOI lookup fails or the DOI can't be found.</summary>
public sealed class DoiLookupException : Exception
{
    /// <summary>HTTP status returned by the server.</summary>
    public int StatusCode { get; }

    /// <summary>Creates the exception.</summary>
    public DoiLookupException(string message, int statusCode) : base(message) => StatusCode = statusCode;
}

/// <summary>DOI normalising, extraction and lookup (Crossref, with a doi.org fallback).</summary>
internal static class Doi
{
    private static readonly Regex Pattern = new(@"10\.[0-9]{4,9}/[^\s""'<>]+", RegexOptions.IgnoreCase);
    private static readonly Regex Prefix = new(@"^(?:https?://(?:dx\.)?doi\.org/|doi:\s*)", RegexOptions.IgnoreCase);
    private static readonly Regex Bare = new(@"^10\.[0-9]{4,9}/\S+\z");
    private static readonly Regex TrailingPunctuation = new(@"[.,;:]+\z");

    private static readonly Dictionary<string, string> CrossrefTypes = new()
    {
        ["journal-article"] = "article-journal",
        ["book"] = "book",
        ["monograph"] = "book",
        ["edited-book"] = "book",
        ["reference-book"] = "book",
        ["book-chapter"] = "chapter",
        ["book-section"] = "chapter",
        ["book-part"] = "chapter",
        ["reference-entry"] = "entry-encyclopedia",
        ["proceedings-article"] = "paper-conference",
        ["report"] = "report",
        ["dissertation"] = "thesis",
        ["posted-content"] = "article",
        ["dataset"] = "dataset",
    };

    private static readonly Dictionary<string, string> Entities = new()
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
    };

    private static string TrimTrailing(string doi)
    {
        var s = TrailingPunctuation.Replace(doi, "");
        // Drop a closing bracket that has no opening partner inside the DOI.
        while (s.Length > 0 && s[^1] is ')' or ']')
        {
            var close = s[^1];
            var open = close == ')' ? '(' : '[';
            if (s.Count(c => c == open) >= s.Count(c => c == close)) break;
            s = TrailingPunctuation.Replace(s[..^1], "");
        }
        return s;
    }

    public static string? Normalize(string? value)
    {
        var s = Uri.UnescapeDataString(Prefix.Replace((value ?? "").Trim(), ""));
        return Bare.IsMatch(s) ? TrimTrailing(s) : null;
    }

    public static List<string> Extract(string? text)
    {
        var seen = new HashSet<string>();
        var found = new List<string>();
        foreach (Match m in Pattern.Matches(text ?? ""))
        {
            var doi = TrimTrailing(m.Value);
            if (seen.Add(doi.ToLowerInvariant())) found.Add(doi);
        }
        return found;
    }

    private static readonly Regex Tag = new("<[^>]+>");
    private static readonly Regex Entity = new("&(#x[0-9a-f]+|#[0-9]+|[a-z]+);", RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+");

    /// <summary>Metadata often contains HTML/JATS markup and entities; returns plain text.</summary>
    private static string? CleanText(JsonNode? value)
    {
        if (value is null) return null;
        var s = Tag.Replace(Json.Str(value), "");
        s = Entity.Replace(s, m =>
        {
            var e = m.Groups[1].Value;
            if (e[0] != '#') return Entities.GetValueOrDefault(e.ToLowerInvariant(), m.Value);
            try
            {
                var code = char.ToLowerInvariant(e[1]) == 'x'
                    ? int.Parse(e[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : int.Parse(e[1..], CultureInfo.InvariantCulture);
                return char.ConvertFromUtf32(code);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
            {
                return m.Value;
            }
        });
        s = Spaces.Replace(s, " ").Trim();
        return s.Length > 0 ? s : null;
    }

    private static JsonArray? CleanNames(JsonNode? names)
    {
        if (names is not JsonArray list || list.Count == 0) return null;
        var output = new JsonArray();
        foreach (var item in list)
        {
            var a = item as JsonObject ?? new JsonObject();
            output.Add(Json.Filled(a["family"])
                ? new JsonObject { ["family"] = CleanText(a["family"]), ["given"] = CleanText(a["given"]) ?? "" }
                : new JsonObject { ["literal"] = CleanText(Json.Filled(a["name"]) ? a["name"] : a["literal"]) ?? "" });
        }
        return output;
    }

    private static List<string> Texts(JsonNode? value)
    {
        var values = value is JsonArray a ? a.ToList() : new List<JsonNode?> { value };
        return values.Select(CleanText).Where(t => t != null).Select(t => t!).ToList();
    }

    private static string? WithSubtitle(string? title, string? subtitle)
    {
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(subtitle)
            || title.ToLowerInvariant().Contains(subtitle.ToLowerInvariant(), StringComparison.Ordinal)) return title;
        return title[^1] is ':' or '?' or '!' ? $"{title} {subtitle}" : $"{title}: {subtitle}";
    }

    private static JsonObject? DateParts(JsonNode? issued)
    {
        if (issued is not JsonObject o || o["date-parts"] is not JsonArray dp || dp.Count == 0 || dp[0] is not JsonArray parts) return null;
        var clean = new JsonArray(parts.Where(n => n != null).Select(Json.Clone).ToArray());
        return new JsonObject { ["date-parts"] = new JsonArray(clean) };
    }

    /// <summary>Drops empty values and copies the rest into a new reference.</summary>
    private static JsonObject Compact(IEnumerable<(string Key, JsonNode? Value)> fields)
    {
        var reference = new JsonObject();
        foreach (var (key, value) in fields)
            if (value != null && !(value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 0))
                reference[key] = value.Parent == null ? value : value.DeepClone();
        return reference;
    }

    private static JsonNode? Node(string? value) => value == null ? null : JsonValue.Create(value);

    public static JsonObject FromCrossref(JsonObject work)
    {
        var type = CrossrefTypes.GetValueOrDefault(Json.Text(work, "type"), "article");
        var issued = work["issued"];
        var hasIssued = issued is JsonObject io && io["date-parts"] is JsonArray dp && dp.Count > 0
            && dp[0] is JsonArray first && first.Count > 0 && Json.Filled(first[0]);
        if (!hasIssued)
        {
            issued = new[] { "published-print", "published-online", "created" }
                .Select(k => work[k]).FirstOrDefault(Json.Filled);
        }
        // For chapters and proceedings Crossref may list [series, book]; the book is last.
        var containers = Texts(work["container-title"]);
        var publisher = CleanText(work["publisher"]);
        if (type == "thesis" && work["institution"] is JsonArray inst && inst.Count > 0
            && inst[0] is JsonObject i0 && Json.Filled(i0["name"]))
            publisher = CleanText(i0["name"]);

        return Compact(new (string, JsonNode?)[]
        {
            ("id", work["DOI"]),
            ("type", type),
            ("title", Node(WithSubtitle(Texts(work["title"]).FirstOrDefault(), Texts(work["subtitle"]).FirstOrDefault()))),
            ("author", CleanNames(work["author"])),
            ("editor", CleanNames(work["editor"])),
            ("container-title", Node(type is "chapter" or "paper-conference" ? containers.LastOrDefault() : containers.FirstOrDefault())),
            ("issued", DateParts(issued)),
            ("volume", work["volume"]),
            ("issue", work["issue"]),
            ("page", work["page"]),
            ("publisher", Node(publisher)),
            ("publisher-place", Node(CleanText(work["publisher-location"]))),
            ("ISBN", Node(Texts(work["ISBN"]).FirstOrDefault())),
            ("DOI", work["DOI"]),
            ("URL", work["URL"]),
        });
    }

    /// <summary>Keeps the CSL-JSON fields Plagx Cite uses from doi.org content negotiation (DataCite, arXiv, mEDRA…).</summary>
    private static JsonObject FromCsl(JsonObject data) => Compact(new (string, JsonNode?)[]
    {
        ("id", data["DOI"]),
        ("type", Json.Filled(data["type"]) ? data["type"] : "article"),
        ("title", Node(Texts(data["title"]).FirstOrDefault())),
        ("author", CleanNames(data["author"])),
        ("editor", CleanNames(data["editor"])),
        ("container-title", Node(Texts(data["container-title"]).FirstOrDefault())),
        ("issued", DateParts(data["issued"])),
        ("volume", data["volume"]),
        ("issue", data["issue"]),
        ("page", data["page"]),
        ("publisher", Node(CleanText(data["publisher"]))),
        ("DOI", data["DOI"]),
        ("URL", data["URL"]),
    });

    /// <summary>Percent-encodes UTF-8 bytes except letters, digits and the given safe characters.</summary>
    private static string Encode(string value, string safe)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || (b < 128 && safe.Contains(c))) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static readonly Lazy<HttpClient> DefaultClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("plagx-cite-dotnet (+https://github.com/plagiarismcheckerx/plagx-cite-dotnet)");
        return client;
    });

    private static async Task<(int Status, string Body)> Get(HttpClient http, string url, string accept, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(accept);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private static bool Ok(int status) => status is >= 200 and < 300;

    public static async Task<JsonObject> LookupAsync(string value, string? mailto, HttpClient? http, CancellationToken ct)
    {
        var doi = Normalize(value) ?? throw new ArgumentException($"Not a valid DOI: \"{value}\"", nameof(value));
        var client = http ?? DefaultClient.Value;

        var url = "https://api.crossref.org/works/" + Encode(doi, "-_.!~*'()");
        if (!string.IsNullOrEmpty(mailto)) url += "?mailto=" + Encode(mailto, "-_.!~*'()");
        var (status, body) = await Get(client, url, "application/json", ct).ConfigureAwait(false);
        if (Ok(status)) return FromCrossref(JsonNode.Parse(body)?["message"] as JsonObject ?? new JsonObject());
        if (status != (int)HttpStatusCode.NotFound) throw new DoiLookupException($"DOI lookup failed ({status}) for {doi}", status);

        (status, body) = await Get(client, "https://doi.org/" + Encode(doi, ";,/?:@&=+$-_.!~*'()#"),
            "application/vnd.citationstyles.csl+json", ct).ConfigureAwait(false);
        if (status == (int)HttpStatusCode.NotFound) throw new DoiLookupException($"DOI not found: {doi}", status);
        if (!Ok(status)) throw new DoiLookupException($"DOI lookup failed ({status}) for {doi}", status);
        return FromCsl(JsonNode.Parse(body) as JsonObject ?? new JsonObject());
    }
}
