using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Where the caller's member id comes from: which fact about the caller is compared with the members of a
/// resource. It is data, so the code that asks in C# and the functions that ask in the database read the same
/// declaration.
/// <para>
/// It is one of three things a resource's rules say apart from each other: who a member is, which is this;
/// where the roles a member holds come from (<see cref="MembershipRules.Roles"/>); and whether something above
/// the resource reaches it (<see cref="MembershipRules.ReachFromAbove"/>). Each stands by itself, so any member source
/// goes with any kind of roles, with reach from above or without.
/// </para>
/// <para>
/// Rules say none, usually: who a member is follows from what a member is known by, the second type of
/// <c>[Member&lt;...&gt;]</c> (<see cref="Inferred"/>). The others are for what that cannot tell.
/// </para>
/// </summary>
public sealed record MemberSource
{
    /// <summary>The claims of a token its user writes itself: nothing in them says who the user is.</summary>
    private const string UserMetadata = "user_metadata";

    private MemberSource(MemberSourceKind kind, string? claimPath, DatabaseFunction? function)
    {
        Kind = kind;
        ClaimPath = claimPath;
        Function = function;
    }

    /// <summary>
    /// Who a member is follows from what it is known by, the member id of <c>[Member&lt;TId, TMemberId, TRoleId,
    /// TResource&gt;]</c>: the default, which rules that say no other source have.
    /// <list type="bullet">
    /// <item>The seat id of a package that keeps an organization's seats: the caller's seat. Such a package answers it,
    /// in C# through the <c>ICallerMember</c> its join with this package registers for the resource, and in the
    /// database through the function its contribution names for its seat id, where the context that maps the resource
    /// maps that package's read model.</item>
    /// <item>An id over a <see cref="Guid"/> otherwise: the caller's user id, the <c>sub</c> of its token, as
    /// <see cref="CallerId"/>.</item>
    /// </list>
    /// A member known by anything else is a member the application says: <see cref="Claim"/> for a claim of the
    /// token, <see cref="Resolved"/> for what it resolves itself.
    /// </summary>
    public static MemberSource Inferred { get; } = new(MemberSourceKind.Inferred, null, null);

    /// <summary>
    /// The caller's user id, the <c>sub</c> of its token: for members that are users, known by an id over a
    /// <see cref="Guid"/>. What <see cref="Inferred"/> takes for such an id, said outright.
    /// </summary>
    public static MemberSource CallerId { get; } = new(MemberSourceKind.CallerId, null, null);

    /// <summary>
    /// A claim of the caller's token, as text: for members known by something an identity provider says of a
    /// user, such as a staff number. Read claims only the server can change, <c>app_metadata</c>, never a
    /// user's own <c>user_metadata</c>.
    /// </summary>
    /// <param name="path">The claim, by its path into the token's claims: <c>app_metadata.staff</c>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is not names of letters, digits and underscores joined by dots; or it reads
    /// <c>user_metadata</c>, which a user changes for itself.
    /// </exception>
    public static MemberSource Claim(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var names = path.Split('.');
        if (!names.All(IsName))
        {
            throw new ArgumentException(
                "'" + path + "' is not a path to a claim. A path is names of letters, digits and underscores joined by dots, such as 'app_metadata.staff'.",
                nameof(path));
        }

        // A user writes its own user_metadata, so whoever is known by a claim in it is whoever the user says it is.
        return string.Equals(names[0], UserMetadata, StringComparison.Ordinal)
            ? throw new ArgumentException(
                "'" + path + "' is a claim its user can change: a user writes its own user_metadata, and would be any member it names there. "
                + "Read a claim only the server can change, such as 'app_metadata.staff'.",
                nameof(path))
            : new MemberSource(MemberSourceKind.Claim, path, null);
    }

    /// <summary>
    /// An id the application resolves for the caller: for members that are not the caller itself but
    /// something the application gives its callers, such as the place somebody has in an organization, which
    /// a token does not carry and which stops counting when the organization says so.
    /// <para>
    /// In C# the application answers it, through an <see cref="ICallerMember{TResourceId, TMemberId}"/> it
    /// registers with the resource; a resource registered without one is refused, so a member is never
    /// silently read from the caller's own id. In a database that answers the questions itself, a function of
    /// the application's answers it, named here.
    /// </para>
    /// </summary>
    /// <param name="function">
    /// The function that answers the caller's member id in the database, one a package ships or the
    /// application declares: one without parameters that answers the id as the member's column stores it, or
    /// <c>NULL</c> for a caller that is nobody's member. Left out by an application whose database does not
    /// answer the questions itself; whatever writes the resource's functions refuses rules that name none.
    /// It is asked by the resource's functions, which run as their owner, so it reads every row whatever
    /// policies its tables have: it answers for the caller by a condition of its own, the caller's id or a
    /// claim, never by a policy.
    /// </param>
    public static MemberSource Resolved(DatabaseFunction? function = null)
        => new(MemberSourceKind.Resolved, null, function);

    /// <summary>Which fact about the caller it is.</summary>
    public MemberSourceKind Kind { get; }

    /// <summary>The claim's path for <see cref="MemberSourceKind.Claim"/>; <see langword="null"/> for every other kind.</summary>
    public string? ClaimPath { get; }

    /// <summary>
    /// For <see cref="MemberSourceKind.Resolved"/>, the function that answers the caller's member id in the
    /// database, or <see langword="null"/> when the rules name none; <see langword="null"/> for every other kind.
    /// </summary>
    public DatabaseFunction? Function { get; }

    private static bool IsName(string name)
        => name.Length > 0
           && !char.IsAsciiDigit(name[0])
           && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
