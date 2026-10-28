using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Makes the access manager of one kind of resource over what a storage answers. A storage package calls it where it
/// registers the resource; an application takes the manager it made, <see cref="MembershipAccessManager{TResource}"/>.
/// </summary>
public static class MembershipAccessManager
{
    /// <summary>The most resources one question about several is asked about.</summary>
    public const int MostResources = 200;

    /// <summary>The access manager of <typeparamref name="TResource"/>, over what <paramref name="storage"/> answers.</summary>
    /// <param name="storage">What the storage answers about the resource, for the current caller.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="storage"/> is null.</exception>
    public static MembershipAccessManager<TResource> Over<TResource, TResourceId>(IMembershipStorage<TResource, TResourceId> storage)
        where TResource : class, IEntity<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(storage);

        return new MembershipAccessManager<TResource>(new MemberAnswers<TResource, TResourceId>(storage));
    }

    /// <summary>
    /// Requires the caller to hold <paramref name="permission"/> on a resource, in the order every question refuses in:
    /// the caller's own refusal when it is nobody; then <c>not-found</c> for a resource it may not see, exactly as for one
    /// that does not exist; then <c>not-permitted</c>, naming the permission.
    /// </summary>
    internal static async Task<MemberHold<T>> RequireAsync<T>(MembershipRules rules, Action requireCurrentUser, Func<Task<MemberHold<T>?>> hold, string permission)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        // The caller before anything is read: nobody is refused as nobody, without a statement made for it.
        requireCurrentUser();

        var codes = rules.Codes;
        var held = await hold().ConfigureAwait(false) ?? throw codes.Refuse(MembershipRefusals.NotFound);

        return held.Via is null
            ? throw codes.Refuse(MembershipRefusals.NotPermitted, ("Permission", permission))
            : held;
    }

    /// <summary>
    /// How the caller holds <paramref name="permission"/> on a resource it may see, or <see langword="null"/> when it
    /// does not hold it, refusing as <see cref="RequireAsync{T}"/> does up to <c>not-found</c>.
    /// </summary>
    internal static async Task<MemberVia?> ViaAsync<T>(MembershipRules rules, Action requireCurrentUser, Func<Task<MemberHold<T>?>> hold, string permission)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        // The same first step as every question: nobody is told a resource is not found, as if it had been
        // looked for, when the questions cannot be asked about that caller at all.
        requireCurrentUser();

        var held = await hold().ConfigureAwait(false) ?? throw rules.Codes.Refuse(MembershipRefusals.NotFound);
        return held.Via;
    }
}

