namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// A user as Supabase Auth's admin API answers one, cut down to what server code decides on. It carries no
/// address on purpose: a caller that needs one already has it, and an address that is never read back
/// cannot end up in a log or an answer.
/// </summary>
/// <param name="Id">The user's id, the <c>sub</c> of the tokens Auth issues for them.</param>
/// <param name="InvitedAt">When Auth last mailed them an invitation, if it ever did.</param>
/// <param name="LastSignInAt">When they last signed in; <see langword="null"/> for a user who never has.</param>
/// <param name="EmailConfirmedAt">When they proved the address is theirs; <see langword="null"/> until then.</param>
public sealed record SupabaseAuthUser(Guid Id, DateTimeOffset? InvitedAt, DateTimeOffset? LastSignInAt, DateTimeOffset? EmailConfirmedAt)
{
    /// <summary>Whether anyone signed in as this user yet.</summary>
    public bool HasSignedIn => LastSignInAt is not null;
}

/// <summary>A user for <see cref="SupabaseAuthAdmin.CreateUserAsync"/> to make.</summary>
/// <param name="Email">The user's e-mail address.</param>
/// <param name="Id">
/// The id the user gets, or <see langword="null"/> to let Auth choose. Give one when the application
/// already refers to the person by it, so that access given before they exist at Auth is theirs when they
/// sign in.
/// </param>
/// <param name="Password">A password to sign in with, or <see langword="null"/> for a user who signs in another way, such as the link of an invitation.</param>
/// <param name="EmailConfirmed">
/// Whether the address counts as proven without a mail to it. Leave it <see langword="false"/> for a real
/// person: they prove it the first time they sign in. <see langword="true"/> is for seeded users, whose
/// addresses nobody reads.
/// </param>
/// <param name="UserMetadata">What the user may see and change about themselves, <c>user_metadata</c>.</param>
/// <param name="AppMetadata">What only the application sets, <c>app_metadata</c>, which the user's tokens carry and the user cannot write.</param>
public sealed record SupabaseNewUser(
    string Email,
    Guid? Id = null,
    string? Password = null,
    bool EmailConfirmed = false,
    IReadOnlyDictionary<string, object?>? UserMetadata = null,
    IReadOnlyDictionary<string, object?>? AppMetadata = null)
{
    /// <summary>
    /// The id and nothing else. A record prints every member, and this one would put an address and a
    /// password into whichever log or message it was written to.
    /// </summary>
    public override string ToString()
        => $"{nameof(SupabaseNewUser)} {{ {nameof(Id)} = {(Id is { } id ? id.ToString() : "(chosen by Auth)")} }}";
}

/// <summary>What <see cref="SupabaseAuthAdmin.UpdateUserAsync"/> changes about a user; what is left <see langword="null"/> stays as it is.</summary>
/// <param name="Password">The user's new password.</param>
/// <param name="AppMetadata">Entries for <c>app_metadata</c>, merged into what is there.</param>
public sealed record SupabaseUserChange(string? Password = null, IReadOnlyDictionary<string, object?>? AppMetadata = null)
{
    /// <summary>Which parts change, never their values: a record prints every member, and this one would print a password.</summary>
    public override string ToString()
        => $"{nameof(SupabaseUserChange)} {{ {nameof(Password)} = {(Password is null ? "(unchanged)" : "(set)")}, {nameof(AppMetadata)} = {(AppMetadata is null ? "(unchanged)" : "(set)")} }}";
}

/// <summary>What goes with an invitation by e-mail.</summary>
/// <param name="RedirectTo">
/// Where the link in the mail takes the person once Auth has let them in. Auth follows it only when the
/// project's redirect allow list has it, and falls back to the project's site URL otherwise.
/// </param>
/// <param name="Data">The new user's <c>user_metadata</c>, which the mail template can read, for instance to greet them in their language.</param>
public sealed record SupabaseInvitation(string? RedirectTo = null, IReadOnlyDictionary<string, object?>? Data = null);

/// <summary>
/// What came of <see cref="SupabaseAuthAdmin.InviteByEmailAsync"/>: <see cref="Sent"/> or
/// <see cref="AlreadyRegistered"/>.
/// </summary>
/// <remarks>
/// An address that has an account is an answer, not an exception, so the code that invited can go on in its
/// own way without the difference leaking out through an error. It is for that code only. Tell the person
/// who asked for the invitation the same in both cases: whoever may invite could otherwise learn which
/// addresses have accounts.
/// </remarks>
public abstract record SupabaseInvitationResult
{
    // Closed: Auth answers one of the two below, so a switch over them is complete.
    private SupabaseInvitationResult()
    {
    }

    /// <summary>
    /// Auth mailed the invitation. The user is new, or was made earlier and never proved the address:
    /// Auth mails such a user again and answers with the id it already had.
    /// </summary>
    /// <remarks>
    /// A user who was there already keeps the password it was made with. Where the project lets anybody
    /// sign up, somebody else can have signed up with the address first and chosen that password, and gets
    /// in with it once the invited person has proven the address. Access given to the id is therefore
    /// safe in a project with sign-ups off, and for a user the application made itself with
    /// <see cref="SupabaseAuthAdmin.CreateUserAsync"/> and then invited by its id with
    /// <see cref="SupabaseAuthAdmin.InviteUserAsync"/>: making refuses an address that has a user, proven
    /// or not, and a later sign-up by somebody else does not change the user it made.
    /// </remarks>
    /// <param name="UserId">
    /// The user's id, which the application can give access to before they sign in, under the condition
    /// above.
    /// </param>
    public sealed record Sent(Guid UserId) : SupabaseInvitationResult;

    /// <summary>
    /// The address belongs to a user who has proven it, so Auth mailed nobody. It says nothing else about
    /// that user: nothing here looks one up by address.
    /// </summary>
    public sealed record AlreadyRegistered : SupabaseInvitationResult;
}
