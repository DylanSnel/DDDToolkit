using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace DDDToolkit.Exceptions;

/// <summary>
/// The refusals the toolkit makes itself, as codes a caller branches on, with the kind and the English text of
/// each. A domain writes its own refusals; these are the few only the toolkit can make, because it is the
/// toolkit that sees what happened.
/// <para>
/// <c>DDDToolkit.Localization</c> carries the same English texts and their Dutch translations under the same
/// codes, so <c>IFailureLocalizer.Localize(RefusalException)</c> phrases them in the reader's language like
/// any refusal of your own.
/// </para>
/// </summary>
public static class ToolkitRefusals
{
    /// <summary>
    /// The database refused a change the application's own checks let through: a row level security policy
    /// denied the row a save would insert, change or delete. The caller may not do this, whatever the
    /// application thought, so it is <see cref="RefusalKind.NotPermitted"/>, and the same command gives the same
    /// answer until somebody's rights change. <c>DDDToolkit.EntityFramework</c> makes it of a failed save, and
    /// logs a warning, since the application and the policies then disagree about a rule.
    /// </summary>
    public const string Refused = "access.refused";

    /// <summary>
    /// A signed-in user's token carries a role the host gave no database role: neither the role of a signed-in
    /// user nor one the host listed. Nothing ran for it, as that role or as any other, so it is
    /// <see cref="RefusalKind.NotPermitted"/>, and the same token gets the same answer until the host lists its
    /// role. <c>DDDToolkit.EntityFramework.Postgres</c> makes it before a context connects for such a caller.
    /// Its argument <c>Role</c> is the role the token carries.
    /// </summary>
    public const string RoleNotAllowed = "access.role-not-allowed";

    /// <summary>
    /// The kind and English text of every code. The text is a template, as a translation of it is: a placeholder
    /// such as <c>{Name}</c> stands for the argument of that name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (RefusalKind Kind, string Template)> Table =
        new ReadOnlyDictionary<string, (RefusalKind Kind, string Template)>(new Dictionary<string, (RefusalKind Kind, string Template)>(StringComparer.Ordinal)
        {
            [Refused] = (RefusalKind.NotPermitted, "The database refused this change."),
            [RoleNotAllowed] = (RefusalKind.NotPermitted, "The role this sign-in carries gives no access here: {Role}."),
        });

    /// <summary>Every code, in the order the table lists them.</summary>
    public static IReadOnlyCollection<string> Codes { get; } = Table.Keys.ToArray();

    /// <summary>The kind of refusal a code is.</summary>
    /// <param name="code">One of the codes above.</param>
    /// <exception cref="ArgumentException">The code is not one of the toolkit's.</exception>
    public static RefusalKind KindOf(string code) => Row(code).Kind;

    /// <summary>The English text of a code, as a template.</summary>
    /// <param name="code">One of the codes above.</param>
    /// <exception cref="ArgumentException">The code is not one of the toolkit's.</exception>
    public static string TemplateOf(string code) => Row(code).Template;

    /// <summary>The refusal for <paramref name="code"/>, with its kind and its English text.</summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="innerException">What the refusal was made of, such as the database's own error, for a log to show.</param>
    /// <exception cref="ArgumentException">The code is not one of the toolkit's: a refusal nobody can look up is a bug.</exception>
    public static RefusalException Of(string code, Exception? innerException = null)
    {
        var (kind, template) = Row(code);
        return new RefusalException(code, kind, template, arguments: null, innerException);
    }

    /// <summary>
    /// The refusal for <paramref name="code"/>, with its kind, its English text filled from
    /// <paramref name="arguments"/>, and the arguments themselves for a translation to use.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="arguments">The values the text names, by name; a placeholder without one is left as written.</param>
    /// <exception cref="ArgumentException">The code is not one of the toolkit's: a refusal nobody can look up is a bug.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is null.</exception>
    public static RefusalException Of(string code, params (string Name, object? Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var (kind, template) = Row(code);
        var named = new Dictionary<string, object?>(arguments.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in arguments)
        {
            named[name] = value;
        }

        return new RefusalException(code, kind, Fill(template, named), named);
    }

    /// <summary>
    /// Fills the <c>{Name}</c> placeholders of <paramref name="template"/>, in the invariant culture, because the
    /// English text reads the same wherever it runs. A placeholder with no argument is left as written.
    /// </summary>
    private static string Fill(string template, Dictionary<string, object?> arguments)
    {
        var filled = new StringBuilder(template.Length + 16);
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (close < 0)
            {
                filled.Append(template, index, template.Length - index);
                break;
            }

            filled.Append(template, index, open - index);
            if (arguments.TryGetValue(template.Substring(open + 1, close - open - 1), out var value))
            {
                filled.Append(value switch
                {
                    null => string.Empty,
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                });
            }
            else
            {
                filled.Append(template, open, close - open + 1);
            }

            index = close + 1;
        }

        return filled.ToString();
    }

    private static (RefusalKind Kind, string Template) Row(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Table.TryGetValue(code, out var row)
            ? row
            : throw new ArgumentException("'" + code + "' is not one of the toolkit's refusal codes.", nameof(code));
    }
}
