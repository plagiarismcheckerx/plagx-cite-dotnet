using System.Text.Json.Nodes;

namespace Plagx.Cite.Styles;

/// <summary>APA 7th edition (common reference types).</summary>
internal sealed class Apa : IStyle
{
    private static readonly string[] JournalTypes = { "article-journal", "article-magazine", "article-newspaper", "article" };
    private static readonly string[] ContainerTypes = { "chapter", "paper-conference", "entry-encyclopedia", "entry-dictionary" };
    private static readonly string[] DatedTypes = { "webpage", "article-newspaper", "article-magazine", "post", "post-weblog" };

    private static string AuthorName(JsonNode? name)
    {
        var n = Names.Parts(name);
        if (n.Literal != null) return n.Literal;
        var s = n.Family;
        if (n.Given.Length > 0) s += ", " + Names.Initials(n.Given);
        if (n.Suffix != null) s += ", " + n.Suffix;
        return s;
    }

    private static string AuthorList(JsonArray list)
    {
        var names = list.Select(AuthorName).ToList();
        if (names.Count == 1) return names[0];
        if (names.Count <= 20) return string.Join(", ", names.Take(names.Count - 1)) + ", & " + names[^1];
        return string.Join(", ", names.Take(19)) + ", . . . " + names[^1];
    }

    private static string EditorList(JsonArray list)
    {
        var names = list.Select(name =>
        {
            var n = Names.Parts(name);
            if (n.Literal != null) return n.Literal;
            return n.Given.Length > 0 ? $"{Names.Initials(n.Given)} {n.Family}" : n.Family;
        }).ToList();
        var s = names.Count switch
        {
            1 => names[0],
            2 => string.Join(" & ", names),
            _ => string.Join(", ", names.Take(names.Count - 1)) + ", & " + names[^1],
        };
        return $"{s} ({(names.Count > 1 ? "Eds." : "Ed.")})";
    }

    private static string DateText(JsonObject reference)
    {
        var d = Util.GetDate(reference);
        if (d.Year is not int year || year == 0) return "n.d.";
        var s = year.ToString();
        if (DatedTypes.Contains(Json.Text(reference, "type")) && d.Month is int month and not 0)
        {
            s += ", " + Util.Months[month - 1];
            if (d.Day is int day and not 0) s += $" {day}";
        }
        return s;
    }

    private static string TitleText(JsonObject reference, Formatter f)
    {
        var title = Json.Text(reference, "title");
        if (title.Length == 0) return "";
        var type = Json.Text(reference, "type");
        var italic = !JournalTypes.Contains(type) && !ContainerTypes.Contains(type);
        var s = italic ? f.I(title) : f.T(title);
        var note = "";
        if (Json.Filled(reference["edition"])) note = $" ({Util.Ordinal(reference["edition"])} ed.)";
        if (type == "thesis")
        {
            var publisher = Json.Filled(reference["publisher"]) ? ", " + Json.Str(reference["publisher"]) : "";
            var genre = Json.Filled(reference["genre"]) ? Json.Str(reference["genre"]) : "Thesis";
            note = $" [{genre}{publisher}]";
        }
        if (note.Length > 0) return s + f.T(note + ".");
        return s + f.T(Util.Dot(title));
    }

    private static string SourceText(JsonObject reference, Formatter f, string authors)
    {
        var type = Json.Text(reference, "type");
        var container = Json.Text(reference, "container-title");
        var publisher = Json.Text(reference, "publisher");

        if (JournalTypes.Contains(type))
        {
            // Preprints and similar have no journal; name the repository instead (e.g. arXiv).
            if (container.Length == 0)
                return publisher.Length > 0 && publisher != authors ? f.T(publisher + Util.Dot(publisher)) : "";
            var s = f.I(container);
            if (Json.Filled(reference["volume"])) s += f.T(", ") + f.I(Json.Str(reference["volume"]));
            if (Json.Filled(reference["issue"])) s += f.T($"({Json.Str(reference["issue"])})");
            if (Json.Filled(reference["page"])) s += f.T(", " + Util.PageRange(Json.Str(reference["page"])));
            return s + f.T(".");
        }

        if (ContainerTypes.Contains(type))
        {
            var s = f.T("In ");
            if (Json.Filled(reference["editor"])) s += f.T(EditorList(Json.Arr(reference["editor"])) + ", ");
            if (container.Length > 0) s += f.I(container);
            if (Json.Filled(reference["page"]))
            {
                var page = Json.Str(reference["page"]);
                s += f.T($" ({(Util.IsPageRange(page) ? "pp." : "p.")} {Util.PageRange(page)})");
            }
            s += f.T(".");
            if (publisher.Length > 0) s += f.T(" " + publisher + Util.Dot(publisher));
            return s;
        }

        if (type == "thesis") return "";

        // Webpage: site name; book, report and others: publisher. Omitted when it repeats the author.
        var source = type == "webpage" ? container : publisher;
        if (source.Length == 0 || source == authors) return "";
        return f.T(source + Util.Dot(source));
    }

    public string Reference(JsonObject reference, Formatter f)
    {
        var authors = Json.Filled(reference["author"]) ? AuthorList(Json.Arr(reference["author"])) : "";
        var date = f.T($"({DateText(reference)}).");
        var title = TitleText(reference, f);
        var parts = authors.Length > 0
            ? new List<string> { f.T(authors + Util.Dot(authors)), date, title }
            : new List<string> { title, date };
        parts.Add(SourceText(reference, f, authors));
        parts.Add(f.T(Util.Link(reference)));
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public string InText(JsonObject reference, string? page, bool narrative)
    {
        var names = Json.Arr(reference["author"]).Select(Names.FamilyName).ToList();
        var conj = narrative ? "and" : "&";

        var detail = Util.GetYear(reference) ?? "n.d.";
        if (!string.IsNullOrEmpty(page)) detail += $", {(Util.IsPageRange(page) ? "pp." : "p.")} {Util.PageRange(page)}";

        // No author: the title stands in, and APA puts the comma inside the quotation marks.
        if (names.Count == 0)
        {
            var title = Json.Filled(reference["title"]) ? Json.Str(reference["title"]) : "Untitled";
            return narrative ? $"“{title}” ({detail})" : $"(“{title},” {detail})";
        }

        var who = names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} {conj} {names[1]}",
            _ => $"{names[0]} et al.",
        };
        return narrative ? $"{who} ({detail})" : $"({who}, {detail})";
    }
}
