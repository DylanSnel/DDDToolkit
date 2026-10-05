namespace DDDToolkit.Access;

/// <summary>
/// What a request requires of its caller before its handler runs. A request says which through
/// <see cref="IRequireAccess"/>, and <see cref="AccessChecks{TRequests}"/> holds it to that.
/// </summary>
/// <remarks>
/// A request answers with one requirement that says what it requires, from a small vocabulary:
/// <code>
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.AllowAnonymous();          // anyone, also not signed in
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.SignedIn();                // a signed-in user
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.ForTheWholeTenant(Keys.Close); // a package's case
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.RequiresSystemWork();      // the application itself
/// </code>
/// The three spelled here are about who is calling and nothing else, so the core decides them, and a host
/// without any supporting domain has them. The other cases are records that derive from this one, each owned
/// by whoever can decide it: a package ships the cases about what it keeps, with the <see cref="IAccessCheck"/>
/// that decides them, and a module adds cases of its own the same way. A record, so two requirements that say
/// the same are equal, and a test can hold every request to the requirement it is meant to declare.
/// <para>
/// A requirement says what is asked, never who may: it carries a key and the ids the request names, and the
/// check reads the rest where it is kept. What a requirement cannot say is decided where it can be: in the
/// handler, in plain sight; in the use case of the package the handler calls; and in the database's own row
/// level security.
/// </para>
/// <para>
/// There is no requirement that says nothing. A request that declares none is stopped, so a requirement that
/// was forgotten never opens a door: <see cref="AllowAnonymous"/> is how a request says that anyone may send it,
/// and it reads as meant in a review.
/// </para>
/// </remarks>
public abstract record AccessRequirement
{
    /// <summary>For the cases a package or a module declares.</summary>
    protected AccessRequirement()
    {
    }

    /// <summary>
    /// Anyone may send the request, a caller who did not sign in too: a self-service registration form, a public
    /// price list. Nobody is asked, so it passes in a module that registered no check.
    /// </summary>
    /// <remarks>
    /// Named as ASP.NET Core's <c>[AllowAnonymous]</c> is, and for the same reason: it says that the door is open
    /// on purpose. Who may send a request is not what its handler runs with: a handler that provisions a tenant
    /// for a registration form begins system work itself, in trusted code, and a caller who sent it gets nothing
    /// more by that.
    /// </remarks>
    public static Anyone AllowAnonymous() => new();

    /// <summary>
    /// The caller is a signed-in user (<see cref="Abstractions.Access.Caller.IsSignedIn"/>), who need not hold
    /// anything yet: accepting an invitation, listing the caller's own seats, registering a first tenant. Anyone
    /// else is refused with <see cref="Exceptions.ToolkitRefusals.NotSignedIn"/>: a caller who did not sign in,
    /// and the application's own work, which is nobody's sign-in.
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
    /// Only the application itself sends the request: system work, <see cref="Abstractions.Access.Caller.System"/>
    /// or a scoped <see cref="Abstractions.Access.Caller.SystemIn"/>, begun in trusted code such as seeding, an
    /// import or a job. Every user is refused with <see cref="Exceptions.ToolkitRefusals.SystemOnly"/>, whatever
    /// they hold, and so is a caller who did not sign in.
    /// </summary>
    /// <remarks>
    /// Begun means begun: <c>using (Callers.Begin(Caller.System))</c> around the work, or a supporting domain's
    /// own way to begin system work, such as Tenancy's <c>TenancyWork</c>. Work nobody began a caller for is
    /// refused as well, also in a host that does not require explicit callers, where the caller accessor answers
    /// the application itself for it: there, that is what a web request no accessor knows gets too.
    /// <para>
    /// It says who may send the request, not where the work acts: a handler that works in one tenant still has
    /// its supporting domain answer which tenant that is, and that refuses system work outside any.
    /// </para>
    /// </remarks>
    public static SystemWork RequiresSystemWork() => new();

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
    /// What <see cref="SignedIn"/> answers: a signed-in user. Decided by <see cref="CallerAccessCheck"/>, which
    /// a set of checks registered with <c>AddAccessChecks</c> asks before the module's own.
    /// </summary>
    [AccessCheckRegistration("services.AddAccessChecks<{TRequests}>(), which puts the core's CallerAccessCheck first in the module's set")]
    public sealed record SignedInUser : AccessRequirement
    {
        /// <summary>Made by <see cref="SignedIn"/>, the one spelling a request writes.</summary>
        internal SignedInUser()
        {
        }
    }

    /// <summary>
    /// What <see cref="RequiresSystemWork"/> answers: the application itself. Decided by
    /// <see cref="CallerAccessCheck"/>, which a set of checks registered with <c>AddAccessChecks</c> asks before
    /// the module's own.
    /// </summary>
    [AccessCheckRegistration("services.AddAccessChecks<{TRequests}>(), which puts the core's CallerAccessCheck first in the module's set")]
    public sealed record SystemWork : AccessRequirement
    {
        /// <summary>Made by <see cref="RequiresSystemWork"/>, the one spelling a request writes.</summary>
        internal SystemWork()
        {
        }
    }
}
