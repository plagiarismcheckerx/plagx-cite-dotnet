using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Plagx.Cite;

/// <summary>BibTeX export and BibTeX/BibLaTeX import.</summary>
internal static class BibTeX
{
    private static readonly Dictionary<string, string> ToBibTeXType = new()
    {
        ["article-journal"] = "article",
        ["article-magazine"] = "article",
        ["article-newspaper"] = "article",
        ["article"] = "article",
        ["book"] = "book",
        ["chapter"] = "incollection",
        ["paper-conference"] = "inproceedings",
        ["report"] = "techreport",
        ["thesis"] = "phdthesis",
    };

    private static readonly Dictionary<string, string> FromBibTeXType = new()
    {
        ["article"] = "article-journal",
        ["book"] = "book",
        ["inbook"] = "chapter",
        ["incollection"] = "chapter",
        ["inproceedings"] = "paper-conference",
        ["conference"] = "paper-conference",
        ["techreport"] = "report",
        ["report"] = "report",
        ["phdthesis"] = "thesis",
        ["mastersthesis"] = "thesis",
        ["thesis"] = "thesis",
        ["online"] = "webpage",
    };

    private static readonly string[] MonthNames = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    private static readonly Dictionary<string, string> Accents = new()
    {
        ["\""] = "\u0308", ["'"] = "\u0301", ["`"] = "\u0300", ["^"] = "\u0302", ["~"] = "\u0303", ["="] = "\u0304",
        ["."] = "\u0307", ["c"] = "\u0327", ["v"] = "\u030C", ["u"] = "\u0306", ["H"] = "\u030B",
    };

    // ------------------------------------------------------------------ export

    private static readonly Regex Special = new(@"[\\&%$#_{}]");
    private static readonly Regex Dashes = new(@"\s*[-\u2013\u2014]+\s*");
    private static readonly Regex KeyChars = new(@"^[A-Za-z0-9_:.-]+\z");

    private static string EscapeValue(string value) =>
        Special.Replace(value, m => m.Value == "\\" ? "\\textbackslash{}" : "\\" + m.Value);

