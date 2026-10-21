# Plagx Cite for .NET

*A free academic writing tool by Plagiarism Checker X.*

**Plagx Cite for .NET is a free, open-source citation generator and reference checker for C# and .NET apps.** It turns sources into correct APA 7 and MLA 9 references and in-text citations, so researchers and students can build a clean reference list in seconds. It also checks BibTeX files for duplicate references, broken DOIs and missing details before you submit.

Use it as an **APA citation generator** or **MLA citation generator**: create a **reference list, bibliography or works cited** page, generate **in-text citations**, turn a **DOI into a citation**, import and export **BibTeX**, and find **duplicate references**.

- No dependencies, .NET 8 or later
- Works in ASP.NET Core, Blazor, desktop apps, Azure Functions and console tools
- Uses [CSL-JSON](https://citeproc-js.readthedocs.io/en/latest/csl-json/markup.html), the reference format used by Zotero, Mendeley and Crossref
- Plain text, HTML or Markdown output
- Same output as the JavaScript version, [plagx-cite on npm](https://www.npmjs.com/package/plagx-cite)

**[Try the live demo →](https://plagiarismcheckerx.github.io/plagx-cite/)**

Need a quick citation online? Use the [free citation generator](https://plagiarismcheckerx.com/citation-generator?utm_source=plagx-cite-dotnet&utm_medium=readme&utm_campaign=plagx&utm_content=citation-generator).

> Plagx Cite is a free academic writing utility by Plagiarism Checker X. It is a citation generator that builds and checks APA and MLA reference lists. Plagiarism checking and AI-generated text detection are available in Plagiarism Checker X, linked at the end of this page.

## Install

```bash
dotnet add package Plagx.Cite
```

## Quick start

```csharp
using System.Text.Json.Nodes;
using Plagx.Cite;

var reference = new JsonObject
{
    ["type"] = "article-journal",
    ["title"] = "Deep learning for text similarity",
    ["author"] = new JsonArray(
        new JsonObject { ["family"] = "Smith", ["given"] = "John Allen" },
        new JsonObject { ["family"] = "Lee", ["given"] = "Kate" }),
    ["issued"] = new JsonObject { ["date-parts"] = new JsonArray(new JsonArray(2020)) },
    ["container-title"] = "Journal of Academic Writing",
    ["volume"] = "12",
    ["issue"] = "3",
    ["page"] = "45-67",
    ["DOI"] = "10.1234/jaw.2020.045",
};

Cite.FormatReference(reference);
// Smith, J. A., & Lee, K. (2020). Deep learning for text similarity. Journal of Academic Writing, 12(3), 45–67. https://doi.org/10.1234/jaw.2020.045

Cite.FormatReference(reference, style: "mla");
// Smith, John Allen, and Kate Lee. “Deep learning for text similarity.” Journal of Academic Writing, vol. 12, no. 3, 2020, pp. 45-67, https://doi.org/10.1234/jaw.2020.045.

Cite.CiteInText(reference, page: "50");               // (Smith & Lee, 2020, p. 50)
Cite.CiteInText(reference, narrative: true);          // Smith and Lee (2020)
Cite.CiteInText(reference, style: "mla", page: "50"); // (Smith and Lee 50)
```

References are `JsonObject`s in CSL-JSON form, so a Zotero or Crossref export works with `Cite.ParseJson(File.ReadAllText("library.json"))`.

## API

All methods are on the static `Cite` class in the `Plagx.Cite` namespace.

### Formatting references and citations

| Method | Returns |
|---|---|
| `FormatReference(reference, style = "apa", format = "text")` | One reference-list entry |
| `FormatBibliography(refs, style = "apa", format = "text")` | Sorted list of entries |
| `CiteInText(reference, style = "apa", page = null, narrative = false)` | In-text citation |
| `SortReferences(refs)` | New list sorted by author, year, title |
| `ParseJson(json)` | References from CSL-JSON text (an array or one object) |
| `Styles` | `["apa", "mla"]` |

- `style`: `"apa"` (default) or `"mla"`
- `format`: `"text"` (default), `"html"` (titles in `<i>`, text escaped) or `"markdown"` (titles in `*…*`)

Supported reference types: `article-journal`, `article-magazine`, `article-newspaper`, `book`, `chapter`, `paper-conference`, `report`, `thesis`, `webpage`. Other types fall back to a sensible general layout.

### BibTeX import and export

```csharp
var refs = Cite.ParseBibTeX(File.ReadAllText("library.bib"));
Console.WriteLine(Cite.ToBibTeX(refs));
```

`ParseBibTeX` handles `@string` macros, `#` concatenation, common LaTeX accents (`{\"u}` → ü), organisation names in braces, and BibLaTeX `date` / `journaltitle` fields.

### DOI lookup

```csharp
var reference = await Cite.LookupDoiAsync("10.1038/nature14539", mailto: "you@example.com");

Cite.NormalizeDoi("https://doi.org/10.1038/NATURE14539"); // "10.1038/NATURE14539"
Cite.ExtractDois(text);                                    // every DOI found in the text
```

`LookupDoiAsync` calls the free public [Crossref API](https://www.crossref.org/documentation/retrieve-metadata/rest-api/). If a DOI isn't registered with Crossref, such as arXiv or Zenodo DOIs from DataCite, it falls back to [doi.org content negotiation](https://citation.doi.org/). No API key is needed. It throws `ArgumentException` for an invalid DOI and `DoiLookupException` when the DOI can't be found. Pass your own `HttpClient` (for example from `IHttpClientFactory`) with `http:`.

### Duplicate references

```csharp
Cite.FindDuplicates(refs);
// [DuplicateGroup { Indexes = [0, 4], Reasons = ["doi"] }, DuplicateGroup { Indexes = [2, 7], Reasons = ["title"] }]
```

References count as duplicates when they share a DOI, or the same title and first author. Case, accents and punctuation are ignored.

### Checking a reference list

```csharp
var result = await Cite.CheckReferencesAsync(refs, checkDois: true);
// result.References = 4, result.Errors = 2, result.Warnings = 1
// result.Issues[0] = CheckIssue { Level = "error", Code = "duplicate", Ref = "smith2020b", ... }
```

Errors are duplicates, invalid DOIs and DOIs that can't be found (with `checkDois: true`). Warnings are missing titles, authors or years. `Cite.CheckReferences(refs)` runs the same checks offline, without the DOI lookups.

## Good to know

- Titles are used exactly as you give them. APA wants sentence case for article and book titles, and MLA wants title case. Plagx Cite does not change capitalisation automatically, because doing so would break names and acronyms.
- The output follows the main APA 7 and MLA 9 rules for common reference types. Always check the final list against your institution's or publisher's guidelines.

## Development

```bash
dotnet test
```

<h2>Plagx Cite - Free Citation & Reference Toolkit</h2>

<p>
 An open-source citation utility for students, researchers and developers.
 Format references, organize citations and manage bibliographies.
 </p>

<p>
 Developed by Plagiarism Checker X.
 </p>

<p>
 Plagx Cite helps with source attribution; it does not detect plagiarism
 or AI-generated text. For full document checks, explore
 <a href="https://plagiarismcheckerx.com/?utm_source=plagx-cite-dotnet&utm_medium=readme&utm_campaign=plagx&utm_content=about-cta">Plagiarism Checker X</a>
 and its
 <a href="https://plagiarismcheckerx.com/ai-content-detector?utm_source=plagx-cite-dotnet&utm_medium=readme&utm_campaign=plagx&utm_content=about-ai-detector">AI Content Detector</a>.
 </p>

## License

MIT © Plagiarism Checker X
