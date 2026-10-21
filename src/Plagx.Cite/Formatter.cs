using System.Text;
using System.Text.RegularExpressions;

namespace Plagx.Cite;

/// <summary>Escapes text and italicises titles for the chosen output format.</summary>
internal sealed class Formatter
{
    private static readonly Regex MarkdownSpecial = new(@"([\\*_])");
    private readonly string _format;

    public Formatter(string format)
    {
        if (format is not ("text" or "html" or "markdown"))
            throw new ArgumentException($"Unknown output format \"{format}\". Use \"text\", \"html\" or \"markdown\".", nameof(format));
        _format = format;
    }

    /// <summary>Plain text in the output format.</summary>
    public string T(string s)
    {
        if (_format == "html")
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => c.ToString() });
            return sb.ToString();
        }
        if (_format == "markdown") return MarkdownSpecial.Replace(s, @"\$1");
        return s;
    }

    /// <summary>Italic text in the output format.</summary>
    public string I(string s) => _format switch
    {
        "html" => $"<i>{T(s)}</i>",
        "markdown" => $"*{T(s)}*",
        _ => s,
    };
}