/// <summary>
/// What the current caller may do on the resources of one kind, such as <c>MembershipAccessManager&lt;Board&gt;</c>:
/// whether it holds a permission on a board, how and until when, and which permissions it holds on several. Your code
/// takes it from the container, as any service:
/// <code>
/// public sealed class ArchiveBoardHandler(IBoardRepository boards, MembershipAccessManager&lt;Board&gt; boardAccess) : ICommandHandler&lt;ArchiveBoard&gt;
/// {
///     public async ValueTask&lt;Unit&gt; Handle(ArchiveBoard command, CancellationToken cancellationToken)
///     {
///         var board = await boards.GetAsync(command.Board, cancellationToken);
///         var hold = await boardAccess.HoldAsync(board, BoardPermissions.Edit, cancellationToken);
///         // ...
///     }
/// }
/// </code>
/// <para>
/// Each question takes the resource, and has an overload that takes its id, for a caller that has only the id, as a
/// request does: <c>boardAccess.HoldAsync(command.Board, ...)</c> (<see cref="MembershipAccessManagerExtensions"/>). An
/// answer about a resource names it as it was asked about: the resource for the resource, the id for an id.
/// </para>
/// <para>
/// Which resources the caller holds a permission on is asked of the resource's own set, inside a statement of your
/// own: <c>db.Boards.WhereIHold(BoardPermissions.View)</c>, which the storage package writes. <see cref="Reach"/> is the
/// lower level under it, for a statement that reads more than which resources are held.
/// </para>
/// <para>
/// The order of what a caller is refused with is the same for every question: the caller's own refusal when it is
/// nobody; then <c>not-found</c> for a resource it may not see, exactly as for one that does not exist, so nobody learns
/// a resource is there; then <c>not-permitted</c>, naming the permission, for a resource it sees without holding the
/// permission. All under the codes of the resource (<see cref="MembershipRules.Codes"/>).
/// </para>
/// <para>
/// It answers what a caller holds and nothing more. Who may add a member, give a role or hand a resource on is the
/// application's to decide, by requiring a permission of its choice on each of its commands, and by whatever further
/// rule it writes over the answer. The storage package registers it for each kind of resource, per scope; every
/// question is asked for the caller that is current when it is asked, so it follows every <c>Begin</c> made after it
/// was handed out.
/// </para>
/// </summary>
/// <typeparam name="TResource">The resource's aggregate: the application's class that holds the members.</typeparam>
public sealed class MembershipAccessManager<TResource>
    where TResource : class
{
    private readonly MemberAnswers<TResource> _answers;

    /// <summary>The access manager over what a storage answers. Made by <see cref="MembershipAccessManager.Over{TResource, TResourceId}"/>.</summary>
    internal MembershipAccessManager(MemberAnswers<TResource> answers) => _answers = answers;

    /// <summary>The rules of the resource, with the codes it refuses under.</summary>
    public MembershipRules Rules => _answers.Rules;

    /// <summary>What the questions by id ask: the storage, closed over the resource's id.</summary>
    internal object Storage => _answers.Storage;

    /// <summary>
    /// Refuses a caller nothing can be answered for at all, with that caller's own refusal. Where the caller's member
    /// id is its own id or a claim there is no such caller: one that is nobody's member simply reaches nothing, so this
    /// returns for everybody. Where the application resolves who a caller is as a member
    /// (<see cref="MemberSource.Resolved"/>), a caller the application refuses outright, somebody who is nobody in the
    /// organization the resources belong to, is refused here, before anything is read.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody the questions can be asked about.</exception>
    public void RequireCurrentUser() => _answers.RequireCurrentUser();

    /// <summary>
    /// The resources the caller reaches for <paramref name="permission"/>, as a set a statement of your own takes: the
    /// lower level under <c>db.Boards.WhereIHold(permission)</c>, for a statement that reads how each resource is held,
    /// or one on another context than the request's. Those it is a member of now with a role that gives the permission,
    /// for a permission that being a member gives those it is a member of at all, for a permission of the resource those
    /// it owns, and, where the rules let a resource be reached from above, those that sit where the caller holds the
    /// permission, or below. Every resource for the application's own work; none for a caller that is nobody.
    /// <para>
    /// A reach says where one permission is held, and nothing of the others. Through its members a resource is held
    /// only where it is seen. From above it is seen by the permission every member holds
    /// (<see cref="MembershipRules.EveryMemberPermission"/>), so a caller that holds another permission there and not that
    /// one is within this reach without seeing the resource. <see cref="HoldAsync"/> and <see cref="PermissionsOnAsync"/>
    /// answer only for what the caller sees; a statement of your own that should do the same puts the reach for the
    /// permission that sees in front of this one.
    /// </para>
    /// </summary>
    /// <param name="permission">A permission.</param>
    public MemberReach<TResource> Reach(string permission) => _answers.Reach(permission);

    /// <summary>
    /// The resources the caller sees, as a set a statement of your own takes: those it is a member of now, whatever roles
    /// it holds there; those it owns, from the moment the resource names it; and, where the rules let a resource be reached
    /// from above and name the permission every member holds (<see cref="MembershipRules.EveryMemberPermission"/>), those
    /// that sit where the caller holds that permission, or below. What <see cref="HoldAsync"/> and
    /// <see cref="PermissionsOnAsync"/> answer about, and what a statement of your own puts in front of a reach for another
    /// permission so it answers about the same resources. Every resource for the application's own work.
    /// </summary>
    public MemberReach<TResource> ReachSeen() => _answers.ReachSeen();

    /// <summary>
    /// What the caller holds on <paramref name="resource"/>, in one statement: <see langword="null"/> when the caller
    /// does not see it, which is also the answer for a resource that no longer exists; otherwise the resource, the
    /// version it is at, and how the caller holds <paramref name="permission"/> on it and until when, if it does. It
    /// refuses nobody: a module builds its own answers on it.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="permission">A permission.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
    public Task<MemberHold<TResource>?> HoldAsync(TResource resource, string permission, CancellationToken cancellationToken)
        => _answers.HoldAsync(resource, permission, cancellationToken);

    /// <summary>
    /// Requires the caller to hold <paramref name="permission"/> on <paramref name="resource"/>: what a command about one
    /// resource passes before it changes it. One statement.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="permission">The permission needed on it.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The resource, with the version it was checked at and how the permission is held.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> when it may not see the resource; <c>not-permitted</c>,
    /// with the <c>Permission</c>, when it sees the resource but does not hold the permission on it.
    /// </exception>
    public Task<MemberHold<TResource>> RequireAsync(TResource resource, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return MembershipAccessManager.RequireAsync(Rules, RequireCurrentUser, () => HoldAsync(resource, permission, cancellationToken), permission);
    }

    /// <summary>
    /// How the caller holds <paramref name="permission"/> on a resource it may see, or <see langword="null"/> when it does
    /// not hold it: for a query that answers with what its caller may do, where not holding the permission is an answer
    /// and not a refusal. One statement.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="permission">A permission.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> when the caller may not see the resource.
    /// </exception>
    public Task<MemberVia?> ViaAsync(TResource resource, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return MembershipAccessManager.ViaAsync(Rules, RequireCurrentUser, () => HoldAsync(resource, permission, cancellationToken), permission);
    }

    /// <summary>
    /// The permissions of <paramref name="permissions"/> the caller holds on each of <paramref name="resources"/> it sees,
    /// in one statement whatever their number, keyed by the resources that were given. A resource the caller does not
    /// see, or holds none of the permissions on, is not in the answer. What is answered is for showing: whether a command
    /// may run is asked again when it runs.
    /// </summary>
    /// <param name="resources">The resources asked about, at most <see cref="MembershipAccessManager.MostResources"/> different ones.</param>
    /// <param name="permissions">Permissions.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentException">
    /// More than <see cref="MembershipAccessManager.MostResources"/> different resources are asked about.
    /// </exception>
    public Task<IReadOnlyDictionary<TResource, IReadOnlySet<string>>> PermissionsOnAsync(
        IReadOnlyCollection<TResource> resources,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
        => _answers.PermissionsOnAsync(resources, permissions, cancellationToken);
}
