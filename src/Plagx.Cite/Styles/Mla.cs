using System.Text.Json.Nodes;

namespace Plagx.Cite.Styles;

/// <summary>MLA 9th edition (common reference types).</summary>
internal sealed class Mla : IStyle
{
    private static readonly string[] QuotedTypes =
    {
        "article-journal", "article-magazine", "article-newspaper", "article",
        "chapter", "paper-conference", "webpage", "entry-encyclopedia", "entry-dictionary", "post", "post-weblog",
    };

    private static string Natural(Names.Name n) =>
        n.Literal ?? string.Join(" ", new[] { n.Given, n.Family }.Where(p => p.Length > 0));

    private static string AuthorList(JsonArray list)
    {
        var names = list.Select(Names.Parts).ToList();
        var first = names[0];
        var lead = first.Literal ?? first.Family;
        if (first.Literal == null && first.Given.Length > 0) lead += ", " + first.Given;
        if (first.Literal == null && first.Suffix != null) lead += ", " + first.Suffix;
        return names.Count switch
        {
            1 => lead,
            2 => $"{lead}, and {Natural(names[1])}",
            _ => $"{lead}, et al.",
        };
    }

    private static string EditorList(JsonArray list)
    {
        var names = list.Select(n => Natural(Names.Parts(n))).ToList();
        return names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} et al.",
        };
    }

    private static string DateText(JsonObject reference)
    {
        var d = Util.GetDate(reference);
        var parts = new List<string>();
        if (d.Day is int day and not 0) parts.Add(day.ToString());
        if (d.Month is int month and not 0) parts.Add(Util.MonthsMla[month - 1]);
        if (d.Year is int year and not 0) parts.Add(year.ToString());
        return string.Join(" ", parts);
    }

    private static string PagesText(string page) =>
        $"{(Util.IsPageRange(page) ? "pp." : "p.")} {Util.PageRange(page, "-")}";

    public string Reference(JsonObject reference, Formatter f)
    {
        var type = Json.Text(reference, "type");
        var parts = new List<string>();

        if (Json.Filled(reference["author"]))
        {
            var authors = AuthorList(Json.Arr(reference["author"]));
            parts.Add(f.T(authors + Util.Dot(authors)));
        }

        var title = Json.Text(reference, "title");
        if (title.Length > 0)
        {
            parts.Add(QuotedTypes.Contains(type)
                ? f.T($"“{title}{Util.Dot(title)}”")
                : f.I(title) + f.T(Util.Dot(title)));
        }

        // Container elements, separated by commas and closed with a period.
        var container = Json.Text(reference, "container-title");
        var elements = new List<string>();
        if (container.Length > 0) elements.Add(f.I(container));
        if (Json.Filled(reference["editor"])) elements.Add(f.T("edited by " + EditorList(Json.Arr(reference["editor"]))));
        if (Json.Filled(reference["edition"])) elements.Add(f.T($"{Util.Ordinal(reference["edition"])} ed."));
        if (Json.Filled(reference["volume"])) elements.Add(f.T($"vol. {Json.Str(reference["volume"])}"));
        if (Json.Filled(reference["issue"])) elements.Add(f.T($"no. {Json.Str(reference["issue"])}"));
        // Articles name their journal, not the publisher, unless there is no journal (e.g. arXiv preprints).
        if (Json.Filled(reference["publisher"]) && (!type.StartsWith("article", StringComparison.Ordinal) || container.Length == 0))
            elements.Add(f.T(Json.Str(reference["publisher"])));
        var date = DateText(reference);
        if (date.Length > 0) elements.Add(f.T(date));
        if (Json.Filled(reference["page"])) elements.Add(f.T(PagesText(Json.Str(reference["page"]))));
        var url = Util.Link(reference);
        if (url.Length > 0) elements.Add(f.T(url));

        if (elements.Count > 0) parts.Add(string.Join(f.T(", "), elements) + f.T("."));
        return string.Join(" ", parts);
    }

    public string InText(JsonObject reference, string? page, bool narrative)
    {
        var names = Json.Arr(reference["author"]).Select(Names.FamilyName).ToList();
        var who = names.Count switch
        {
            0 => $"“{(Json.Filled(reference["title"]) ? Json.Str(reference["title"]) : "Untitled")}”",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} et al.",
        };
        var pages = string.IsNullOrEmpty(page) ? "" : Util.PageRange(page, "-");
        if (narrative) return pages.Length > 0 ? $"{who} ({pages})" : who;
        return pages.Length > 0 ? $"({who} {pages})" : $"({who})";
    }
}
