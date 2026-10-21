// Same cases and expected output as the JavaScript package (plagx-cite on npm).
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Plagx.Cite.Tests;

public class CiteTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject With(JsonObject source, string key, JsonNode? value)
    {
        var copy = source.DeepClone().AsObject();
        copy[key] = value;
        return copy;
    }

    private static readonly JsonObject Article = Obj("""
        {"type": "article-journal", "title": "Deep learning for text similarity",
         "author": [{"family": "Smith", "given": "John Allen"}, {"family": "Lee", "given": "Kate"}],
         "issued": {"date-parts": [[2020]]}, "container-title": "Journal of Academic Writing",
         "volume": "12", "issue": "3", "page": "45-67", "DOI": "10.1234/jaw.2020.045"}
        """);

    private static readonly JsonObject Book = Obj("""
        {"type": "book", "title": "Writing research papers", "author": [{"family": "Brown", "given": "Mary"}],
         "issued": {"date-parts": [[2018]]}, "publisher": "Academic Press", "edition": "2"}
        """);

    private static readonly JsonObject Chapter = Obj("""
        {"type": "chapter", "title": "Citing sources well",
         "author": [{"family": "Khan", "given": "Ali"}, {"family": "Ng", "given": "Wei"}, {"family": "Ruiz", "given": "Ana"}],
         "editor": [{"family": "Green", "given": "Paul"}, {"family": "White", "given": "Sara Jane"}],
         "container-title": "Handbook of scholarly writing", "page": "101-120", "publisher": "Routledge",
         "issued": {"date-parts": [[2019]]}}
        """);

    private static readonly JsonObject Webpage = Obj("""
        {"type": "webpage", "title": "How to avoid plagiarism", "author": [{"literal": "Writing Centre"}],
         "container-title": "University Library", "issued": {"date-parts": [[2023, 3, 15]]},
         "URL": "https://example.com/post"}
        """);

    [Fact]
    public void ApaReferenceListEntries()
    {
        Assert.Equal("Smith, J. A., & Lee, K. (2020). Deep learning for text similarity. Journal of Academic Writing, "
            + "12(3), 45–67. https://doi.org/10.1234/jaw.2020.045", Cite.FormatReference(Article));
        Assert.Equal("Brown, M. (2018). Writing research papers (2nd ed.). Academic Press.", Cite.FormatReference(Book));
        Assert.Equal("Khan, A., Ng, W., & Ruiz, A. (2019). Citing sources well. In P. Green & S. J. White (Eds.), "
            + "Handbook of scholarly writing (pp. 101–120). Routledge.", Cite.FormatReference(Chapter));
        Assert.Equal("Writing Centre. (2023, March 15). How to avoid plagiarism. University Library. https://example.com/post",
            Cite.FormatReference(Webpage));
    }

    [Fact]
    public void ApaWithoutAuthorOrDate()
    {
        var reference = Obj("""{"type": "webpage", "title": "Citation basics", "URL": "https://example.com"}""");
        Assert.Equal("Citation basics. (n.d.). https://example.com", Cite.FormatReference(reference));
    }

    [Fact]
    public void ApaManyAuthorsUseEllipsis()
    {
        var authors = new JsonArray(Enumerable.Range(1, 22).Select(i => (JsonNode)new JsonObject { ["family"] = $"A{i}", ["given"] = "B" }).ToArray());
        var output = Cite.FormatReference(new JsonObject
        {
            ["type"] = "article-journal", ["title"] = "T", ["author"] = authors, ["issued"] = Obj("""{"date-parts": [[2021]]}"""),
        });
        Assert.Matches(@"^A1, B\., .*A19, B\., \. \. \. A22, B\. \(2021\)", output);
        Assert.DoesNotContain("A20", output);
    }

    [Fact]
    public void MlaWorksCitedEntries()
    {
        Assert.Equal("Smith, John Allen, and Kate Lee. “Deep learning for text similarity.” Journal of Academic Writing, "
            + "vol. 12, no. 3, 2020, pp. 45-67, https://doi.org/10.1234/jaw.2020.045.", Cite.FormatReference(Article, "mla"));
        Assert.Equal("Brown, Mary. Writing research papers. 2nd ed., Academic Press, 2018.", Cite.FormatReference(Book, "mla"));
        Assert.Equal("Khan, Ali, et al. “Citing sources well.” Handbook of scholarly writing, edited by Paul Green and "
            + "Sara Jane White, Routledge, 2019, pp. 101-120.", Cite.FormatReference(Chapter, "mla"));
        Assert.Equal("Writing Centre. “How to avoid plagiarism.” University Library, 15 Mar. 2023, https://example.com/post.",
            Cite.FormatReference(Webpage, "mla"));
    }

    [Fact]
    public void HtmlAndMarkdownOutput()
    {
        Assert.Equal("Brown, M. (2018). <i>Writing research papers</i> (2nd ed.). Academic Press.", Cite.FormatReference(Book, format: "html"));
        Assert.Equal("Brown, M. (2018). *Writing research papers* (2nd ed.). Academic Press.", Cite.FormatReference(Book, format: "markdown"));
        Assert.Contains("<i>Tom &amp; &lt;Jerry&gt;</i>", Cite.FormatReference(With(Book, "title", "Tom & <Jerry>"), format: "html"));
    }

    [Fact]
    public void InTextCitations()
    {
        Assert.Equal("(Smith & Lee, 2020, p. 50)", Cite.CiteInText(Article, page: "50"));
        Assert.Equal("Smith and Lee (2020)", Cite.CiteInText(Article, narrative: true));
        Assert.Equal("(Khan et al., 2019, pp. 101–103)", Cite.CiteInText(Chapter, page: "101-103"));
        Assert.Equal("(Smith and Lee 50)", Cite.CiteInText(Article, "mla", "50"));
        Assert.Equal("(Khan et al.)", Cite.CiteInText(Chapter, "mla"));
        Assert.Equal("(“Citation guide,” 2021)", Cite.CiteInText(Obj("""{"title": "Citation guide", "year": 2021}""")));
        Assert.Equal("“Citation guide” (n.d.)", Cite.CiteInText(Obj("""{"title": "Citation guide"}"""), narrative: true));
    }

    [Fact]
    public void BibliographyIsSorted()
    {
        var output = Cite.FormatBibliography(new[] { Article, Chapter, Book });
        Assert.Equal(new[] { "Brown", "Khan", "Smith" }, output.Select(s => s.Split(',')[0]));
    }

    [Fact]
    public void UnknownStyleOrFormat()
    {
        Assert.Contains("Unknown citation style", Assert.Throws<ArgumentException>(() => Cite.FormatReference(Book, "xyz")).Message);
        Assert.Contains("Unknown output format", Assert.Throws<ArgumentException>(() => Cite.FormatReference(Book, format: "pdf")).Message);
    }

    [Fact]
    public void InvalidDoiIsNotALinkAndFullUrlNotDoubled()
    {
        var baseRef = Obj("""{"type": "book", "title": "T", "author": [{"family": "A", "given": "B"}], "year": 2001}""");
        Assert.Equal("A, B. (2001). T.", Cite.FormatReference(With(baseRef, "DOI", "not-a-doi")));
        Assert.Equal("A, B. (2001). T. https://doi.org/10.1234/x", Cite.FormatReference(With(baseRef, "DOI", "https://doi.org/10.1234/x")));
    }

    [Fact]
    public void BibTeXExport()
    {
        Assert.Equal(string.Join("\n",
            "@article{smith2020deep,",
            "  author = {Smith, John Allen and Lee, Kate},",
            "  title = {Deep learning for text similarity},",
            "  journal = {Journal of Academic Writing},",
            "  year = {2020},",
            "  volume = {12},",
            "  number = {3},",
            "  pages = {45--67},",
            "  doi = {10.1234/jaw.2020.045}",
            "}"), Cite.ToBibTeX(Article));
        Assert.Contains(@"title = {50\% of R\&D}", Cite.ToBibTeX(With(Book, "title", "50% of R&D")));
        var keys = Regex.Matches(Cite.ToBibTeX(new[] { Book, Book }), @"@book\{(\w+),").Select(m => m.Groups[1].Value);
        Assert.Equal(new[] { "brown2018writing", "brown2018writinga" }, keys);
    }

    [Fact]
    public void BibTeXRoundTrip()
    {
        var originals = new[] { Article, Book, Chapter };
        var parsed = Cite.ParseBibTeX(Cite.ToBibTeX(originals));
        for (var i = 0; i < originals.Length; i++)
        {
            parsed[i].Remove("id");
            Assert.True(JsonNode.DeepEquals(originals[i], parsed[i]), parsed[i].ToJsonString());
        }
    }

    [Fact]
    public void BibTeXImportLatexMacrosAndOrganisations()
    {
        var reference = Assert.Single(Cite.ParseBibTeX("""
            @comment{ignored}
            @string{jaw = "Journal of Academic Writing"}
            @Article{muller21,
              author  = {M{\"u}ller, J{\"o}rg and {World Health Organization}},
              title   = "{T}he {\&} problem",
              journal = jaw,
              year    = 2021, month = mar,
              pages   = "1--9",
              doi     = {https://doi.org/10.5555/abc}
            }
            """));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""[{"family": "Müller", "given": "Jörg"}, {"literal": "World Health Organization"}]"""), reference["author"]));
        Assert.Equal("The & problem", (string?)reference["title"]);
        Assert.Equal("Journal of Academic Writing", (string?)reference["container-title"]);
        Assert.Equal("""{"date-parts":[[2021,3]]}""", reference["issued"]!.ToJsonString());
        Assert.Equal("1-9", (string?)reference["page"]);
        Assert.Equal("10.5555/abc", (string?)reference["DOI"]);
    }

    [Fact]
    public void DoiNormalisingAndExtraction()
    {
        Assert.Equal("10.1234/ABC.5", Cite.NormalizeDoi("https://doi.org/10.1234/ABC.5"));
        Assert.Equal("10.1234/abc", Cite.NormalizeDoi("doi: 10.1234/abc"));
        Assert.Null(Cite.NormalizeDoi("not a doi"));
        Assert.False(Cite.IsDoi(null));
        const string text = "See (doi:10.1000/xyz123). Also https://doi.org/10.1000/xyz123 and 10.5555/a(b)c.";
        Assert.Equal(new[] { "10.1000/xyz123", "10.5555/a(b)c" }, Cite.ExtractDois(text));
    }

    /// <summary>Answers requests with a fixed function, so DOI tests run offline.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, (int Status, string Body)> respond) : HttpMessageHandler
    {
        public List<(string Url, string Accept)> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.AbsoluteUri, request.Headers.Accept.ToString()));
            var (status, body) = respond(request);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task DoiLookupMapsCrossref()
    {
        var handler = new FakeHandler(_ => (200, """
            {"message": {"type": "journal-article", "title": ["Deep learning for text similarity"],
             "author": [{"given": "John Allen", "family": "Smith"}, {"given": "Kate", "family": "Lee"}],
             "container-title": ["Journal of Academic Writing"], "issued": {"date-parts": [[2020]]},
             "volume": "12", "issue": "3", "page": "45-67", "DOI": "10.1234/jaw.2020.045",
             "URL": "https://doi.org/10.1234/jaw.2020.045", "publisher": "Example Publisher"}}
            """));
        var reference = await Cite.LookupDoiAsync("doi:10.1234/jaw.2020.045", "a@b.org", new HttpClient(handler));
        Assert.Equal("https://api.crossref.org/works/10.1234%2Fjaw.2020.045?mailto=a%40b.org", handler.Calls[0].Url);
        Assert.Equal(Cite.FormatReference(Article), Cite.FormatReference(reference));

        var notFound = new HttpClient(new FakeHandler(_ => (404, "")));
        var ex = await Assert.ThrowsAsync<DoiLookupException>(() => Cite.LookupDoiAsync("10.1234/none", http: notFound));
        Assert.StartsWith("DOI not found", ex.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => Cite.LookupDoiAsync("nope", http: notFound));
    }

    [Fact]
    public async Task DoiLookupFallsBackToDoiOrg()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host == "api.crossref.org" ? (404, "") : (200, """
            {"type": "article", "title": "Attention is all you need",
             "author": [{"family": "Vaswani", "given": "Ashish"}, {"family": "Shazeer", "given": "Noam"}],
             "issued": {"date-parts": [[2017]]}, "publisher": "arXiv", "DOI": "10.48550/ARXIV.1706.03762", "abstract": "ignored"}
            """));
        var reference = await Cite.LookupDoiAsync("10.48550/arXiv.1706.03762", http: new HttpClient(handler));
        Assert.Equal(("https://doi.org/10.48550/arXiv.1706.03762", "application/vnd.citationstyles.csl+json"), handler.Calls[1]);
        Assert.False(reference.ContainsKey("abstract"));
        Assert.Equal("Vaswani, A., & Shazeer, N. (2017). Attention is all you need. arXiv. https://doi.org/10.48550/ARXIV.1706.03762",
            Cite.FormatReference(reference));
        Assert.Equal("Vaswani, Ashish, and Noam Shazeer. “Attention is all you need.” arXiv, 2017, https://doi.org/10.48550/ARXIV.1706.03762.",
            Cite.FormatReference(reference, "mla"));
    }

    [Fact]
    public void CrossrefMarkupSubtitleAndBookTitle()
    {
        var reference = Cite.FromCrossref(Obj("""
            {"type": "book-chapter", "title": ["Optuna"],
             "subtitle": ["A next-generation <i>hyperparameter</i> framework"],
             "container-title": ["Lecture Notes in Computer Science", "Knowledge Discovery &amp; Data Mining"],
             "author": [{"family": "Akiba", "given": "Takuya"}], "publisher": "ACM", "DOI": "10.1145/1"}
            """));
        Assert.Equal("Optuna: A next-generation hyperparameter framework", (string?)reference["title"]);
        Assert.Equal("Knowledge Discovery & Data Mining", (string?)reference["container-title"]);
        var journal = Cite.FromCrossref(Obj("""{"type": "journal-article", "container-title": ["Nature", "Nat."]}"""));
        Assert.Equal("Nature", (string?)journal["container-title"]);
    }

    [Fact]
    public void DuplicateDetection()
    {
        var groups = Cite.FindDuplicates(new[]
        {
            Article,
            Book,
            With(With(Article, "title", "A different title"), "DOI", "https://doi.org/10.1234/JAW.2020.045"),
            With(Book, "title", "Writing Research Papers!"),
            With(Book, "author", JsonNode.Parse("""[{"family": "Someone", "given": "Else"}]""")),
        });
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 0, 2 }, groups[0].Indexes);
        Assert.Equal(new[] { "doi" }, groups[0].Reasons);
        Assert.Equal(new[] { 1, 3 }, groups[1].Indexes);
        Assert.Equal(new[] { "title" }, groups[1].Reasons);
        Assert.Empty(Cite.FindDuplicates(new[] { Article, Book }));
    }

    [Fact]
    public async Task CheckReferences()
    {
        var refs = Cite.ParseBibTeX("""
            @article{smith2020, author={Smith, John and Lee, Kate}, title={Deep learning for text similarity},
              journal={Journal of Academic Writing}, year=2020, volume=12, number=3, pages={45--67}}
            @book{brown2018, author={Brown, Mary}, title={Writing research papers}, publisher={Academic Press}, year=2018}
            @article{smithdup, author={Smith, J.}, title={Deep Learning for Text Similarity!}, journal={JAW}, year=2020}
            @misc{bad, title={Untitled web page}, doi={not-a-doi}}
            """);
        var result = Cite.CheckReferences(refs);
        Assert.Equal((4, 2, 2), (result.References, result.Errors, result.Warnings));
        Assert.Equal(new[] { "duplicate", "invalid-doi", "missing-author", "missing-year" }, result.Issues.Select(x => x.Code));
        Assert.Equal("Possible duplicate (title match) of smith2020: smith2020, smithdup", result.Issues[0].Message);

        var missing = await Cite.CheckReferencesAsync(
            new[] { Obj("""{"title": "X", "author": ["A B"], "year": 2000, "DOI": "10.1234/missing"}""") },
            checkDois: true, http: new HttpClient(new FakeHandler(_ => (404, ""))));
        Assert.Equal(new[] { "doi-not-found" }, missing.Issues.Select(x => x.Code));
    }

    [Fact]
    public void ReferencesBuiltInCode()
    {
        var reference = new JsonObject
        {
            ["type"] = "book",
            ["title"] = "Writing research papers",
            ["author"] = new JsonArray(new JsonObject { ["family"] = "Brown", ["given"] = "Mary" }),
            ["issued"] = new JsonObject { ["date-parts"] = new JsonArray(new JsonArray(2018, 4)) },
            ["edition"] = 3,
            ["publisher"] = "Academic Press",
        };
        Assert.Equal("Brown, M. (2018). Writing research papers (3rd ed.). Academic Press.", Cite.FormatReference(reference));
        Assert.Equal("Brown, Mary. Writing research papers. 3rd ed., Academic Press, Apr. 2018.", Cite.FormatReference(reference, "mla"));
        Assert.Contains("month = {apr}", Cite.ToBibTeX(reference));
    }

    [Fact]
    public void ParseJsonAcceptsArrayOrObject()
    {
        Assert.Equal(2, Cite.ParseJson("""[{"title": "A"}, {"title": "B"}]""").Count);
        Assert.Single(Cite.ParseJson("""{"title": "A"}"""));
        Assert.Throws<ArgumentException>(() => Cite.ParseJson("42"));
    }
}
