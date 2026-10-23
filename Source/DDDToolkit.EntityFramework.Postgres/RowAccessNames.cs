using System.Text;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// The names of the functions row access rules ask: <c>schema.name</c>, which is a function's name in the
/// database as SQL writes it, and the logical <c>owner/name</c>, which a script resolves to the schema of the
/// context that defines the function, delimited as Entity Framework delimits that schema:
/// <c>desk.tickets_i_watch</c>, <c>"Desk".tickets_i_watch</c>.
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

    /// <summary>
    /// Compares the names functions have in the database, <c>schema.name</c> as SQL writes them, by the function
    /// Postgres takes each for (<see cref="Kept"/>): <c>"Desk".f</c> and <c>desk.f</c> are two functions, in two
    /// schemas, while <c>Desk.f</c> and <c>desk.f</c> are one, since Postgres folds a name without quotes. A name
    /// that is no <c>schema.name</c>, such as a logical name nothing defines, is compared as Postgres would fold it.
    /// </summary>
    public static IEqualityComparer<string> Functions { get; } = new KeptComparer();

    /// <summary>
    /// The schema and the name Postgres keeps for <paramref name="qualified"/>, a function's name as SQL writes it,
    /// <c>schema.name</c>: a part in double quotes as it is between them, a doubled quote being one quote, and a
    /// part without them with its capitals in lower case, as Postgres folds such a name. So <c>"Desk".f</c> is
    /// <c>Desk</c> and <c>f</c>, and <c>Desk.f</c> is <c>desk</c> and <c>f</c>. Null for anything else.
    /// </summary>
    public static (string Schema, string Name)? Kept(string qualified)
    {
        var position = 0;
        if (KeptPart(qualified, ref position) is not { } schema || position >= qualified.Length || qualified[position] != '.')
        {
            return null;
        }

        position++;
        return KeptPart(qualified, ref position) is { } name && position == qualified.Length ? (schema, name) : null;
    }

    /// <summary>
    /// The part of a qualified name that starts at <paramref name="position"/>, as Postgres keeps it, with
    /// <paramref name="position"/> moved past it; null where no part starts there.
    /// </summary>
    private static string? KeptPart(string text, ref int position)
    {
        if (position < text.Length && text[position] == '"')
        {
            var kept = new StringBuilder();
            for (var i = position + 1; i < text.Length; i++)
            {
                if (text[i] != '"')
                {
                    kept.Append(text[i]);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    kept.Append('"');
                    i++;
                }
                else
                {
                    position = i + 1;
                    return kept.Length > 0 ? kept.ToString() : null;
                }
            }

            return null;
        }

        var start = position;
        while (position < text.Length && text[position] is not ('.' or '"'))
        {
            position++;
        }

        return position > start ? Folded(text[start..position]) : null;
    }

    /// <summary>
    /// A name without quotes as Postgres keeps it: its ASCII capitals in lower case. A letter beyond ASCII is kept
    /// as it is, as Postgres keeps it in a database whose encoding is UTF-8.
    /// </summary>
    private static string Folded(string name)
        => string.Create(name.Length, name, static (folded, written) =>
        {
            for (var i = 0; i < written.Length; i++)
            {
                folded[i] = char.IsAsciiLetterUpper(written[i]) ? (char)(written[i] + ('a' - 'A')) : written[i];
            }
        });

    private static bool IsOwner(string owner)
        => owner.Length > 0 && owner[0] != '-' && owner[^1] != '-' && owner.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>The comparer of <see cref="Functions"/>: two names are one where Postgres keeps the same schema and name for both.</summary>
    private sealed class KeptComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
            => ReferenceEquals(x, y) || (x is not null && y is not null && string.Equals(Key(x), Key(y), StringComparison.Ordinal));

        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Key(obj));

        /// <summary>The schema and the name as Postgres keeps them, apart by a character no name has.</summary>
        private static string Key(string name) => Kept(name) is { } kept ? kept.Schema + "\0" + kept.Name : Folded(name);
    }
}
