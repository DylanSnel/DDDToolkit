namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Finds, in the text of a command, a statement that would end the transaction the caller's settings live in,
/// or change the role the rest of it runs as. Where the settings last one transaction
/// (<see cref="RowLevelSecurityScope.Transaction"/>), every statement after such a one would run with nothing
/// set, as the role the application logged in as.
/// </summary>
/// <remarks>
/// <para>
/// A text is split into statements at every <c>;</c> outside a string, a quoted name, a dollar-quoted body and
/// a comment, which is where the provider splits it too, and only the first words of each statement count:
/// <c>CASE … END</c> in a query is not the statement <c>END</c>.
/// </para>
/// <para>
/// This catches a mistake, a <c>COMMIT</c> pasted into raw SQL, not an attack: a statement may hide the same
/// in a function it calls. Whoever can send SQL of their own choosing to the application's connection is past
/// what the application can hold, here as everywhere.
/// </para>
/// </remarks>
internal static class TransactionControl
{
    /// <summary>
    /// The first words of the first statement in <paramref name="sql"/> that controls the transaction or the
    /// role itself, such as <c>COMMIT</c> or <c>SET ROLE</c>, or <see langword="null"/> when there is none.
    /// Savepoints are left alone: <c>SAVEPOINT</c>, <c>RELEASE</c> and <c>ROLLBACK TO</c> stay inside the
    /// transaction.
    /// </summary>
    public static string? Refused(string sql)
    {
        var position = 0;
        while (position < sql.Length)
        {
            var (refused, next) = Statement(sql, position);
            if (refused is not null)
            {
                return refused;
            }

            position = next;
        }

        return null;
    }

    /// <summary>Reads one statement from <paramref name="start"/>: what it is refused for, if anything, and where the next one begins.</summary>
    private static (string? Refused, int Next) Statement(string sql, int start)
    {
        // The first words, as far as any rule reads: SET LOCAL SESSION AUTHORIZATION is the longest. They end at
        // the first thing that is neither a word nor white space nor a comment.
        Span<Range> leading = stackalloc Range[4];
        var words = 0;
        var first = true;
        var position = start;

        while (position < sql.Length)
        {
            var character = sql[position];

            if (character == ';')
            {
                position++;
                break;
            }

            if (Skipped(sql, position) is { } after)
            {
                // A comment between the first words is white space; a string or a quoted name ends them.
                first &= character is '-' or '/';
                position = after;
                continue;
            }

            if (!IsWordStart(character))
            {
                first &= char.IsWhiteSpace(character);
                position++;
                continue;
            }

            var end = position + 1;
            while (end < sql.Length && IsWordPart(sql[end]))
            {
                end++;
            }

            if (first && words < leading.Length)
            {
                leading[words++] = position..end;
            }

            position = end;
        }

        return (Refusal(sql, leading[..words]), position);
    }

    /// <summary>What a statement with these first words is refused for, or <see langword="null"/>.</summary>
    private static string? Refusal(string sql, ReadOnlySpan<Range> words)
    {
        if (words.Length == 0)
        {
            return null;
        }

        bool At(ReadOnlySpan<Range> all, int index, string word) => index < all.Length && Is(sql, all[index], word);

        if (At(words, 0, "BEGIN") || At(words, 0, "COMMIT") || At(words, 0, "END") || At(words, 0, "ABORT") || At(words, 0, "DISCARD"))
        {
            return sql[words[0]].ToUpperInvariant();
        }

        if (At(words, 0, "START") && At(words, 1, "TRANSACTION"))
        {
            return "START TRANSACTION";
        }

        if (At(words, 0, "PREPARE") && At(words, 1, "TRANSACTION"))
        {
            return "PREPARE TRANSACTION";
        }

        if (At(words, 0, "ROLLBACK"))
        {
            // ROLLBACK [WORK | TRANSACTION] TO [SAVEPOINT] name goes back to a savepoint, inside the transaction.
            var to = At(words, 1, "WORK") || At(words, 1, "TRANSACTION") ? 2 : 1;
            return At(words, to, "TO") ? null : "ROLLBACK";
        }

        if (At(words, 0, "RESET"))
        {
            if (At(words, 1, "ROLE") || At(words, 1, "ALL"))
            {
                return "RESET " + sql[words[1]].ToUpperInvariant();
            }

            return (At(words, 1, "SESSION") && At(words, 2, "AUTHORIZATION")) || At(words, 1, "SESSION_AUTHORIZATION") ? "RESET SESSION AUTHORIZATION" : null;
        }

        if (At(words, 0, "SET"))
        {
            // SET [SESSION | LOCAL] ROLE …, SET [SESSION | LOCAL] SESSION AUTHORIZATION …, and the same two
            // written as settings, SET role = … and SET session_authorization = ….
            var what = At(words, 1, "LOCAL") || (At(words, 1, "SESSION") && !At(words, 2, "AUTHORIZATION")) ? 2 : 1;
            if (At(words, what, "ROLE"))
            {
                return "SET ROLE";
            }

            return (At(words, what, "SESSION") && At(words, what + 1, "AUTHORIZATION")) || At(words, what, "SESSION_AUTHORIZATION") ? "SET SESSION AUTHORIZATION" : null;
        }

        return null;
    }

