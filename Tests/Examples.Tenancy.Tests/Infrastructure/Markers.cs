using System.Text;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Texts a caller may send where a list expects one of its own cursors: what a scenario sends to see a list
/// refuse it. A cursor is Base64 of a head in braces and the values the list is ordered by.
/// </summary>
public static class Markers
{
    /// <summary>A text that is no Base64, so no cursor of any list.</summary>
    public const string PlainText = "not-a-cursor";

    /// <summary>A head and nothing after it: a cursor without a value, which no list gives.</summary>
    public const string HeadAlone = "e30=";

    /// <summary>A head that is neither empty nor three numbers, with a value after it.</summary>
    public static string UnreadableHead { get; } = Of("{x|y}P");

    /// <summary>The cursor whose text is <paramref name="text"/>.</summary>
    public static string Of(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// <paramref name="cursor"/>, a cursor a list gave, with <paramref name="head"/> written into its head: the
    /// same place in the list, and a claim about pages to skip and the list's total that the list never made.
    /// </summary>
    public static string WithHead(string cursor, string head)
    {
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return Of("{" + head + text[text.IndexOf('}', StringComparison.Ordinal)..]);
    }
}
