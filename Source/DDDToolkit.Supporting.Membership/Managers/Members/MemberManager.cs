using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// The members of one kind of resource: adding them, giving and taking their roles, removing them, leaving, naming
/// another owner, opening a new resource, and listing who is on one. Your code takes it as <c>BoardMemberManager</c>, a
/// class the package's generator writes for your member class, closed over your resource and its ids, and the
/// container makes per scope.
/// <code>
/// public sealed class AddMemberHandler(BoardMemberManager members) : ICommandHandler&lt;AddMember, Board&gt;
/// {
///     public async ValueTask&lt;Board&gt; Handle(AddMember command, CancellationToken cancellationToken)
///         =&gt; await members.AddAsync(command.Board, command.Seat, command.Role, until: null, cancellationToken);
/// }
/// </code>
/// <para>
/// Each change takes the resource, or its id. Both do the same: the manager loads the resource itself, tracked, and
/// answers it as it changed and saved it. A member and a role are ids, since a member is somebody of another module
/// and a role is a row of your role class or a name the rules declare. After the cancellation token comes the version
/// the caller last read, for a command that names one: a resource at another version is a lost race.
/// </para>
/// <para>
/// <b>What a change checks, in order.</b> The caller's right first, as the access manager refuses: the caller's own
/// refusal when it is nobody, <c>not-found</c> for a resource it may not see, <c>not-permitted</c> naming the
/// permission. A change of the members needs the permission the rules mark <c>ManagesMembers</c>, and naming an owner
/// the one marked <c>NamesOwner</c>; a resource that marks none lets only system work make the change. Where the
/// request in hand was checked for that permission on that resource, the manager takes the check's answer and asks
/// nothing again. Then whether the member and the role may go on the list: an active member where the application
/// registered an <see cref="IMemberDirectory{TResourceId, TMemberId}"/>, a role in use that goes to a member. Then the
/// resource's guard, <c>OnChangingMembers()</c>, and the member list's rules, then the save.
/// </para>
/// <para>
/// <b>The caller's own place.</b> A caller that takes a role from itself, takes itself off, leaves, or hands on the
/// resource it owns can lose the very right the save needs, where a database checks every row. Such a save runs as
/// the resource's own work in its scope, for the caller (<see cref="IOwnPlaceWork{TResourceId, TMemberId}"/>), when the
/// request in hand is the one whose check read the resource and the unit of work holds nothing but the resource.
/// Otherwise every save runs as the caller.
/// </para>
/// <para>
/// <b>What stays yours.</b> The resource, its fields and its constructor, which opens its member list. The hooks the
/// generator declares on it: the guard, <c>OnChangingMembers()</c>, and the events, <c>OnMemberAdded</c>,
/// <c>OnMemberRemoved</c>, <c>OnRoleGiven</c>, <c>OnRoleTaken</c> and <c>OnOwnerNamed</c>. A rule of your own, such as
/// that nobody gives a role for longer than they hold the permission, asked with the access manager before the call.
/// </para>
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
/// <typeparam name="TMember">The application's member class.</typeparam>
/// <typeparam name="TId">The member class's own id.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public class MemberManager<TResource, TResourceId, TMember, TId, TMemberId, TRoleId>
    where TResource : AggregateRoot<TResourceId>, IMembershipResource, IMembershipResource<TMember, TMemberId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The most resources one list of several is asked about.</summary>
    public const int MostResources = MembershipAccessManager.MostResources;

    private readonly MembershipAccessManager<TResource> _access;
    private readonly IMemberStore<TResource, TResourceId, TMemberId> _store;
    private readonly ICurrentUser _user;
    private readonly AccessManager _work;
    private readonly TimeProvider _clock;
    private readonly MemberAdmission<TResourceId, TMemberId, TRoleId> _admission;
    private readonly IOwnPlaceWork<TResourceId, TMemberId>? _ownPlace;

    /// <summary>The member manager of one kind of resource. The container makes it; the generated class has the same constructor.</summary>
    /// <param name="access">The resource's access manager, which asks what the caller holds.</param>
    /// <param name="store">Where the resource is loaded and saved.</param>
    /// <param name="user">Who is calling.</param>
    /// <param name="clock">What "now" is.</param>
    /// <param name="roles">Which roles there are; the rules answer it themselves where the roles are fixed.</param>
    /// <param name="directory">Who may be made a member, or <see langword="null"/> when everybody may.</param>
    /// <param name="policy">Which roles may go to a member, or <see langword="null"/> when every one may.</param>
    /// <param name="ownPlace">How the save of a change to the caller's own place begins, or <see langword="null"/> for system work in the resource's scope.</param>
    public MemberManager(
        MembershipAccessManager<TResource> access,
        IMemberStore<TResource, TResourceId, TMemberId> store,
        ICurrentUser user,
        TimeProvider clock,
        IMemberRoles<TResourceId, TRoleId>? roles = null,
        IMemberDirectory<TResourceId, TMemberId>? directory = null,
        IMemberRolePolicy<TResourceId, TRoleId>? policy = null,
        IOwnPlaceWork<TResourceId, TMemberId>? ownPlace = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(clock);

        _access = access;
        _store = store;
        _user = user;
        _work = new AccessManager(user);
        _clock = clock;
        _ownPlace = ownPlace;

        // Roles in code are known by the rules alone.
        roles ??= access.Rules.Roles.Kind == RolesKind.Fixed && typeof(TRoleId) == typeof(NamedRole)
            ? (IMemberRoles<TResourceId, TRoleId>)(object)new NamedRoles<TResourceId>(access.Rules)
            : null;
        _admission = new MemberAdmission<TResourceId, TMemberId, TRoleId>(access.Rules, typeof(TResource).Name, roles, directory, policy);
    }

    /// <summary>The rules of the resource, with the codes it refuses under.</summary>
    public MembershipRules Rules => _access.Rules;

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// Opens a new resource: the owner its constructor put on its member list is admitted, and the resource is added and
    /// saved. Who may open one is the request's to say: there is no resource yet to hold a permission on.
    /// </summary>
    /// <param name="resource">The resource, just made, with its member list opened in its constructor.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The resource, saved.</returns>
    /// <exception cref="RefusalException"><c>member-not-active</c> for an owner the application's directory does not admit.</exception>
    /// <example>
    /// <code>
    /// var board = await members.OpenAsync(new Board(BoardId.Create(), command.Name, owner, clock.GetUtcNow()), cancellationToken);
    /// </code>
    /// </example>
    public async Task<TResource> OpenAsync(TResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        await _admission.RequireMemberAsync(resource.Owner, cancellationToken).ConfigureAwait(false);
        _store.Add(resource);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return resource;
    }

    /// <summary>
    /// Adds a member, from now until <paramref name="until"/> or for good, with <paramref name="role"/> for the same
    /// period where one is given. Who adds it is the caller, as a member of the resource.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="member">The member.</param>
    /// <param name="role">The role it holds from the start, or <see langword="null"/> for none.</param>
    /// <param name="until">When the membership ends, or <see langword="null"/> for no end.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
    /// <returns>The resource, with the member on it.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-found</c>, <c>not-permitted</c> without the permission that manages the members, <c>member-not-active</c>,
    /// <c>role-not-for-members</c>, what the resource's guard refuses, <c>invalid-period</c> and <c>already-member</c>,
    /// under the resource's codes.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>, or changed while this was saved.</exception>
    /// <example>
    /// <code>
    /// var board = await members.AddAsync(command.Board, command.Seat, command.Role, until: null, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TResource> AddAsync(
        TResourceId resource,
        TMemberId member,
        TRoleId? role,
        DateTimeOffset? until,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
    {
        await RequireAsync(resource, Rules.ManagesMembersPermission, nameof(OnAttribute<object>.ManagesMembers), cancellationToken).ConfigureAwait(false);
        await _admission.RequireMemberAsync(member, cancellationToken).ConfigureAwait(false);
        if (role is { } given)
        {
            await _admission.RequireRoleAsync(given, cancellationToken).ConfigureAwait(false);
        }

        var found = await LoadAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        found.AddMember(member, role, MemberPeriod.Between(now, until), now, _store.CurrentMember);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Adds a member, as <see cref="AddAsync(TResourceId, TMemberId, TRoleId?, DateTimeOffset?, CancellationToken, long?)"/> does with the resource's id.</summary>
    /// <inheritdoc cref="AddAsync(TResourceId, TMemberId, TRoleId?, DateTimeOffset?, CancellationToken, long?)"/>
    public Task<TResource> AddAsync(
        TResource resource,
        TMemberId member,
        TRoleId? role,
        DateTimeOffset? until,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
        => AddAsync(IdOf(resource), member, role, until, cancellationToken, expectedVersion);

    /// <summary>
    /// Gives a member a role, from now until <paramref name="until"/> or for good, next to whatever roles it holds. Who
    /// gives it is the caller, as a member of the resource.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="member">The member.</param>
    /// <param name="role">The role.</param>
    /// <param name="until">When the role ends, or <see langword="null"/> for no end.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
    /// <returns>The resource, with the role given.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-found</c>, <c>not-permitted</c> without the permission that manages the members, <c>role-not-for-members</c>,
    /// what the resource's guard refuses, <c>invalid-period</c>, <c>member-not-found</c>, <c>role-held</c>, and
    /// <c>owner-protected</c> for the owner, under the resource's codes.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>, or changed while this was saved.</exception>
    public async Task<TResource> GiveRoleAsync(
        TResourceId resource,
        TMemberId member,
        TRoleId role,
        DateTimeOffset? until,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
    {
        await RequireAsync(resource, Rules.ManagesMembersPermission, nameof(OnAttribute<object>.ManagesMembers), cancellationToken).ConfigureAwait(false);
        await _admission.RequireRoleAsync(role, cancellationToken).ConfigureAwait(false);

        var found = await LoadAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        found.GiveRole(member, role, MemberPeriod.Between(now, until), now, _store.CurrentMember);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Gives a member a role, as <see cref="GiveRoleAsync(TResourceId, TMemberId, TRoleId, DateTimeOffset?, CancellationToken, long?)"/> does with the resource's id.</summary>
    /// <inheritdoc cref="GiveRoleAsync(TResourceId, TMemberId, TRoleId, DateTimeOffset?, CancellationToken, long?)"/>
    public Task<TResource> GiveRoleAsync(
        TResource resource,
        TMemberId member,
        TRoleId role,
        DateTimeOffset? until,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
        => GiveRoleAsync(IdOf(resource), member, role, until, cancellationToken, expectedVersion);

    /// <summary>
    /// Takes a role from a member, who stays a member with whatever else it holds. Any role can be taken, one that is no
    /// longer in use too. A role the caller takes from itself is saved as the resource's own work (see the class).
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="member">The member.</param>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
    /// <returns>The resource, with the role taken.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-found</c>, <c>not-permitted</c> without the permission that manages the members, what the resource's guard
    /// refuses, <c>member-not-found</c> and <c>role-not-held</c>, under the resource's codes.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>, or changed while this was saved.</exception>
    public async Task<TResource> TakeRoleAsync(
        TResourceId resource,
        TMemberId member,
        TRoleId role,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
    {
        await RequireAsync(resource, Rules.ManagesMembersPermission, nameof(OnAttribute<object>.ManagesMembers), cancellationToken).ConfigureAwait(false);

        var found = await LoadAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false);
        var parts = PartsOf(found);
        found.TakeRole(member, role);
        await SaveAsync(found, OwnPlace(member), parts, cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Takes a role from a member, as <see cref="TakeRoleAsync(TResourceId, TMemberId, TRoleId, CancellationToken, long?)"/> does with the resource's id.</summary>
    /// <inheritdoc cref="TakeRoleAsync(TResourceId, TMemberId, TRoleId, CancellationToken, long?)"/>
    public Task<TResource> TakeRoleAsync(TResource resource, TMemberId member, TRoleId role, CancellationToken cancellationToken, long? expectedVersion = null)
        => TakeRoleAsync(IdOf(resource), member, role, cancellationToken, expectedVersion);

    /// <summary>
    /// Takes a member off the resource, with every role it holds. The owner stays until somebody else is named owner. A
    /// caller that takes itself off is saved as the resource's own work (see the class).
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
    /// <returns>The resource, without the member.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-found</c>, <c>not-permitted</c> without the permission that manages the members, what the resource's guard
    /// refuses, <c>member-not-found</c> and <c>owner-protected</c>, under the resource's codes.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>, or changed while this was saved.</exception>
    public async Task<TResource> RemoveAsync(TResourceId resource, TMemberId member, CancellationToken cancellationToken, long? expectedVersion = null)
    {
        await RequireAsync(resource, Rules.ManagesMembersPermission, nameof(OnAttribute<object>.ManagesMembers), cancellationToken).ConfigureAwait(false);

        var found = await LoadAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false);
        var parts = PartsOf(found);
        found.RemoveMember(member);
        await SaveAsync(found, OwnPlace(member), parts, cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Takes a member off the resource, as <see cref="RemoveAsync(TResourceId, TMemberId, CancellationToken, long?)"/> does with the resource's id.</summary>
    /// <inheritdoc cref="RemoveAsync(TResourceId, TMemberId, CancellationToken, long?)"/>
    public Task<TResource> RemoveAsync(TResource resource, TMemberId member, CancellationToken cancellationToken, long? expectedVersion = null)
        => RemoveAsync(IdOf(resource), member, cancellationToken, expectedVersion);

    /// <summary>
    /// Takes the caller off the resource, with every role it holds there. It needs no permission: the caller sees the
    /// resource, is a member of it and is not its owner. Saved as the resource's own work (see the class).
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
    /// <returns>The resource, without the caller.</returns>
    /// <exception cref="RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> for a resource it does not see; what the resource's
    /// guard refuses; <c>member-not-found</c> for a caller that is no member; <c>owner-protected</c> for the owner. Under
    /// the resource's codes.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>, or changed while this was saved.</exception>
    public async Task<TResource> LeaveAsync(TResourceId resource, CancellationToken cancellationToken, long? expectedVersion = null)
    {
        _access.RequireCurrentUser();
        if (!CheckedInHand<TResourceId>.Read(resource)
            && await _access.HoldAsync(resource, Rules.Permissions[0].Name, cancellationToken).ConfigureAwait(false) is null)
        {
            throw Rules.Codes.Refuse(MembershipRefusals.NotFound);
        }

        var me = _store.CurrentMember ?? throw Rules.Codes.Refuse(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, null));
        var found = await LoadAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false);
        var parts = PartsOf(found);
        found.RemoveMember(me);
        await SaveAsync(found, me, parts, cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Takes the caller off the resource, as <see cref="LeaveAsync(TResourceId, CancellationToken, long?)"/> does with the resource's id.</summary>
    /// <inheritdoc cref="LeaveAsync(TResourceId, CancellationToken, long?)"/>
    public Task<TResource> LeaveAsync(TResource resource, CancellationToken cancellationToken, long? expectedVersion = null)
        => LeaveAsync(IdOf(resource), cancellationToken, expectedVersion);

    /// <summary>
    /// Names another owner: a member from now on and with no end, added when not a member yet, holding no role. The owner
    /// before becomes what <paramref name="formerOwner"/> says: a member in a role, or nobody on the list. The resource's
    /// <c>OnOwnerNamed</c> hook hears everything that changed. An owner that hands on its own resource is saved as the
    /// resource's own work (see the class).
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="owner">The member that is to own the resource.</param>
    /// <param name="formerOwner"><c>FormerOwner.StaysAs(role)</c>, or <c>FormerOwner.Leaves</c>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
    /// <returns>The resource, with its new owner.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-found</c>, <c>not-permitted</c> without the permission that names the owner, <c>member-not-active</c> for
    /// the new owner, <c>role-not-for-members</c> for the role the owner before stays in, what the resource's guard
    /// refuses, and <c>already-owner</c>, under the resource's codes.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>, or changed while this was saved.</exception>
    public async Task<TResource> NameOwnerAsync(
        TResourceId resource,
        TMemberId owner,
        FormerOwner<TRoleId> formerOwner,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(formerOwner);

        await RequireAsync(resource, Rules.NamesOwnerPermission, nameof(OnAttribute<object>.NamesOwner), cancellationToken).ConfigureAwait(false);
        await _admission.RequireMemberAsync(owner, cancellationToken).ConfigureAwait(false);
        if (formerOwner.Role is { } stays)
        {
            await _admission.RequireRoleAsync(stays, cancellationToken).ConfigureAwait(false);
        }

        var found = await LoadAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false);
        var parts = PartsOf(found);
        var before = found.Owner;
        found.NameOwner(owner, formerOwner, _clock.GetUtcNow());
        await SaveAsync(found, OwnPlace(before), parts, cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Names another owner, as <see cref="NameOwnerAsync(TResourceId, TMemberId, FormerOwner{TRoleId}, CancellationToken, long?)"/> does with the resource's id.</summary>
    /// <inheritdoc cref="NameOwnerAsync(TResourceId, TMemberId, FormerOwner{TRoleId}, CancellationToken, long?)"/>
    public Task<TResource> NameOwnerAsync(TResource resource, TMemberId owner, FormerOwner<TRoleId> formerOwner, CancellationToken cancellationToken, long? expectedVersion = null)
        => NameOwnerAsync(IdOf(resource), owner, formerOwner, cancellationToken, expectedVersion);

    // ------------------------------------------------------------------ reads

    /// <summary>
    /// The members of a resource the caller sees, read for showing: the owner first, then by when each membership
    /// starts, then by member, and each member's roles by when each starts, then by role. Members whose membership has
    /// ended, or is still to start, are in it too; <c>member.AppliesAt(now)</c> and <c>member.HoldsAt(role, now)</c> say
    /// what counts at a moment.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Its members, your member class, as they were read.</returns>
    /// <exception cref="RefusalException">The caller's own refusal when it is nobody; <c>not-found</c> for a resource it does not see.</exception>
    public async Task<IReadOnlyList<TMember>> ListAsync(TResourceId resource, CancellationToken cancellationToken)
    {
        _access.RequireCurrentUser();
        var read = await _store.ReadAsync([resource], _access.ReachSeen(), cancellationToken).ConfigureAwait(false);
        return read.Count == 0 ? throw Rules.Codes.Refuse(MembershipRefusals.NotFound) : Ordered(read[0]);
    }

    /// <summary>The members of a resource, as <see cref="ListAsync(TResourceId, CancellationToken)"/> answers them with the resource's id.</summary>
    /// <inheritdoc cref="ListAsync(TResourceId, CancellationToken)"/>
    public Task<IReadOnlyList<TMember>> ListAsync(TResource resource, CancellationToken cancellationToken)
        => ListAsync(IdOf(resource), cancellationToken);

    /// <summary>
    /// The members of each of <paramref name="resources"/> the caller sees, in one statement whatever their number,
    /// each list in the order <see cref="ListAsync(TResourceId, CancellationToken)"/> answers in. A resource the caller
    /// does not see is not in the answer.
    /// </summary>
    /// <param name="resources">The resources, at most <see cref="MostResources"/> different ones.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The members of each resource the caller sees, by its id.</returns>
    /// <exception cref="ArgumentException">More than <see cref="MostResources"/> different resources are asked about.</exception>
    /// <exception cref="RefusalException">The caller's own refusal when it is nobody.</exception>
    public async Task<IReadOnlyDictionary<TResourceId, IReadOnlyList<TMember>>> ListAsync(IReadOnlyCollection<TResourceId> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var about = resources.Distinct().ToList();
        if (about.Count > MostResources)
        {
            throw new ArgumentException(
                "The members are listed for at most " + MostResources + " different resources at once, and these are " + about.Count
                + ". A use case that takes the ids from a request refuses its caller before it asks.",
                nameof(resources));
        }

        _access.RequireCurrentUser();
        if (about.Count == 0)
        {
            return new Dictionary<TResourceId, IReadOnlyList<TMember>>();
        }

        var read = await _store.ReadAsync(about, _access.ReachSeen(), cancellationToken).ConfigureAwait(false);
        return read.ToDictionary(found => found.Id, Ordered);
    }

    // ------------------------------------------------------------------ the steps

    /// <summary>
    /// Requires <paramref name="permission"/> on <paramref name="resource"/>, or takes the answer of the check the request
    /// in hand passed. A resource whose rules mark no such permission lets only system work through.
    /// </summary>
    private async Task RequireAsync(TResourceId resource, string? permission, string setting, CancellationToken cancellationToken)
    {
        if (permission is null)
        {
            if (Rules.IsOwnWork(_user.Snapshot))
            {
                return;
            }

            throw NoneMarked(setting);
        }

        if (CheckedInHand<TResourceId>.Holds(resource, permission))
        {
            return;
        }

        await _access.RequireAsync(resource, permission, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResource> LoadAsync(TResourceId resource, long? expectedVersion, CancellationToken cancellationToken)
        => await _store.FindAsync(resource, expectedVersion, cancellationToken).ConfigureAwait(false)
           ?? throw Rules.Codes.Refuse(MembershipRefusals.NotFound);

    /// <summary>The caller, where <paramref name="member"/> is the caller's own place; <see langword="null"/> otherwise.</summary>
    private TMemberId? OwnPlace(TMemberId member) => _store.CurrentMember is { } me && me.Equals(member) ? me : null;

    /// <summary>
    /// Saves: a change to the caller's own place as the resource's own work in its scope, when the request in hand is the
    /// one whose check read the resource and the unit of work holds nothing but the resource; anything else as the caller.
    /// </summary>
    private async Task SaveAsync(TResource resource, TMemberId? ownPlaceOf, HashSet<object> parts, CancellationToken cancellationToken)
    {
        if (ownPlaceOf is { } me && CheckedInHand<TResourceId>.Read(resource.Id))
        {
            var scope = OwnScope();
            AddPartsOf(resource, parts);
            if (_store.ChangedBesides(parts).Count == 0)
            {
                using (_ownPlace?.Begin(me, scope) ?? (scope is null ? _work.BeginSystemBypassingRowAccess() : _work.BeginSystem(scope)))
                {
                    await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }

                return;
            }
        }

        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The scope of the resource's own work: its module's, where its rules name it, or the first they name; or none, the
    /// application's own work that no row access rule holds, for rules that name no scope.
    /// </summary>
    private string? OwnScope()
        => TResource.MembershipScope is { } own && Rules.SystemScopes.Contains(own, StringComparer.Ordinal)
            ? own
            : Rules.SystemScopes.FirstOrDefault();

    private IReadOnlyList<TMember> Ordered(TResource resource) => MemberOrder.Of<TMember, TId, TMemberId, TRoleId>(resource.Members, resource.Owner);

    /// <summary>The resource, its members and their roles as they are now, each by reference.</summary>
    private static HashSet<object> PartsOf(TResource resource)
    {
        var parts = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AddPartsOf(resource, parts);
        return parts;
    }

    private static void AddPartsOf(TResource resource, HashSet<object> parts)
    {
        parts.Add(resource);
        foreach (var member in resource.Members)
        {
            parts.Add(member);
            foreach (var role in member.Roles)
            {
                parts.Add(role);
            }
        }
    }

    private static TResourceId IdOf(TResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Id;
    }

    /// <summary>The refusal of a change no permission on the resource is marked for: only system work makes it.</summary>
    private RefusalException NoneMarked(string setting)
        => new(
            Rules.Codes[MembershipRefusals.NotPermitted],
            RefusalKind.NotPermitted,
            "Only system work makes this change to " + Rules.Name + ": no permission on " + typeof(TResource).Name + " is marked " + setting + ". Mark the permission that "
            + "makes it [On<" + typeof(TResource).Name + ">(" + setting + " = true)].",
            new Dictionary<string, object?> { ["Permission"] = null });
}