    /// <summary>
    /// Where the string, quoted name, dollar-quoted body or comment that begins at <paramref name="position"/>
    /// ends, or <see langword="null"/> when none begins there. One that never closes ends with the text.
    /// </summary>
    private static int? Skipped(string sql, int position)
    {
        var character = sql[position];
        var next = position + 1 < sql.Length ? sql[position + 1] : '\0';

        switch (character)
        {
            case '\'':
                // E'…' reads a backslash as an escape; a plain string does not.
                var escapes = position > 0 && (sql[position - 1] is 'E' or 'e') && (position < 2 || !IsWordPart(sql[position - 2]));
                return Quoted(sql, position, '\'', escapes);

            case '"':
                return Quoted(sql, position, '"', escapes: false);

            case '-' when next == '-':
                var line = sql.IndexOf('\n', position);
                return line < 0 ? sql.Length : line + 1;

            case '/' when next == '*':
                // Block comments nest in Postgres.
                var depth = 1;
                var at = position + 2;
                while (at < sql.Length && depth > 0)
                {
                    if (sql[at] == '/' && at + 1 < sql.Length && sql[at + 1] == '*')
                    {
                        depth++;
                        at += 2;
                    }
                    else if (sql[at] == '*' && at + 1 < sql.Length && sql[at + 1] == '/')
                    {
                        depth--;
                        at += 2;
                    }
                    else
                    {
                        at++;
                    }
                }

                return at;

            case '$':
                // $tag$ … $tag$, the tag empty or a name; $1 is a parameter, and a '$' inside a name is part of it.
                if (position > 0 && IsWordPart(sql[position - 1]))
                {
                    return null;
                }

                var tagEnd = position + 1;
                while (tagEnd < sql.Length && IsWordPart(sql[tagEnd]) && (tagEnd > position + 1 || !char.IsAsciiDigit(sql[tagEnd])))
                {
                    tagEnd++;
                }

                if (tagEnd >= sql.Length || sql[tagEnd] != '$')
                {
                    return null;
                }

                var tag = sql.AsSpan(position, tagEnd - position + 1);
                var close = sql.AsSpan(tagEnd + 1).IndexOf(tag, StringComparison.Ordinal);
                return close < 0 ? sql.Length : tagEnd + 1 + close + tag.Length;

            default:
                return null;
        }
    }

    /// <summary>Where the quoted text that opens at <paramref name="position"/> ends; the quote doubled is the quote itself.</summary>
    private static int Quoted(string sql, int position, char quote, bool escapes)
    {
        var at = position + 1;
        while (at < sql.Length)
        {
            if (escapes && sql[at] == '\\')
            {
                at += 2;
                continue;
            }

            if (sql[at] == quote)
            {
                if (at + 1 < sql.Length && sql[at + 1] == quote)
                {
                    at += 2;
                    continue;
                }

                return at + 1;
            }

            at++;
        }

        return sql.Length;
    }

    private static bool Is(string sql, Range word, string expected)
        => sql.AsSpan(word).Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsWordStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsWordPart(char character) => char.IsLetterOrDigit(character) || character == '_';
}
