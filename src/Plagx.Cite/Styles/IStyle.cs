using System.Text.Json.Nodes;

namespace Plagx.Cite.Styles;

internal interface IStyle
{
    string Reference(JsonObject reference, Formatter f);

    string InText(JsonObject reference, string? page, bool narrative);
}
