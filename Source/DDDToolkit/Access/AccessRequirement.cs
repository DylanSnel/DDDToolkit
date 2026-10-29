namespace DDDToolkit.Access;

/// <summary>
/// What a request requires of its caller before its handler runs: who may send it. A request says which through
/// <see cref="IRequireAccess"/>, and <see cref="AccessChecks{TRequests}"/> holds it to that.
/// </summary>
/// <remarks>
/// A request answers with a requirement that says what it requires, from a small vocabulary. Where the requirement
/// reads no value of the request but a permission and an id it carries, an attribute on the request says it, and the
/// toolkit's generator turns the attribute into the same call at compile time:
/// <code>
/// [AllowAnonymous]                                          // AccessRequirement.AllowAnonymous(): anyone, also not signed in
/// [SignedIn]                                                // AccessRequirement.SignedIn(): a signed-in user
/// [RequiresPermission(BillingPermissions.Close)]                          // a package's case: TenancyAccess.AtTenant(BillingPermissions.Close)
/// [RequiresPlan(Plan.Pro), RequiresPermission(BillingPermissions.Close)]  // both: AccessRequirement.All(...), in the order they are written
/// </code>
/// and where it is computed from the request's values, or is one case or another, the request answers
/// <see cref="IRequireAccess.RequiredAccess"/> itself:
/// <code>
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; Unit is { } unit
///     ? TenancyAccess.AtUnit(BillingPermissions.Close, unit)
///     : TenancyAccess.AtTenant(BillingPermissions.Close);
///
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.Any(
///     TenancyAccess.Operator(),
///     TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));
/// </code>
/// The cases spelled here are about who is calling, or combine other cases, so the core decides them, and a host
/// without any supporting domain has them. The other cases are records that derive from this one, each owned by
/// whoever can decide it: a package ships the cases about what it keeps, with the <see cref="IAccessCheck"/> that
/// decides them, and a module adds cases of its own the same way. A record, so two requirements that say the same
/// are equal, and a test can hold every request to the requirement it is meant to declare.
/// <para>
/// A requirement says who may send a request. What the work runs with is begun on purpose. The application's own work
/// is no requirement but a scope, begun by trusted code around the work with <c>access.BeginSystem(scope)</c> on
/// <see cref="AccessManager"/>, or on a supporting domain's manager that derives from it, such as Tenancy's, after which
/// the caller before it is current again. A check may begin work around the handler of a request it let through
/// (<see cref="IBeginsWork"/>), as Tenancy's does for a command an operator sends. And it says what is asked, never who may: it carries a permission and the ids the request
/// names, and the check reads the rest where it is kept. What a requirement cannot say is decided where it can
/// be: in the handler, in plain sight; in the use case of the package the handler calls; and in the database's
/// own row level security, which asks again for every row, as the caller.
/// </para>
/// <para>
/// There is no requirement that says nothing. A request that declares none is stopped, so a requirement that
/// was forgotten never opens a door: <see cref="AllowAnonymous"/> is how a request says that anyone may send it,
/// and it reads as meant in a review.
/// </para>
/// </remarks>
public abstract partial record AccessRequirement
{
    /// <summary>For the cases a package or a module declares.</summary>
    protected AccessRequirement()
    {
    }

    /// <summary>
    /// Anyone may send the request, a caller who did not sign in too: a self-service registration form, a public
    /// price list. Nobody is asked, so it passes in a module that registered no check. A request says it with
    /// <see cref="AllowAnonymousAttribute"/> as well.
    /// </summary>
    /// <remarks>
    /// Named as ASP.NET Core's <c>[AllowAnonymous]</c> is, and for the same reason: it says that the door is open
    /// on purpose. Who may send a request is not what its handler runs with: a handler that provisions a tenant
    /// for a registration form begins system work itself, in trusted code, and a caller who sent it gets nothing
    /// more by that. It combines with nothing (<see cref="All"/>, <see cref="Any"/>): beside another case it would
    /// open the door that case closes, or say nothing at all.
    /// </remarks>
    public static Anyone AllowAnonymous() => new();

    /// <summary>
    /// The caller is a signed-in user (<see cref="Abstractions.Access.ICurrentUser.IsAuthenticated"/>), who need not hold
    /// anything yet: accepting an invitation, listing the caller's own seats, registering a first tenant. Anyone
    /// else is refused with <see cref="Exceptions.ToolkitRefusals.NotSignedIn"/>: a caller who did not sign in,
    /// and the application's own work, which is nobody's sign-in. A request says it with
    /// <see cref="SignedInAttribute"/> as well. The handler reads the caller's id with
    /// <see cref="Abstractions.Access.ICurrentUser.RequireId"/>.
    /// </summary>
    /// <remarks>
    /// Signed in means that the caller's token names a user. An identity provider that signs a visitor in
    /// without an account, as Supabase Auth's anonymous sign-in does, gives a token that names one, marked with
    /// the <c>is_anonymous</c> claim: such a caller is signed in here. A handler that should take only an account
    /// with a verified identity, such as one that makes the caller the administrator of a new tenant, refuses
    /// that claim itself, as Tenancy's acceptance of an invitation does.
    /// </remarks>
    public static SignedInUser SignedIn() => new();

    /// <summary>
    /// Every one of <paramref name="requirements"/> holds: the caller meets each, in the order they are given, and a
    /// refusal is the first failing case's own. Cases of different owners combine, a permission on a project of Membership's
    /// with a permission at a unit of Tenancy's say, and a combination may hold another.
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.All(
    ///     MemberAccess.On(ProjectPermissionNames.Edit, Project, ExpectedVersion),
    ///     TenancyAccess.AtUnit(ProjectPermissionNames.Open, To));
    /// </code>
    /// Requirement attributes stacked on one request are this, in the order they are written above it.
    /// </summary>
    /// <remarks>
    /// Each case is decided by the check that decides it alone, so every case needs one: a combination with a case
    /// no check of the module decides is stopped as that case would be, before anything is asked. The cases are asked
    /// one after the other, and one that refuses ends it, so put the one that reads nothing first.
    /// </remarks>
    /// <param name="requirements">The cases, at least one, none of them <see cref="AllowAnonymous"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requirements"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="requirements"/> is empty, holds a null, or holds <see cref="AllowAnonymous"/>.
    /// </exception>
    public static AllOf All(params AccessRequirement[] requirements) => new(requirements);

    /// <summary>
    /// One of <paramref name="requirements"/> holds: the first case the caller meets lets it through, and the cases
    /// after it are not asked. When none does, the refusal is one <see cref="Exceptions.ToolkitRefusals.Refused"/>,
    /// whose argument <see cref="Exceptions.ToolkitRefusals.ReasonsArgument"/> holds each case's own refusal, its code
    /// and its arguments, in the order of the cases. A combination may hold another, and cases of different owners
    /// combine.
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.Any(
    ///     TenancyAccess.Operator(),
    ///     TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));
    /// </code>
    /// No attribute says it, since stacked attributes all hold: a request whose caller may be one or another says it
    /// as <see cref="IRequireAccess.RequiredAccess"/>, except for the request about the caller's own, which
    /// <see cref="OrOwnAttribute"/> says.
    /// </summary>
    /// <remarks>
    /// The door decides per request, and reads no row the request is about. Under row level security every policy
    /// that lets a role read or write a table is asked of each row, and Postgres lets a row through that one of them
    /// allows: so where a request lets two kinds of caller through to the application's own table, each kind needs a
    /// row rule of its own, or the one the door let in through the other case reads nothing.
    /// <para>
    /// Each case is decided by the check that decides it alone, so every case needs one: a combination with a case no
    /// check of the module decides is stopped as that case would be, before anything is asked. A case that fails with
    /// anything but a refusal, a lost race say, ends it as that failure. Put the case that reads nothing first.
    /// </para>
    /// </remarks>
    /// <param name="requirements">The cases, at least one, none of them <see cref="AllowAnonymous"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requirements"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="requirements"/> is empty, holds a null, or holds <see cref="AllowAnonymous"/>.
    /// </exception>
    public static AnyOf Any(params AccessRequirement[] requirements) => new(requirements);

    /// <summary>
    /// The request is about the caller's own <typeparamref name="TValue"/>: <paramref name="value"/> is the one the
    /// caller has. Anyone else is refused with <see cref="Exceptions.ToolkitRefusals.NotOwn"/>. The request names the
    /// value, and the check compares it with the caller's at the door, so the request need not know who sends it.
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.Any(
    ///     AccessRequirement.Own(Seat),                                  // the caller's own seat,
    ///     TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));    // or the permission for every seat
    /// </code>
    /// A request says it with attributes as well: <see cref="OrOwnAttribute"/> beside <see cref="RequiresPermissionAttribute"/>
    /// for the caller's own or the permission, <see cref="OwnAttribute"/> with <see cref="OwnOnlyAttribute"/> for the
    /// caller's own alone.
    /// </summary>
    /// <remarks>
    /// What the caller's own value is, whoever knows says, with an <see cref="IOwnedByCurrentUser{TValue}"/>: the core for a
    /// <see cref="Guid"/>, the caller's verified identity, which is the id its token names; Tenancy for its seat id, the
    /// caller's seat in the tenant of the request; and the application for a type of its own, its own user id say,
    /// with a small implementation it registers, <c>services.AddOwnedByCurrentUser&lt;UserId, CurrentUserId&gt;()</c>. A value of
    /// a type nobody said that for lets nobody through: the request is stopped as one with a requirement no check
    /// decides.
    /// </remarks>
    /// <param name="value">The value the request names, which the caller's own is compared with.</param>
    /// <typeparam name="TValue">What the request names: the caller's identity, a seat, a user id of the application's.</typeparam>
    public static Owned<TValue> Own<TValue>(TValue value)
        where TValue : struct, IEquatable<TValue>
        => new(value);

    /// <summary>
    /// What <see cref="AllowAnonymous"/> answers: anyone may send the request. <see cref="AccessChecks{TRequests}"/>
    /// decides it itself, by asking nobody.
    /// </summary>
    public sealed record Anyone : AccessRequirement
    {
        /// <summary>Made by <see cref="AllowAnonymous"/>, the one spelling a request writes.</summary>
        internal Anyone()
        {
        }
    }

    /// <summary>
    /// What <see cref="SignedIn"/> answers: a signed-in user. Decided by <see cref="CurrentUserAccessCheck"/>, which
    /// a set of checks registered with <c>AddAccessChecks</c> asks before the module's own.
    /// </summary>
    [AccessCheckRegistration("services.AddAccessChecks<{TRequests}>(), which puts the core's CurrentUserAccessCheck first in the module's set")]
    public sealed record SignedInUser : AccessRequirement
    {
        /// <summary>Made by <see cref="SignedIn"/>, the one spelling a request writes.</summary>
        internal SignedInUser()
        {
        }
    }
}
