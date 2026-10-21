/*!
 * Plagx Cite: a free academic writing utility by Plagiarism Checker X.
 * https://plagiarismcheckerx.com/ · MIT License
 */
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Plagx.Cite.Styles;

namespace Plagx.Cite;

/// <summary>
/// APA 7 and MLA 9 citation generator and reference checker.
/// References are CSL-JSON objects, for example a Zotero or Crossref export read with <see cref="ParseJson"/>.
/// </summary>
public static class Cite
{
    private static readonly Dictionary<string, IStyle> StyleMap = new()
    {
        ["apa"] = new Apa(),
        ["mla"] = new Mla(),
    };

    /// <summary>Names of the built-in citation styles.</summary>
    public static IReadOnlyList<string> Styles { get; } = StyleMap.Keys.ToList();

    private static IStyle Style(string name) =>
        StyleMap.TryGetValue(name.ToLowerInvariant(), out var style)
            ? style
            : throw new ArgumentException($"Unknown citation style \"{name}\". Available: {string.Join(", ", Styles)}.", nameof(name));

    /// <summary>Reads CSL-JSON text: an array of references or a single reference.</summary>
    public static List<JsonObject> ParseJson(string json) => JsonNode.Parse(json) switch
    {
        JsonArray a => a.OfType<JsonObject>().ToList(),
        JsonObject o => new List<JsonObject> { o },
        _ => throw new ArgumentException("Expected a CSL-JSON array or object.", nameof(json)),
    };

    /// <summary>Formats one reference as a reference-list entry.</summary>
    /// <param name="reference">CSL-JSON reference.</param>
    /// <param name="style"><c>"apa"</c> or <c>"mla"</c>.</param>
    /// <param name="format"><c>"text"</c>, <c>"html"</c> (titles in &lt;i&gt;, text escaped) or <c>"markdown"</c> (titles in *…*).</param>
    public static string FormatReference(JsonObject reference, string style = "apa", string format = "text") =>
        Style(style).Reference(reference, new Formatter(format));

    /// <summary>Sorts references and formats each one.</summary>
    public static List<string> FormatBibliography(IEnumerable<JsonObject> refs, string style = "apa", string format = "text") =>
        SortReferences(refs).Select(r => FormatReference(r, style, format)).ToList();

    /// <summary>In-text citation, e.g. "(Smith &amp; Lee, 2020, p. 4)" or "Smith and Lee (2020)".</summary>
    public static string CiteInText(JsonObject reference, string style = "apa", string? page = null, bool narrative = false) =>
        Style(style).InText(reference, page, narrative);

    private static readonly Regex Article = new(@"^(a|an|the)\s+", RegexOptions.IgnoreCase);

    private static string SortKey(JsonObject reference)
    {
        var lead = Json.Filled(reference["author"])
            ? Names.FamilyName(Json.Arr(reference["author"])[0])
            : Article.Replace(Json.Text(reference, "title"), "");
        return $"{Util.NormalizeText(lead)} {Util.GetYear(reference) ?? "0000"} {Util.NormalizeText(Json.Text(reference, "title"))}";
    }

    /// <summary>Returns a new list sorted by first author (or title), year, then title.</summary>
    public static List<JsonObject> SortReferences(IEnumerable<JsonObject> refs) =>
        refs.OrderBy(SortKey, StringComparer.Ordinal).ToList();

    /// <summary>Converts references to BibTeX.</summary>
    public static string ToBibTeX(IEnumerable<JsonObject> refs) => BibTeX.ToBibTeX(refs);

    /// <summary>Converts one reference to BibTeX.</summary>
    public static string ToBibTeX(JsonObject reference) => BibTeX.ToBibTeX(new[] { reference });

    /// <summary>
    /// Parses BibTeX/BibLaTeX text into CSL-JSON references. Handles <c>@string</c> macros, <c>#</c> concatenation,
    /// common LaTeX accents and BibLaTeX <c>date</c> / <c>journaltitle</c> fields.
    /// </summary>
    public static List<JsonObject> ParseBibTeX(string source) => BibTeX.Parse(source);

    /// <summary>Returns a bare DOI ("10.xxxx/...") from a DOI, doi: string or doi.org URL, or null if invalid.</summary>
    public static string? NormalizeDoi(string? value) => Doi.Normalize(value);

    /// <summary>True when the value is a valid DOI.</summary>
    public static bool IsDoi(string? value) => Doi.Normalize(value) != null;

    /// <summary>Finds every DOI in a block of text (unique, in order of appearance).</summary>
    public static List<string> ExtractDois(string? text) => Doi.Extract(text);

    /// <summary>Maps a Crossref "work" record to CSL-JSON.</summary>
    public static JsonObject FromCrossref(JsonObject work) => Doi.FromCrossref(work);

    /// <summary>
    /// Looks up a DOI and returns CSL-JSON. Tries the public Crossref API first, then doi.org
    /// content negotiation (covers DataCite DOIs such as arXiv and Zenodo). No API key is needed.
    /// </summary>
    /// <param name="doi">DOI, doi: string or doi.org URL.</param>
    /// <param name="mailto">Optional email for Crossref's polite pool.</param>
    /// <param name="http">Optional HttpClient to use instead of the built-in one.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException">The DOI is invalid.</exception>
    /// <exception cref="DoiLookupException">The DOI can't be found or the lookup failed.</exception>
    public static Task<JsonObject> LookupDoiAsync(string doi, string? mailto = null, HttpClient? http = null,
        CancellationToken cancellationToken = default) => Doi.LookupAsync(doi, mailto, http, cancellationToken);

    /// <summary>Finds references that share a DOI, or the same title and first author.</summary>
    public static List<DuplicateGroup> FindDuplicates(IReadOnlyList<JsonObject> refs) => Checks.FindDuplicates(refs);

    /// <summary>
    /// Checks a reference list offline. Errors: duplicates and invalid DOIs.
    /// Warnings: missing titles, authors or years.
    /// </summary>
    public static CheckResult CheckReferences(IReadOnlyList<JsonObject> refs) =>
        Checks.CheckAsync(refs, false, null, null, default).GetAwaiter().GetResult();

    /// <summary>
    /// Checks a reference list. With <paramref name="checkDois"/> every DOI is also looked up online
    /// (Crossref / doi.org) and DOIs that can't be found are reported as errors.
    /// </summary>
    public static Task<CheckResult> CheckReferencesAsync(IReadOnlyList<JsonObject> refs, bool checkDois = false,
        string? mailto = null, HttpClient? http = null, CancellationToken cancellationToken = default) =>
        Checks.CheckAsync(refs, checkDois, mailto, http, cancellationToken);
}
