using System.Text.Json.Nodes;

namespace Plagx.Cite;

/// <summary>Duplicate detection and reference list checks.</summary>
internal static class Checks
{
    private static Dictionary<string, string> KeysFor(JsonObject reference)
    {
        var keys = new Dictionary<string, string>();
        var doi = Json.Filled(reference["DOI"]) ? Doi.Normalize(Json.Str(reference["DOI"])) : null;
        if (doi != null) keys["doi"] = doi.ToLowerInvariant();
        var title = Util.NormalizeText(Json.Text(reference, "title"));
        if (title.Length > 0)
        {
            var lead = Json.Filled(reference["author"]) ? Util.NormalizeText(Names.FamilyName(Json.Arr(reference["author"])[0])) : "";
            keys["title"] = $"{title}|{lead}";
        }
        return keys;
    }

    public static List<DuplicateGroup> FindDuplicates(IReadOnlyList<JsonObject> refs)
    {
        var keys = refs.Select(KeysFor).ToList();
        var parent = Enumerable.Range(0, refs.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        var firstSeen = new Dictionary<string, int>();
        for (var i = 0; i < keys.Count; i++)
        {
            foreach (var reason in new[] { "doi", "title" })
            {
                if (!keys[i].TryGetValue(reason, out var value)) continue;
                var id = $"{reason}:{value}";
                if (firstSeen.TryGetValue(id, out var seen)) parent[Find(i)] = Find(seen);
                else firstSeen[id] = i;
            }
        }

        var roots = new List<int>();
        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < refs.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var group))
            {
                groups[root] = group = new List<int>();
                roots.Add(root);
            }
            group.Add(i);
        }

        return roots.Select(r => groups[r]).Where(indexes => indexes.Count > 1).Select(indexes =>
        {
            var reasons = new[] { "doi", "title" }.Where(reason =>
            {
                var values = indexes.Select(i => keys[i].GetValueOrDefault(reason)).Where(v => v != null).ToList();
                return values.Count > values.Distinct().Count();
            }).ToList();
            return new DuplicateGroup(indexes, reasons);
        }).ToList();
    }

    private static string Label(JsonObject reference, int i) =>
        Json.Filled(reference["id"]) ? Json.Str(reference["id"]) : $"#{i + 1}";

    public static async Task<CheckResult> CheckAsync(IReadOnlyList<JsonObject> refs, bool checkDois, string? mailto,
        HttpClient? http, CancellationToken ct)
    {
        var issues = new List<CheckIssue>();
        void Add(string level, string code, int index, string message) =>
            issues.Add(new CheckIssue(level, code, index, Label(refs[index], index), message));

        foreach (var group in FindDuplicates(refs))
        {
            var names = group.Indexes.Select(i => Label(refs[i], i)).ToList();
            foreach (var i in group.Indexes.Skip(1))
                Add("error", "duplicate", i, $"Possible duplicate ({string.Join(" and ", group.Reasons)} match) of {names[0]}: {string.Join(", ", names)}");
        }

        for (var i = 0; i < refs.Count; i++)
        {
            var reference = refs[i];
            if (!Json.Filled(reference["title"])) Add("warning", "missing-title", i, "No title");
            if (!Json.Filled(reference["author"]) && !Json.Filled(reference["editor"])) Add("warning", "missing-author", i, "No author or editor");
            if (Util.GetYear(reference) == null) Add("warning", "missing-year", i, "No year (will show as n.d.)");
            if (Json.Filled(reference["DOI"]) && Doi.Normalize(Json.Str(reference["DOI"])) == null)
                Add("error", "invalid-doi", i, $"Invalid DOI \"{Json.Str(reference["DOI"])}\"");
        }

        if (checkDois)
        {
            for (var i = 0; i < refs.Count; i++)
            {
                var doi = Json.Filled(refs[i]["DOI"]) ? Doi.Normalize(Json.Str(refs[i]["DOI"])) : null;
                if (doi == null) continue;
                try
                {
                    await Doi.LookupAsync(doi, mailto, http, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Add("error", "doi-not-found", i, ex.Message);
                }
            }
        }

        var sorted = issues.OrderBy(x => x.Index).ThenBy(x => x.Level == "error" ? 0 : 1).ToList();
        var errors = sorted.Count(x => x.Level == "error");
        return new CheckResult(refs.Count, errors, sorted.Count - errors, sorted);
    }
}