    private static string BibName(JsonNode? name)
    {
        var n = Names.Parts(name);
        if (n.Literal != null) return "{" + n.Literal + "}";
        return string.Join(", ", new[] { n.Family, n.Suffix, n.Given }.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string BaseKey(JsonObject reference)
    {
        var lead = Json.Filled(reference["author"]) ? Names.FamilyName(Json.Arr(reference["author"])[0]) : "anon";
        var word = Util.NormalizeText(Json.Text(reference, "title")).Split(' ')
            .FirstOrDefault(w => w.Length > 3 && w is not ("with" or "from" or "that" or "this")) ?? "";
        var year = Util.GetDate(reference).Year is int y and not 0 ? y.ToString() : "";
        var key = Util.NormalizeText(lead).Replace(" ", "") + year + word;
        return key.Length > 0 ? key : "ref";
    }

    private static string Entry(JsonObject reference, string key)
    {
        var type = ToBibTeXType.GetValueOrDefault(Json.Text(reference, "type"), "misc");
        var container = reference["container-title"];
        var d = Util.GetDate(reference);
        string NameList(string field) => string.Join(" and ", Json.Arr(reference[field]).Select(BibName));

        var publisherField = type == "phdthesis" ? "school" : type == "techreport" ? "institution" : "publisher";
        var fields = new (string Name, string? Value)[]
        {
            ("author", Json.Filled(reference["author"]) ? NameList("author") : null),
            ("editor", Json.Filled(reference["editor"]) ? NameList("editor") : null),
            ("title", Value(reference["title"])),
            (type == "article" ? "journal" : "booktitle", type is "article" or "incollection" or "inproceedings" ? Value(container) : null),
            ("howpublished", type == "misc" ? Value(container) : null),
            ("year", d.Year is int y and not 0 ? y.ToString() : null),
            ("month", d.Month is int m and not 0 ? MonthNames[m - 1] : null),
            ("volume", Value(reference["volume"])),
            ("number", Value(reference["issue"])),
            ("pages", Json.Filled(reference["page"]) ? Dashes.Replace(Json.Str(reference["page"]), "--") : null),
            ("edition", Value(reference["edition"])),
            (publisherField, Value(reference["publisher"])),
            ("address", Value(reference["publisher-place"])),
            ("isbn", Value(reference["ISBN"])),
            ("doi", Value(reference["DOI"])),
            ("url", Value(reference["URL"])),
        };

        var body = fields.Where(x => !string.IsNullOrEmpty(x.Value)).Select(x =>
        {
            var raw = x.Name is "author" or "editor" or "url" or "doi" or "month";
            return $"  {x.Name} = {{{(raw ? x.Value : EscapeValue(x.Value!))}}}";
        });
        return $"@{type}{{{key},\n" + string.Join(",\n", body) + "\n}";
    }

    private static string? Value(JsonNode? node) => Json.Filled(node) ? Json.Str(node) : null;

    public static string ToBibTeX(IEnumerable<JsonObject> refs)
    {
        var used = new HashSet<string>();
        var output = new List<string>();
        foreach (var reference in refs)
        {
            var id = Json.Text(reference, "id");
            var key = id.Length > 0 && KeyChars.IsMatch(id) ? id : BaseKey(reference);
            if (used.Contains(key))
            {
                var n = 0;
                while (used.Contains(key + (char)(97 + n))) n++;
                key += (char)(97 + n);
            }
            used.Add(key);
            output.Add(Entry(reference, key));
        }
        return string.Join("\n\n", output);
    }

    // ------------------------------------------------------------------ import

    private static readonly Regex AccentShort = new(@"\\([`'^""~=.])\s*\{?\\?([A-Za-z])\}?");
    private static readonly Regex AccentLetter = new(@"\\([cvuH])\s*\{\\?([A-Za-z])\}");
    private static readonly Regex Command = new(@"\\(?:emph|textit|textbf|textsc|mathrm)\{([^{}]*)\}");
    private static readonly Regex Escaped = new(@"\\([&%$#_{}])");
    private static readonly Regex Braces = new(@"(?<!\\)[{}]");
    private static readonly Regex Spaces = new(@"\s+");

    /// <summary>Converts common LaTeX markup to plain Unicode text.</summary>
    private static string CleanLatex(string value)
    {
        var s = AccentShort.Replace(value, m => m.Groups[2].Value + Accents[m.Groups[1].Value]);
        s = AccentLetter.Replace(s, m => m.Groups[2].Value + Accents[m.Groups[1].Value]);
        s = s.Replace("\\textbackslash{}", "\\");
        s = Command.Replace(s, "$1");
        s = Escaped.Replace(s, "$1");
        s = Braces.Replace(s, "");
        s = s.Replace("~", " ");
        s = Spaces.Replace(s, " ");
        return s.Normalize(NormalizationForm.FormC).Trim();
    }

    private static readonly Regex And = new(@"\G\s+and\s+", RegexOptions.IgnoreCase);
    private static readonly Regex Comma = new(@"\G,");

    private static List<string> SplitTopLevel(string value, Regex separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c == '{') depth++;
            if (c == '}') depth--;
            var m = depth == 0 ? separator.Match(value, i) : null;
            if (m is { Success: true })
            {
                parts.Add(current.ToString());
                current.Clear();
                i += m.Length;
                continue;
            }
            current.Append(c);
            i++;
        }
        parts.Add(current.ToString());
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    private static readonly Regex BracedName = new(@"^\{[^{}]*\}\z");

    private static JsonArray ParseNames(string value)
    {
        var output = new JsonArray();
        foreach (var raw in SplitTopLevel(value, And))
        {
            if (BracedName.IsMatch(raw))
            {
                output.Add(new JsonObject { ["literal"] = CleanLatex(raw[1..^1]) });
                continue;
            }
            var parts = SplitTopLevel(raw, Comma).Select(CleanLatex).ToList();
            if (parts.Count == 3)
                output.Add(new JsonObject { ["family"] = parts[0], ["suffix"] = parts[1], ["given"] = parts[2] });
            else if (parts.Count == 2)
                output.Add(new JsonObject { ["family"] = parts[0], ["given"] = parts[1] });
            else
            {
                var n = Names.Parts(JsonValue.Create(CleanLatex(raw)));
                output.Add(new JsonObject { ["family"] = n.Family, ["given"] = n.Given });
            }
        }
        return output;
    }

    private static int FindClose(string src, int start, char open, char close)
    {
        var depth = 0;
        for (var i = start; i < src.Length; i++)
        {
            if (src[i] == open) depth++;
            else if (src[i] == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static readonly Regex FieldName = new(@"\G[^\s=,{}]+");
    private static readonly Regex Token = new(@"\G[^\s,#}]+");

    private static Dictionary<string, string> ParseFields(string body, Dictionary<string, string> macros)
    {
        var fields = new Dictionary<string, string>();
        var n = body.Length;
        var i = 0;
        int SkipSpace(int k)
        {
            while (k < n && char.IsWhiteSpace(body[k])) k++;
            return k;
        }

        while (i < n)
        {
            while (i < n && (body[i] == ',' || char.IsWhiteSpace(body[i]))) i++;
            var name = FieldName.Match(body, i);
            if (!name.Success) break;
            i = SkipSpace(name.Index + name.Length);
            if (i >= n || body[i] != '=') break;
            i++;

            var value = new StringBuilder();
            while (true)
            {
                i = SkipSpace(i);
                if (i < n && body[i] == '{')
                {
                    var end = FindClose(body, i, '{', '}');
                    if (end < 0) return fields;
                    value.Append(body, i + 1, end - i - 1);
                    i = end + 1;
                }
                else if (i < n && body[i] == '"')
                {
                    var depth = 0;
                    var j = i + 1;
                    for (; j < n; j++)
                    {
                        if (body[j] == '{') depth++;
                        else if (body[j] == '}') depth--;
                        else if (body[j] == '"' && depth == 0 && body[j - 1] != '\\') break;
                    }
                    value.Append(body, i + 1, Math.Min(j, n) - i - 1);
                    i = j + 1;
                }
                else
                {
                    var token = i < n ? Token.Match(body, i) : Match.Empty;
                    if (!token.Success) break;
                    value.Append(macros.GetValueOrDefault(token.Value.ToLowerInvariant(), token.Value));
                    i = token.Index + token.Length;
                }
                i = SkipSpace(i);
                if (i >= n || body[i] != '#') break;
                i++;
            }
            fields[name.Value.ToLowerInvariant()] = value.ToString();
        }
        return fields;
    }

    private static readonly Regex IsoDate = new(@"^([0-9]{4})(?:-([0-9]{1,2}))?(?:-([0-9]{1,2}))?");
    private static readonly Regex Year = new(@"[0-9]{4}");
    private static readonly Regex Hyphens = new(@"\s*-+\s*");
    private static readonly Regex DoiUrl = new(@"^https?://(dx\.)?doi\.org/", RegexOptions.IgnoreCase);

    private static JsonObject ToCsl(string type, string key, Dictionary<string, string> fields)
    {
        string? Field(string name) => fields.TryGetValue(name, out var v) && v.Length > 0 ? v : null;
        string? Text(string name) => Field(name) is string v ? CleanLatex(v) : null;
        string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var reference = new JsonObject
        {
            ["id"] = key,
            ["type"] = FromBibTeXType.GetValueOrDefault(type) ?? (Field("url") != null ? "webpage" : "document"),
        };

        if (Field("author") is string author) reference["author"] = ParseNames(author);
        if (Field("editor") is string editor) reference["editor"] = ParseNames(editor);
        if (Field("title") != null) reference["title"] = Text("title");
        var container = First(Text("journal"), Text("journaltitle"), Text("booktitle"), Text("howpublished"));
        if (container != null) reference["container-title"] = container;

        var date = IsoDate.Match(Field("date") ?? "");
        var yearMatch = Year.Match(Field("year") ?? "");
        var year = date.Success ? date.Groups[1].Value : yearMatch.Success ? yearMatch.Value : null;
        if (year != null)
        {
            var month = First(date.Success ? date.Groups[2].Value : null, Field("month"));
            var monthNumber = 0;
            if (month != null && month.All(char.IsAsciiDigit)) monthNumber = int.Parse(month);
            else if (month != null)
            {
                var key3 = month.Trim().ToLowerInvariant();
                monthNumber = Array.IndexOf(MonthNames, key3[..Math.Min(3, key3.Length)]) + 1;
            }
            var parts = new JsonArray { int.Parse(year) };
            if (monthNumber > 0) parts.Add(monthNumber);
            if (date.Success && date.Groups[3].Success) parts.Add(int.Parse(date.Groups[3].Value));
            reference["issued"] = new JsonObject { ["date-parts"] = new JsonArray { parts } };
        }

        if (Field("volume") != null) reference["volume"] = Text("volume");
        if (Field("number") != null) reference["issue"] = Text("number");
        if (Field("pages") != null) reference["page"] = Hyphens.Replace(Text("pages") ?? "", "-");
        if (Field("edition") != null) reference["edition"] = Text("edition");
        var publisher = First(Text("publisher"), Text("school"), Text("institution"), Text("organization"));
        if (publisher != null) reference["publisher"] = publisher;
        if (Field("address") != null || Field("location") != null) reference["publisher-place"] = First(Text("address"), Text("location"));
        if (Field("isbn") != null) reference["ISBN"] = Text("isbn");
        if (Field("doi") is string doi) reference["DOI"] = DoiUrl.Replace(doi.Trim(), "");
        if (Field("url") is string url) reference["URL"] = url.Trim();
        if (type == "phdthesis") reference["genre"] = "Doctoral dissertation";
        if (type == "mastersthesis") reference["genre"] = "Master\u2019s thesis";
        return reference;
    }

    private static readonly Regex Head = new(@"\G@\s*([A-Za-z]+)\s*([{(])");

    public static List<JsonObject> Parse(string source)
    {
        var macros = MonthNames.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => (x.i + 1).ToString());
        var refs = new List<JsonObject>();
        var i = 0;
        while (true)
        {
            var at = source.IndexOf('@', i);
            if (at < 0) break;
            var head = Head.Match(source, at);
            if (!head.Success)
            {
                i = at + 1;
                continue;
            }
            var type = head.Groups[1].Value.ToLowerInvariant();
            var openIndex = head.Index + head.Length - 1;
            var open = head.Groups[2].Value[0];
            var end = FindClose(source, openIndex, open, open == '{' ? '}' : ')');
            if (end < 0) break;
            var body = source.Substring(openIndex + 1, end - openIndex - 1);
            i = end + 1;

            if (type is "comment" or "preamble") continue;
            if (type == "string")
            {
                foreach (var (k, v) in ParseFields(body, macros)) macros[k] = v;
                continue;
            }

            var comma = body.IndexOf(',');
            var key = (comma < 0 ? body : body[..comma]).Trim();
            var fields = comma < 0 ? new Dictionary<string, string>() : ParseFields(body[(comma + 1)..], macros);
            refs.Add(ToCsl(type, key, fields));
        }
        return refs;
    }
}
