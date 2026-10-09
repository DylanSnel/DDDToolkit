using System.Globalization;

namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// Supabase Auth's admin API refused a call of <see cref="SupabaseAuthAdmin"/>, or answered something that
/// is not what the call asks for.
/// </summary>
/// <remarks>
/// The message is put together here from the status and the error code and from nothing Auth wrote: its
/// own texts can quote what was sent, a database error names the value that clashed, and a gateway may
/// answer a whole page. So an address, a password or the secret key cannot reach a log through this
/// exception, whatever the host does with it.
/// </remarks>
public sealed class SupabaseAuthAdminException : Exception
{
    /// <summary>The error code Auth gives an address that already belongs to a user.</summary>
    internal const string AddressExists = "email_exists";

    /// <summary>The error code Auth gives an id it has no user for.</summary>
    internal const string NoSuchUser = "user_not_found";

    /// <summary>PostgreSQL's code for a value that a unique index already has, which Auth passes on as it got it.</summary>
    internal const string UniqueViolation = "23505";

    /// <summary>An exception for a call Auth answered with <paramref name="status"/>.</summary>
    /// <param name="status">The HTTP status of Auth's answer.</param>
    /// <param name="authErrorCode">Auth's error code, such as <c>not_admin</c>, when the answer had one.</param>
    /// <param name="operation">What was being done, in a few words that follow "while", such as "creating a user".</param>
    public SupabaseAuthAdminException(int status, string? authErrorCode, string? operation = null)
        : this(MessageOf(status, authErrorCode, operation, detail: null), status, authErrorCode)
    {
    }

    private SupabaseAuthAdminException(string message, int status, string? authErrorCode)
        : base(message)
    {
        Status = status;
        AuthErrorCode = authErrorCode;
    }

    /// <summary>The HTTP status of Auth's answer.</summary>
    public int Status { get; }

    /// <summary>
    /// Auth's error code, such as <c>not_admin</c>, <c>validation_failed</c> or
    /// <c>over_email_send_rate_limit</c>; <see langword="null"/> when the answer had none, as a gateway's
    /// has not.
    /// </summary>
    public string? AuthErrorCode { get; }

    /// <summary>
    /// Whether Auth refused because the address already belongs to a user, which is what
    /// <see cref="SupabaseAuthAdmin.CreateUserAsync"/> throws for one. Like
    /// <see cref="SupabaseInvitationResult.AlreadyRegistered"/> it is for server code only and not for the
    /// person who asked.
    /// </summary>
    public bool AddressAlreadyRegistered => Status == 422 && AuthErrorCode == AddressExists;

    /// <summary>
    /// Whether the database under Auth refused a value that has to be unique and is there already. When a
    /// user is made that is the id, for one that is taken, or the address, when another call registered it
    /// at the same moment: Auth looks for the address before it writes, and of two calls that both found
    /// nothing the database lets one write. The answer does not say which of the two it was in a form that
    /// is safe to read, so a caller that gave an id asks <see cref="SupabaseAuthAdmin.FindUserAsync"/>
    /// whether that id has a user.
    /// </summary>
    public bool DuplicateKey => Status == 500 && AuthErrorCode == UniqueViolation;

    /// <summary>An answer with a success status that is not what the call answers with, such as a page from something in between.</summary>
    internal static SupabaseAuthAdminException Unreadable(int status, string operation)
        => new(MessageOf(status, authErrorCode: null, operation, "The answer is not one Auth's admin API gives for this, so something else answered: " + CheckTheAddress + "."), status, authErrorCode: null);

    /// <summary>Auth made the user, but under an id of its own.</summary>
    internal static SupabaseAuthAdminException AnotherId(int status, Guid made)
        => new(
            MessageOf(status, authErrorCode: null, "creating a user",
                $"Auth made the user under an id of its own, {made}, instead of the one it was given, so this Auth server does not take an id. " +
                "The user was left in place: delete it, or refer to the person by that id."),
            status,
            authErrorCode: null);

    /// <summary>The user to invite has no e-mail address, so there is nowhere to mail an invitation.</summary>
    internal static SupabaseAuthAdminException NoAddress(int status, Guid user)
        => new(
            MessageOf(status, authErrorCode: null, "inviting a user",
                $"User {user} has no e-mail address, as one who signs in by phone has none, so there is nowhere to mail an invitation."),
            status,
            authErrorCode: null);

    /// <summary>Auth mailed an invitation, but to another user than the one that was asked for.</summary>
    internal static SupabaseAuthAdminException InvitedAnother(Guid user)
        => new(
            MessageOf(200, authErrorCode: null, "inviting a user",
                $"Auth invited another user than {user}, the one asked for. Either that user was removed while it was being invited, " +
                "or it signs in through single sign-on, where its address is not its alone and an invitation by e-mail is for somebody else. " +
                "Nothing was given to the user that was mailed: an application gives access by id."),
            200,
            authErrorCode: null);

    /// <summary>What a host checks when something other than Auth answered: the address it said Auth is at.</summary>
    private const string CheckTheAddress = "check the project URL this was registered with, and SupabaseAuthOptions.AuthUrl where one is set";

    private static string MessageOf(int status, string? authErrorCode, string? operation, string? detail)
    {
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"Supabase Auth's admin API answered {status}{(authErrorCode is null ? "" : $" ({authErrorCode})")}{(operation is null ? "" : $" while {operation}")}.");

        return (detail, status, authErrorCode) switch
        {
            ({ } text, _, _) => message + " " + text,
            (null, 401 or 403, _) => message + " It takes the project's secret key, or on a bare Auth server a token with the service role: check the key this was registered with.",

            // Auth says what it did not find. A 404 that says nothing came from something that has no such
            // route: a URL that is not a project's, or an AuthUrl that is a project's URL, or a gateway's
            // address without the /auth/v1 under which the gateway serves Auth. AuthUrl is taken as it is
            // written, so the message says where each kind of Auth answers.
            (null, 404, null) => message + " Nothing at that address knows the admin API: " + CheckTheAddress
                + ". A project's own Auth needs no AuthUrl. A bare Auth server answers at its root; a project's gateway"
                + " reached under another name than its tokens carry serves Auth under /auth/v1, so there AuthUrl ends in /auth/v1.",
            (null, >= 300 and <= 399, _) => message + " That is a redirect, and none is followed, so that the secret key goes to Auth's address and nowhere else: " + CheckTheAddress + ".",
            _ => message,
        };
    }
}
