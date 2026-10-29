using System.Linq.Expressions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// The roles a customer makes for one kind of resource: making them, the starter roles among them, renaming them,
/// setting their permissions and archiving them, and reading them. Your code takes it as <c>BoardRoleManager</c>, a class
/// the package's generator writes for your role class, closed over it and the resource, and the container makes per
/// scope.
/// <code>
/// public sealed class RenameBoardRoleHandler(BoardRoleManager roles) : ICommandHandler&lt;RenameBoardRole, BoardRole&gt;
/// {
///     public async ValueTask&lt;BoardRole&gt; Handle(RenameBoardRole command, CancellationToken cancellationToken)
///         =&gt; await roles.RenameAsync(command.Role, command.Name, command.Description, cancellationToken);
/// }
/// </code>
/// <para>
/// Each change takes the role, or its id; both load the role and answer it as it was changed and saved. The manager
/// hands the role the resource's rules, so no handler does. Each change needs the permission the rules name for it
/// (<see cref="Roles.ManagedWith"/>), held for the whole of what the roles are kept for: the manager asks it of what the
/// application registered for that (<see cref="ICustomRoleAccess{TResource}"/>), which the join with an organization's
/// package answers for the whole organization. Where the rules name none, or nothing answers, only system work changes the
/// roles.
/// </para>
/// <para>
/// <b>What stays yours.</b> The role class, its constructor and the fields it adds, such as the customer a role is of. Its
/// events: the generator declares the partial methods <c>OnRenamed()</c>, <c>OnPermissionsSet(set)</c> and
/// <c>OnArchived()</c> on it, and the role calls each after a change that changed something. A role class kept per a
/// scope of your own, a garden say, passes that scope to the reads and to the starter roles:
/// <c>scope: role =&gt; role.GardenId == garden</c>.
/// </para>
/// <para>
/// The reads answer the roles there are for the caller: what the context's own rules let through, the roles of the
/// caller's organization for a class marked with the organization package's marker. They ask no permission.
/// </para>
/// </summary>
/// <typeparam name="TRole">The application's role class.</typeparam>
/// <typeparam name="TRoleId">The role class's id.</typeparam>
/// <typeparam name="TResource">The resource the roles are of.</typeparam>
public class CustomRoleManager<TRole, TRoleId, TResource>
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TResource : class
{
    /// <summary>How many ids one question by id takes.</summary>
    public const int MostIds = 200;

    private readonly ICustomRoleStore<TRole, TRoleId> _store;
    private readonly MembershipAccessManager<TResource> _access;
    private readonly ICurrentUser _user;
    private readonly ICustomRoleAccess<TResource>? _roleAccess;

    /// <summary>The role manager of one role class. The container makes it; the generated class has the same constructor.</summary>
    /// <param name="store">Where the roles are loaded and saved.</param>
    /// <param name="access">The access manager of the resource the roles are of, for its rules.</param>
    /// <param name="user">Who is calling.</param>
    /// <param name="roleAccess">Whether the caller may change the roles, or <see langword="null"/> where only system work may.</param>
    public CustomRoleManager(
        ICustomRoleStore<TRole, TRoleId> store,
        MembershipAccessManager<TResource> access,
        ICurrentUser user,
        ICustomRoleAccess<TResource>? roleAccess = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(user);

        _store = store;
        _access = access;
        _user = user;
        _roleAccess = roleAccess;
    }

    /// <summary>The rules of the resource the roles are of: what a role's constructor takes.</summary>
    public MembershipRules Rules => _access.Rules;

    // ------------------------------------------------------------------ commands

    /// <summary>Adds a role made with your class's constructor, and saves it.</summary>
    /// <param name="role">The role, just made.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, saved.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-permitted</c> without the permission that changes the roles; whatever your context refuses a second role of
    /// one name with.
    /// </exception>
    /// <example>
    /// <code>
    /// var role = await roles.CreateAsync(new BoardRole(BoardRoleId.Create(), organization, new CustomRoleDraft(command.Name, command.Description, command.Permissions)), cancellationToken);
    /// </code>
    /// </example>
    public async Task<TRole> CreateAsync(TRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);

        await RequireAsync(cancellationToken).ConfigureAwait(false);
        _store.Add(role);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return role;
    }

    /// <summary>
    /// Makes the starter roles the rules declare that the roles there are for the caller do not have yet, each through
    /// <paramref name="make"/>, and saves them. A starter role is told by what it was made from, so one a customer renamed
    /// or archived is not made again, and asking twice makes nothing the second time.
    /// </summary>
    /// <param name="make">Makes a role of your class from a starter role's draft: its name, its permissions and the starter role it is made from.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="scope">The roles of one scope of your own, for a role class kept per that scope; left out, every role there is for the caller.</param>
    /// <returns>The roles it made, in the order the rules declare them; none when every one is there.</returns>
    /// <exception cref="RefusalException"><c>not-permitted</c> without the permission that changes the roles.</exception>
    /// <example>
    /// <code>
    /// var made = await roles.CreateStarterRolesAsync(draft =&gt; new BoardRole(BoardRoleId.Create(), organization, draft), cancellationToken);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TRole>> CreateStarterRolesAsync(
        Func<CustomRoleDraft, TRole> make,
        CancellationToken cancellationToken,
        Expression<Func<TRole, bool>>? scope = null)
    {
        ArgumentNullException.ThrowIfNull(make);

        await RequireAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _store.ListAsync(scope, tracked: true, cancellationToken).ConfigureAwait(false);
        var missing = StarterRoles.Missing(Rules, existing);
        if (missing.Count == 0)
        {
            return [];
        }

        var made = new List<TRole>(missing.Count);
        foreach (var draft in missing)
        {
            var role = make(draft) ?? throw new InvalidOperationException("The role made for the starter role '" + draft.MadeFrom + "' is null. Make one with your class's constructor.");
            _store.Add(role);
            made.Add(role);
        }

        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return made;
    }

    /// <summary>Renames a role and says again what it is for. The same name and description again change nothing.</summary>
    /// <param name="role">The role.</param>
    /// <param name="name">Its name, as it is to be.</param>
    /// <param name="description">What it is for, as it is to be; <see langword="null"/> clears it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, renamed.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-permitted</c> without the permission that changes the roles; <c>role-not-found</c>, <c>role-archived</c>,
    /// <c>role-name-invalid</c>, under the resource's codes; whatever your context refuses a second role of one name with.
    /// </exception>
    public async Task<TRole> RenameAsync(TRoleId role, string name, string? description, CancellationToken cancellationToken)
    {
        var found = await LoadAsync(role, cancellationToken).ConfigureAwait(false);
        found.Rename(name, description, Rules);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Renames a role, as <see cref="RenameAsync(TRoleId, string, string?, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="RenameAsync(TRoleId, string, string?, CancellationToken)"/>
    public Task<TRole> RenameAsync(TRole role, string name, string? description, CancellationToken cancellationToken)
        => RenameAsync(IdOf(role), name, description, cancellationToken);

    /// <summary>Sets the permissions a role gives, for every member that holds it. The same permissions again change nothing.</summary>
    /// <param name="role">The role.</param>
    /// <param name="permissions">Every permission it gives from now on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, with its new permissions.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-permitted</c> without the permission that changes the roles; <c>role-not-found</c>, <c>role-archived</c>,
    /// <c>permission-not-for-members</c>, under the resource's codes.
    /// </exception>
    public async Task<TRole> SetPermissionsAsync(TRoleId role, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var found = await LoadAsync(role, cancellationToken).ConfigureAwait(false);
        found.SetPermissions(permissions, Rules);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Sets the permissions a role gives, as <see cref="SetPermissionsAsync(TRoleId, IReadOnlyCollection{string}, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="SetPermissionsAsync(TRoleId, IReadOnlyCollection{string}, CancellationToken)"/>
    public Task<TRole> SetPermissionsAsync(TRole role, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
        => SetPermissionsAsync(IdOf(role), permissions, cancellationToken);

    /// <summary>Archives a role: it stays on every member that holds it, gives nothing from now on, and is not given again.</summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, archived.</returns>
    /// <exception cref="RefusalException">
    /// <c>not-permitted</c> without the permission that changes the roles; <c>role-not-found</c> and <c>role-archived</c>,
    /// under the resource's codes.
    /// </exception>
    public async Task<TRole> ArchiveAsync(TRoleId role, CancellationToken cancellationToken)
    {
        var found = await LoadAsync(role, cancellationToken).ConfigureAwait(false);
        found.Archive(Rules);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Archives a role, as <see cref="ArchiveAsync(TRoleId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="ArchiveAsync(TRoleId, CancellationToken)"/>
    public Task<TRole> ArchiveAsync(TRole role, CancellationToken cancellationToken) => ArchiveAsync(IdOf(role), cancellationToken);

    // ------------------------------------------------------------------ reads

    /// <summary>Every role there is for the caller, archived ones included, by name: your class, whole, read for showing.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <param name="scope">The roles of one scope of your own; left out, every role there is for the caller.</param>
    public async Task<IReadOnlyList<TRole>> ListAsync(CancellationToken cancellationToken, Expression<Func<TRole, bool>>? scope = null)
    {
        var roles = await _store.ListAsync(scope, tracked: false, cancellationToken).ConfigureAwait(false);
        return [.. roles.OrderBy(role => role.Name, StringComparer.Ordinal).ThenBy(role => role.Id.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>
    /// The roles of <paramref name="roles"/> there are for the caller, read for showing: for a list that names the roles
    /// its rows hold by id. A role that is not there for the caller is not in the answer.
    /// </summary>
    /// <param name="roles">The roles, at most <see cref="MostIds"/> different ones.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentException">More than <see cref="MostIds"/> different roles are asked about.</exception>
    public async Task<IReadOnlyList<TRole>> ByIdAsync(IReadOnlyCollection<TRoleId> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var about = roles.Distinct().ToList();
        if (about.Count > MostIds)
        {
            throw new ArgumentException(
                "Roles are read by id at most " + MostIds + " at once, and these are " + about.Count + ". A use case that takes the ids from a request refuses its caller before it asks.",
                nameof(roles));
        }

        return about.Count == 0 ? [] : await _store.ByIdAsync(about, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ the steps

    /// <summary>
    /// Requires the caller to be allowed to change the roles: the application's own work for the resource always is;
    /// anybody else holds the permission the rules name, as what the application registered answers.
    /// </summary>
    private async Task RequireAsync(CancellationToken cancellationToken)
    {
        if (Rules.IsOwnWork(_user.Snapshot))
        {
            return;
        }

        var permission = Rules.Roles.ManagedWith;
        if (permission is null)
        {
            throw new RefusalException(
                Rules.Codes[MembershipRefusals.NotPermitted],
                RefusalKind.NotPermitted,
                "Only system work changes the roles of " + Rules.Name + ": the rules name no permission for it. Name the one that does, Roles.Custom(managedWith: ...).",
                new Dictionary<string, object?> { ["Permission"] = null });
        }

        if (_roleAccess is null)
        {
            // Nothing answers whether the caller holds it for the whole of what the roles are kept for: only system work does.
            throw Rules.Codes.Refuse(MembershipRefusals.NotPermitted, ("Permission", permission));
        }

        await _roleAccess.RequireAsync(permission, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TRole> LoadAsync(TRoleId role, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken).ConfigureAwait(false);
        return await _store.FindAsync(role, cancellationToken).ConfigureAwait(false)
               ?? throw Rules.Codes.Refuse(MembershipRefusals.RoleNotFound, ("Role", role));
    }

    private static TRoleId IdOf(TRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return role.Id;
    }
}
