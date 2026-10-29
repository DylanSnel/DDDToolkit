using System.Diagnostics.CodeAnalysis;

namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Who the current work runs as: a signed-in user, somebody who has not signed in, or system work. Code takes it from
/// dependency injection and asks it each time it needs the answer. It reads the current flow of work on every call, so
/// inside system work that code began it answers system work, and after the <c>using</c> it answers the user again.
/// <code>
/// public sealed class PlaceOrderHandler(ICurrentUser user, OrderingContext context) : ICommandHandler&lt;PlaceOrder&gt;
/// {
///     public async ValueTask&lt;Unit&gt; Handle(PlaceOrder command, CancellationToken cancellationToken)
///     {
///         var customer = user.RequireId();   // PlaceOrder requires [SignedIn], so a signed-in user sent it
///         // ...
///     }
/// }
/// </code>
/// It only answers. Work is begun on the access manager, <c>AccessManager</c> in the <c>DDDToolkit</c> package, whose
/// <c>CurrentUser</c> is this.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the answer comes from.</b> Work begun with the access manager's <c>BeginAs</c> or <c>BeginSystem</c> runs as
/// what was begun. Other work runs as the user the host says: in ASP.NET Core with
/// <c>DDDToolkit.Auth.Supabase.AspNetCore</c>, the user of the request's validated token, or somebody who has not signed in.
/// Outside a request it is system work that no row access rule holds, the work the access manager's
/// <c>BeginSystemBypassingRowAccess()</c> begins. A host that requires every flow of work to begin its user throws there
/// instead.
/// </para>
/// <para>
/// <b>Work without a request.</b> A job, a queue-triggered function or a test begins the user it runs as on the access
/// manager:
/// <code>
/// using (access.BeginAs(job.UserId))
/// {
///     await handler.HandleAsync(job.Command, cancellationToken);
/// }
///
/// using (access.BeginSystem("billing"))   // system work inside the scope of the billing module
/// {
///     await invoices.CloseMonthAsync(cancellationToken);
/// }
/// </code>
/// What is begun follows the flow of work the way an <see cref="AsyncLocal{T}"/> does: into awaited calls and into tasks
/// started inside, and back to the user before it when the result is disposed.
/// </para>
/// <para>
/// <b>Row access rules.</b> A rule takes it as its second parameter, <c>Allows(Order order, ICurrentUser user)</c>. The
/// generator translates <see cref="Id"/>, <see cref="IsAuthenticated"/>, <see cref="Role"/> and
/// <see cref="FindClaim"/> into SQL, so a rule answers the same in C# as in the database. A rule that uses another
/// member, such as <see cref="RequireId"/> or <see cref="RequireSystemWork"/>, gets DDD00039.
/// </para>
/// <para>
/// The toolkit's implementation is <c>CurrentUser</c> in the <c>DDDToolkit</c> package, which every registration of the
/// toolkit that needs it registers as a singleton, with the <c>AccessManager</c> beside it.
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The kind of user the current work runs as.</summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    CurrentUserKind Kind { get; }

    /// <summary>
    /// The signed-in user's id, the <c>sub</c> of their token. <see langword="null"/> for system work, for somebody who
    /// has not signed in, and for a token whose <c>sub</c> is no <see cref="Guid"/>. In a row access rule it is
    /// <c>auth.uid()</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    Guid? Id { get; }

    /// <summary>
    /// Whether a user signed in. It is true when <see cref="Id"/> has a value. In a row access rule it is
    /// <c>auth.uid() IS NOT NULL</c>.
    /// <para>
    /// After it answers true, the compiler treats <see cref="Id"/> as not null, so <c>user.Id.Value</c> gives no
    /// warning. A handler behind <c>[SignedIn]</c> reads the id with <see cref="RequireId"/> instead.
    /// </para>
    /// <code>
    /// if (user.IsAuthenticated)              // a request that anyone may send
    /// {
    ///     var customer = user.Id.Value;
    /// }
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    [MemberNotNullWhen(true, nameof(Id))]
    bool IsAuthenticated { get; }

    /// <summary>
    /// Whether the current work is system work, with a scope or without one. <see cref="Kind"/> tells the two apart.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    bool IsSystem { get; }

    /// <summary>The scope of scoped system work, as <c>BeginSystem(scope)</c> was given it. <see langword="null"/> for every other kind.</summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    string? SystemScope { get; }

    /// <summary>
    /// The role the user's token carries: <c>authenticated</c>, <c>anon</c>, or a role that the identity provider gave
    /// the user. <see langword="null"/> for system work, and for a token without a role claim. In a row access rule it
    /// is <c>auth.role()</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    string? Role { get; }

    /// <summary>
    /// The current user, fixed at this moment. Code that keeps the answer or passes it on takes it, as row level
    /// security does when it sets the user on a connection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    CurrentUserSnapshot Snapshot { get; }

    /// <summary>
    /// Whether code began the current user for this flow of work, with the access manager's <c>BeginAs</c>,
    /// <c>BeginSystem</c> or <c>BeginSystemBypassingRowAccess</c>. False where the host answers, and inside
    /// <c>BeginNone()</c>. It never throws.
    /// </summary>
    bool IsBegun { get; }

    /// <summary>
    /// A claim of the user's token, as text, or <see langword="null"/> when the token has no such claim.
    /// <c>FindClaim("email")</c> reads a top-level claim, and <c>FindClaim("app_metadata.role")</c> a nested one. In a row
    /// access rule it is <c>auth.jwt() -&gt;&gt; 'email'</c> and <c>auth.jwt() #&gt;&gt; '{app_metadata,role}'</c>.
    /// </summary>
    /// <param name="path">The claim's name, or a path of names separated by dots.</param>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    string? FindClaim(string path);

    /// <summary>
    /// The signed-in user's id, for code that only a signed-in user may run. A handler behind <c>[SignedIn]</c> reads
    /// the id with it and checks nothing itself.
    /// <code>
    /// public async Task HandleAsync(PlaceOrder command, CancellationToken cancellationToken)
    /// {
    ///     var customer = user.RequireId();   // access.not-signed-in for anyone else
    ///     // ... the order
    /// }
    /// </code>
    /// Anyone else is refused with <c>access.not-signed-in</c>, as <c>[SignedIn]</c> refuses them. So a handler that
    /// runs without the access check refuses such a caller in the same way. Where both cases happen, such as behind
    /// <c>[AllowAnonymous]</c>, code tests <see cref="IsAuthenticated"/> and then reads <see cref="Id"/>.
    /// A row access rule cannot call it. The generator reports DDD00039 on the call, and a rule compares
    /// <see cref="Id"/> instead.
    /// </summary>
    /// <returns>The value of <see cref="Id"/>.</returns>
    /// <exception cref="Exception">
    /// <c>RefusalException</c> with <c>access.not-signed-in</c> for somebody who has not signed in, for system work, and
    /// for a token whose <c>sub</c> is no <see cref="Guid"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    Guid RequireId();

    /// <summary>
    /// Refuses everyone but system work that code began for this flow with the access manager's
    /// <c>BeginSystem(scope)</c> or <c>BeginSystemBypassingRowAccess()</c>. A handler or a use case that only system
    /// work may run calls it first.
    /// <code>
    /// public async Task HandleAsync(ImportPrices command, CancellationToken cancellationToken)
    /// {
    ///     user.RequireSystemWork();          // access.system-only for anyone else
    ///     // ... the import
    /// }
    /// </code>
    /// A flow that began nothing is refused, even where the host answers system work for it. The request's own
    /// requirement still says who may send it.
    /// </summary>
    /// <returns>The system work the flow runs as.</returns>
    /// <exception cref="Exception">
    /// <c>RefusalException</c> with <c>access.system-only</c> for a user, for somebody who has not signed in, and for work
    /// that began no system work.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <c>NoCurrentUserException</c>: nothing began a user for the work, and the host requires every flow of work to begin one.
    /// </exception>
    CurrentUserSnapshot RequireSystemWork();
}
