using System.Text.Json.Serialization;

namespace Plagx.Cite;

/// <summary>References that appear more than once, and what matched (<c>"doi"</c>, <c>"title"</c>).</summary>
public sealed record DuplicateGroup(
    [property: JsonPropertyName("indexes")] IReadOnlyList<int> Indexes,
    [property: JsonPropertyName("reasons")] IReadOnlyList<string> Reasons);

/// <summary>One problem found by <see cref="Cite.CheckReferences"/>.</summary>
/// <param name="Level"><c>"error"</c> or <c>"warning"</c>.</param>
/// <param name="Code"><c>duplicate</c>, <c>invalid-doi</c>, <c>doi-not-found</c>, <c>missing-title</c>, <c>missing-author</c> or <c>missing-year</c>.</param>
/// <param name="Index">Position of the reference in the list.</param>
/// <param name="Ref">The reference's id, or <c>#n</c> when it has none.</param>
/// <param name="Message">Readable description.</param>
public sealed record CheckIssue(
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Result of checking a reference list.</summary>
public sealed record CheckResult(
    [property: JsonPropertyName("references")] int References,
    [property: JsonPropertyName("errors")] int Errors,
    [property: JsonPropertyName("warnings")] int Warnings,
    [property: JsonPropertyName("issues")] IReadOnlyList<CheckIssue> Issues);
