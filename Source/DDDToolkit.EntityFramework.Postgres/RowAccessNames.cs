using System.Text;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// The names of the functions row access rules ask: <c>schema.name</c>, which is a function's name in the
/// database, and the logical <c>owner/name</c>, which a script resolves to the schema of the context that
/// defines the function.
/// </summary>
internal static class RowAccessNames
{
    /// <summary>
    /// An owner as a module's name is written in a migration's file name: lower case, anything but ASCII
    /// letters and digits a dash, no dash at either end. The generator writes owners the same way.
    /// </summary>
    public static string NormalizeOwner(string owner)
    {
        var normalized = new StringBuilder(owner.Length);
        foreach (var character in owner.Trim().ToLowerInvariant())
        {
            normalized.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        return normalized.ToString().Trim('-') is { Length: > 0 } result ? result : "module";
    }

    /// <summary>
    /// The name rules ask a function by: <paramref name="name"/> as it is when it is <c>schema.name</c> or
    /// <c>owner/name</c>, <c>owner/name</c> for a bare name and an owner, and null for anything else.
    /// </summary>
    public static string? Logical(string name, string? owner)
    {
        var slash = name.Split('/');
        if (slash.Length == 2)
        {
            return IsOwner(slash[0]) && IsIdentifier(slash[1]) ? name : null;
        }

        if (slash.Length > 2)
        {
            return null;
        }

        var dot = name.Split('.');
        if (dot.Length == 2)
        {
            return dot.All(IsIdentifier) ? name : null;
        }

        return dot.Length == 1 && IsIdentifier(name) && owner is not null ? owner + "/" + name : null;
    }

    /// <summary>Whether <paramref name="name"/> is a logical name, <c>owner/name</c>, which a script resolves.</summary>
    public static bool IsLogical(string name) => name.Contains('/', StringComparison.Ordinal);

    /// <summary>The name part of <c>owner/name</c> or <c>schema.name</c>.</summary>
    public static string Unqualified(string name) => name[(name.LastIndexOfAny(['/', '.']) + 1)..];

    /// <summary>Whether <paramref name="part"/> is a name a script writes unquoted: a letter or an underscore, then letters, digits and underscores.</summary>
    public static bool IsIdentifier(string part)
        => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.All(static c => char.IsLetterOrDigit(c) || c == '_');

    private static bool IsOwner(string owner)
        => owner.Length > 0 && owner[0] != '-' && owner[^1] != '-' && owner.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
