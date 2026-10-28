using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// Who the current work runs as, and the work that runs without a request. Code that begins work takes it from
/// dependency injection: a job, a worker, a queue-triggered function, a host's middleware, a seeder.
/// <code>
/// public sealed class CloseMonthJob(AccessManager access, IInvoices invoices)
/// {
///     public async Task RunAsync(CancellationToken cancellationToken)
///     {
///         using (access.BeginSystem("billing"))   // system work inside the scope of the billing module
///         {
///             await invoices.CloseMonthAsync(cancellationToken);
///         }
///     }
/// }
/// </code>
/// Code that only asks who the work runs as takes <see cref="ICurrentUser"/>, which is <see cref="CurrentUser"/> here.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a <c>Begin</c> does.</b> Each <c>Begin</c> method makes a user current for the flow of work until its result
/// is disposed. Then the user before it is current again. What is begun follows the flow of work the way an
/// <see cref="AsyncLocal{T}"/> does: into awaited calls and into tasks started inside. <see cref="ICurrentUser"/> answers
/// a begun user before the host's answer, so the work's queries run as what was begun. Begin as narrow as possible:
/// around the one call that needs it.
/// </para>
/// <para>
/// <b>The work is not the user's.</b> Beginning system work needs no user. A job that begins it runs as the system, not
/// as whoever started the job. Read what the work needs of the user, such as <c>access.CurrentUser.Id</c>, before the
/// <c>using</c>: inside it, the current user is what was begun.
/// </para>
/// <para>
/// <b>A supporting domain's own manager.</b> A supporting domain derives its access manager from this one, so an
/// application of it takes one thing. Tenancy's <c>TenancyAccessManager</c> has every member here, and the tenant's
/// besides: <c>CurrentTenant</c>, <c>BeginSystemIn</c> and <c>BeginTokenIn</c>. Its <see cref="BeginSystem"/> begins
/// system work outside every tenant.
/// </para>
/// <para>
/// It keeps no state of its own, and what one instance begins every instance sees. <c>AddCurrentUser()</c> registers it
/// as a singleton, beside <see cref="ICurrentUser"/>, and every registration of the toolkit that needs the current user
/// calls that.
/// </para>
/// </remarks>
public class AccessManager
{
    /// <summary>The access manager over the application's current user. The container makes it; your code takes it.</summary>
    /// <param name="currentUser">What <see cref="CurrentUser"/> answers: the application's <see cref="ICurrentUser"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="currentUser"/> is null.</exception>
    public AccessManager(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        CurrentUser = currentUser;
    }

    /// <summary>
    /// Who the current work runs as: a signed-in user, somebody who has not signed in, or system work. It is the
    /// application's <see cref="ICurrentUser"/>, and it reads the current flow of work each time it is asked. So inside
    /// <see cref="BeginSystem"/> it answers system work, and after the <c>using</c> the user again.
    /// </summary>
    /// <example>
    /// <code>
    /// var id = access.CurrentUser.Id;   // null for system work and for somebody who has not signed in
    /// </code>
    /// </example>
    public ICurrentUser CurrentUser { get; }

    /// <summary>
    /// Runs the work as system work until the result is disposed. Then the user before it is current again.
    /// <para>
    /// Without a scope it is system work above the policies: on Postgres it runs as the host's system role, which row
    /// level security lets past every policy. Give a module's work its scope, <c>BeginSystem("billing")</c>. Scoped
    /// system work runs as a role that cannot bypass the policies, and the module's policies read the scope.
    /// </para>
    /// </summary>
    /// <remarks>
    /// A supporting domain's manager may begin its own state beside it. Tenancy's begins system work outside every
    /// tenant, always with a scope. Where none is given, it begins Tenancy's own, <c>tenancy</c>.
    /// </remarks>
    /// <param name="scope">
    /// The scope, in lower case letters, digits, <c>_</c> and <c>-</c>, such as the module's name. <see langword="null"/>
    /// for system work without a scope.
    /// </param>
    /// <returns>The work, which ends when it is disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope. Nothing is begun then.</exception>
    public virtual IDisposable BeginSystem(string? scope = null)
        => BegunUser.Begin(scope is null ? CurrentUserSnapshot.System : CurrentUserSnapshot.SystemIn(scope));

    /// <summary>
    /// Runs the work as the signed-in user <paramref name="userId"/>, with the role <c>authenticated</c> and no other
    /// claims, until the result is disposed. Then the user before it is current again.
    /// </summary>
    /// <param name="userId">The user's id.</param>
    /// <returns>The work, which ends when it is disposed.</returns>
    /// <example>
    /// A job runs the command it queued as the user who queued it:
    /// <code>
    /// using (access.BeginAs(job.UserId))
    /// {
    ///     await handler.HandleAsync(job.Command, cancellationToken);
    /// }
    /// </code>
    /// </example>
    public IDisposable BeginAs(Guid userId) => BegunUser.Begin(CurrentUserSnapshot.User(userId));

    /// <summary>
    /// Runs the work as <paramref name="user"/> until the result is disposed. Then the user before it is current again.
    /// A host begins the request's user with it, and a job begins the user of the token that queued it:
    /// <c>access.BeginAs(CurrentUserSnapshot.FromClaims(job.Claims))</c>.
    /// </summary>
    /// <param name="user">The user to run as.</param>
    /// <returns>The work, which ends when it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="user"/> is null.</exception>
    public IDisposable BeginAs(CurrentUserSnapshot user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return BegunUser.Begin(user);
    }

    /// <summary>
    /// Makes nobody the begun user until the result is disposed, and the user before it current again afterwards.
    /// The host's answer applies inside, as it does where nothing was begun. The toolkit runs the handlers of its own
    /// background work inside it when the host requires every flow of work to begin its user, so a handler does not
    /// inherit the system work of the bookkeeping that called it.
    /// </summary>
    /// <returns>The scope, which ends when it is disposed.</returns>
    public IDisposable BeginNone() => BegunUser.Begin(null);
}
