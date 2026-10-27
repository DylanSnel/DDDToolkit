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
    /// Access was refused, with no one rule of a module's to name: the caller may not do this, so it is
    /// <see cref="RefusalKind.NotPermitted"/>, and the same request gives the same answer until somebody's rights
    /// change. Two places make it.
    /// <list type="bullet">
    /// <item><description>
    /// The database refused a change the application's own checks let through: a row level security policy denied the
    /// row a save would insert, change or delete, or a guard of the database, a trigger that raises <c>42501</c> with
    /// the toolkit's hint, refused the statement. <c>DDDToolkit.EntityFramework</c> makes it of a failed save, and logs
    /// what it was: a warning when the application and the database disagree about a rule, and an information line when
    /// the caller's rights changed between the request's access check and the save, which it tells by asking that check
    /// again (<see cref="Access.RequestInHand.StillPassesAsync"/>). No arguments.
    /// </description></item>
    /// <item><description>
    /// A request lets one caller or another through (<see cref="Access.AccessRequirement.Any"/>, or
    /// <see cref="Access.OrOwnAttribute"/> beside a permission), and the caller is neither: its argument
    /// <see cref="ReasonsArgument"/> is each case's own refusal, its code and its arguments, in the order of the cases
    /// (<see cref="RefusalReason"/>), and the refusal's message names them for a log.
    /// </description></item>
    /// </list>
    /// </summary>
    public const string Refused = "access.refused";

    /// <summary>
    /// The argument of a <see cref="Refused"/> made at the door that holds each case's own refusal, in their order: a
    /// list of <see cref="RefusalReason"/>, its code and its arguments each, which a client reads as a list of objects.
    /// </summary>
    public const string ReasonsArgument = "Reasons";

    /// <summary>
    /// A request is about the caller's own (<see cref="Access.AccessRequirement.Own{TValue}"/>, <see cref="Access.OwnAttribute"/>)
    /// and names a value the caller does not have: another person's seat, another user. It is
    /// <see cref="RefusalKind.NotPermitted"/>, and the same request gets the same answer from the same caller. The core's
    /// <c>CurrentUserAccessCheck</c> makes it, before the handler runs, for anyone the value is not the own of: a caller who did
    /// not sign in and the application's own work too. No arguments: the value is the request's, and the caller sent it.
    /// <para>
    /// A request about the owner of an aggregate (<see cref="Access.AccessRequirement.OwnerOf{TId}"/>) is refused with it
    /// too, where the aggregate is somebody else's, or nobody's, its owner left empty: whoever it is about is not the
    /// caller.
    /// </para>
    /// </summary>
    public const string NotOwn = "access.not-own";

    /// <summary>
    /// A request is about the owner of an aggregate (<see cref="Access.AccessRequirement.OwnerOf{TId}"/>, <see cref="Access.OwnAttribute"/>
    /// or <see cref="Access.OrOwnAttribute"/> on the id of an aggregate whose owner is marked <c>[Owner]</c>), and names one
    /// there is no row of for the caller: none was ever made, it is gone, or row level security, or a query filter, keeps
    /// it from the caller. It is <see cref="RefusalKind.NotFound"/>, and one the caller may not see reads the same as one
    /// that does not exist. The core's <c>CurrentUserAccessCheck</c> makes it, before the handler runs, as Membership makes its
    /// own <c>not-found</c> for a resource the caller does not see. No arguments: the id is the request's.
    /// </summary>
    public const string NotFound = "access.not-found";

    /// <summary>
    /// A signed-in user's token carries a role the host gave no database role: neither the role of a signed-in
    /// user nor one the host listed. Nothing ran for it, as that role or as any other, so it is
    /// <see cref="RefusalKind.NotPermitted"/>, and the same token gets the same answer until the host lists its
    /// role. <c>DDDToolkit.EntityFramework.Postgres</c> makes it before a context connects for such a caller.
    /// Its argument <c>Role</c> is the role the token carries.
    /// </summary>
    public const string RoleNotAllowed = "access.role-not-allowed";

    /// <summary>
    /// A request requires a signed-in user (<c>AccessRequirement.SignedIn()</c>), and the caller is not one: it
    /// did not sign in, or it is the application's own work, which is nobody's sign-in. It is
    /// <see cref="RefusalKind.NotPermitted"/>, and the same caller gets the same answer until it signs in. The
    /// core's <c>CurrentUserAccessCheck</c> makes it, before the handler runs. No arguments.
    /// </summary>
    public const string NotSignedIn = "access.not-signed-in";

    /// <summary>
    /// What only the application's own work may do, and the caller is not that work: a signed-in user, whatever
    /// they hold, a caller who did not sign in, or work that began no user. It is
    /// <see cref="RefusalKind.NotPermitted"/>, and no permission or role gives it. System work is a scope trusted code
    /// begins around the work, not a requirement a request declares, so this is made past the door: by a
    /// supporting domain's use case that only system work may call, such as Tenancy's provisioning of a tenant,
    /// and by a handler that only system work may run. So "only the application itself" has one code. No arguments.
    /// </summary>
    public const string SystemOnly = "access.system-only";

    /// <summary>
    /// The kind and English text of every code. The text is a template, as a translation of it is: a placeholder
    /// such as <c>{Name}</c> stands for the argument of that name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (RefusalKind Kind, string Template)> Table =
        new ReadOnlyDictionary<string, (RefusalKind Kind, string Template)>(new Dictionary<string, (RefusalKind Kind, string Template)>(StringComparer.Ordinal)
        {
            [Refused] = (RefusalKind.NotPermitted, "Access to this was refused."),
            [RoleNotAllowed] = (RefusalKind.NotPermitted, "The role this sign-in carries gives no access here: {Role}."),
            [NotSignedIn] = (RefusalKind.NotPermitted, "Only a signed-in user can do this."),
            [SystemOnly] = (RefusalKind.NotPermitted, "Only the application itself can do this."),
            [NotOwn] = (RefusalKind.NotPermitted, "Only whoever this is about can do this."),
            [NotFound] = (RefusalKind.NotFound, "That was not found, or it is not one you can see."),
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
    public static RefusalException Refuse(string code, Exception? innerException = null)
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
    public static RefusalException Refuse(string code, params (string Name, object? Value)[] arguments)
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
    /// The one <see cref="Refused"/> of a combination none of whose cases the caller met: each case's own refusal, in
    /// their order, as its <see cref="ReasonsArgument"/>, and named in its message, after the code's own text, for a log.
    /// What a localizer phrases is the code's text alone, since the reasons' codes are for a client to branch on.
    /// </summary>
    /// <param name="reasons">Each case's refusal, at least one.</param>
    internal static RefusalException NoneMet(IReadOnlyList<RefusalReason> reasons)
    {
        var (kind, template) = Row(Refused);
        var named = new Dictionary<string, object?>(1, StringComparer.OrdinalIgnoreCase)
        {
            [ReasonsArgument] = new ReadOnlyCollection<RefusalReason>([.. reasons]),
        };

        var message = template.TrimEnd('.') + ": " + string.Join("; ", reasons) + ".";
        return new RefusalException(Refused, kind, message, named);
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
