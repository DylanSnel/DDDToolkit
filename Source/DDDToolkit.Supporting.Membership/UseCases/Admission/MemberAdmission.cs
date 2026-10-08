using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>
/// What a use case asks before it changes the members of one kind of resource, where the answer is not the
/// member list's to give: whether the one to be made a member is known, whether a role is one there is for
/// this resource, and which role an owner holds. A handler asks, and passes the answer to the aggregate:
/// <code>
/// await admission.RequireMemberAsync(command.With, cancellationToken);
/// await admission.RequireRoleAsync(command.Role, cancellationToken);
/// document.ShareWith(command.With, command.Role, MemberPeriod.Between(clock.GetUtcNow(), command.Until), by);
/// </code>
/// <para>
/// It asks what makes a change well formed, whoever makes it: a member that exists, a role that exists. It
/// does not ask who may make the change. Who may add a member or give a role, which roles and for how long,
/// is the application's to decide: by the key its command requires, and by any rule of its own in the
/// handler, in front of these.
/// </para>
/// <para>
/// One per kind of resource, asked for by the resource's id, and it refuses under that resource's codes. It
/// keeps nothing between two questions.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed class MemberAdmission<TResourceId, TMemberId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly MembershipCodes _codes;
    private readonly IMemberRoles<TResourceId, TRoleId> _roles;
    private readonly IMemberDirectory<TResourceId, TMemberId>? _directory;
    private readonly IMemberRolePolicy<TResourceId, TRoleId>? _policy;

    /// <summary>The admission of one kind of resource.</summary>
    /// <param name="rules">The resource's rules, for the codes it refuses under.</param>
    /// <param name="roles">The roles there are for its members.</param>
    /// <param name="directory">Who may be made a member, or <see langword="null"/> when everybody may.</param>
    /// <param name="policy">Which of the roles may go to a member, or <see langword="null"/> when every one may.</param>
    public MemberAdmission(
        MembershipRules rules,
        IMemberRoles<TResourceId, TRoleId> roles,
        IMemberDirectory<TResourceId, TMemberId>? directory = null,
        IMemberRolePolicy<TResourceId, TRoleId>? policy = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(roles);

        _codes = rules.Codes;
        _roles = roles;
        _directory = directory;
        _policy = policy;
    }

    /// <summary>
    /// Requires <paramref name="member"/> to be somebody who may be made a member, or named owner: known and
    /// active, as the application's directory says. Admitted without a question where there is no directory.
    /// </summary>
    /// <param name="member">The one to be made a member.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="Exceptions.RefusalException"><c>member-not-active</c>, with the <c>Member</c>, under the resource's codes.</exception>
    public async ValueTask RequireMemberAsync(TMemberId member, CancellationToken cancellationToken)
    {
        if (_directory is not null && !await _directory.IsActiveAsync(member, cancellationToken).ConfigureAwait(false))
        {
            throw _codes.Refuse(MembershipRefusals.MemberNotActive, (MembershipCodes.DefaultMemberArgument, member));
        }
    }

    /// <summary>
    /// Requires <paramref name="role"/> to be one that goes to a member: a role there is for this resource,
    /// which the application's own rule, where it registered one
    /// (<see cref="IMemberRolePolicy{TResourceId, TRoleId}"/>), does not keep off a member list. What the role
    /// gives is not asked, and neither is who gives it.
    /// </summary>
    /// <param name="role">The role to be given.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="Exceptions.RefusalException"><c>role-not-for-members</c>, with the <c>Role</c>, under the resource's codes.</exception>
    public async ValueTask RequireRoleAsync(TRoleId role, CancellationToken cancellationToken)
    {
        if (!await _roles.ExistsAsync(role, cancellationToken).ConfigureAwait(false)
            || (_policy is not null && !await _policy.MayHoldAsync(role, cancellationToken).ConfigureAwait(false)))
        {
            throw _codes.Refuse(MembershipRefusals.RoleNotForMembers, ("Role", role));
        }
    }

    /// <summary>
    /// The role every owner holds: what a resource is opened with, and what a new owner is given. The
    /// application's own rule about which roles go to a member is not asked: an owner holds the owner's role
    /// whatever else that role is for.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="Exceptions.RefusalException"><c>no-owner-role</c> when there is none in use, under the resource's codes.</exception>
    public async ValueTask<TRoleId> OwnerRoleAsync(CancellationToken cancellationToken)
        => await _roles.FindOwnerRoleAsync(cancellationToken).ConfigureAwait(false) ?? throw _codes.Refuse(MembershipRefusals.NoOwnerRole);

    /// <summary>
    /// The role every owner holds, or <see langword="null"/> when there is none in use: for a use case that
    /// only needs to know which role is the owner's, and goes on without one, as taking a role from the owner
    /// does.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public ValueTask<TRoleId?> FindOwnerRoleAsync(CancellationToken cancellationToken) => _roles.FindOwnerRoleAsync(cancellationToken);
}
