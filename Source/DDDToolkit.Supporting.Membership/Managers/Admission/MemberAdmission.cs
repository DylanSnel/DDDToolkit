using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// What the member manager asks before it changes the members of one kind of resource, where the answer is not the
/// member list's to give: whether the one to be made a member is known, whether a role is one there is for this
/// resource. It asks what makes a change well formed, whoever makes it. Who may make the change the manager asks of
/// the access manager first.
/// <para>
/// One per kind of resource, and it refuses under that resource's codes. It keeps nothing between two questions.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
internal sealed class MemberAdmission<TResourceId, TMemberId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly MembershipCodes _codes;
    private readonly IMemberRoles<TResourceId, TRoleId>? _roles;
    private readonly string _resource;
    private readonly IMemberDirectory<TResourceId, TMemberId>? _directory;
    private readonly IMemberRolePolicy<TResourceId, TRoleId>? _policy;

    /// <summary>The admission of one kind of resource.</summary>
    /// <param name="rules">The resource's rules, for the codes it refuses under.</param>
    /// <param name="resource">The resource's class, for what a missing answer says.</param>
    /// <param name="roles">The roles there are for its members, or <see langword="null"/> where nothing answers that.</param>
    /// <param name="directory">Who may be made a member, or <see langword="null"/> when everybody may.</param>
    /// <param name="policy">Which of the roles may go to a member, or <see langword="null"/> when every one may.</param>
    public MemberAdmission(
        MembershipRules rules,
        string resource,
        IMemberRoles<TResourceId, TRoleId>? roles,
        IMemberDirectory<TResourceId, TMemberId>? directory,
        IMemberRolePolicy<TResourceId, TRoleId>? policy)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _codes = rules.Codes;
        _resource = resource;
        _roles = roles;
        _directory = directory;
        _policy = policy;
    }

    /// <summary>
    /// Requires <paramref name="member"/> to be somebody who may be made a member, or named owner: known and
    /// active, as the application's directory says. Admitted without a question where there is no directory.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>member-not-active</c>, with the <c>Member</c>, under the resource's codes.</exception>
    public async ValueTask RequireMemberAsync(TMemberId member, CancellationToken cancellationToken)
    {
        if (_directory is not null && !await _directory.IsActiveAsync(member, cancellationToken).ConfigureAwait(false))
        {
            throw _codes.Refuse(MembershipRefusals.MemberNotActive, (MembershipCodes.DefaultMemberArgument, member));
        }
    }

    /// <summary>
    /// Requires <paramref name="role"/> to be one that goes to a member: a role there is for this resource, which the
    /// application's own rule, where it registered one (<see cref="IMemberRolePolicy{TResourceId, TRoleId}"/>), does
    /// not keep off a member list.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>role-not-for-members</c>, with the <c>Role</c>, under the resource's codes.</exception>
    /// <exception cref="InvalidOperationException">Nothing answers which roles there are.</exception>
    public async ValueTask RequireRoleAsync(TRoleId role, CancellationToken cancellationToken)
    {
        var roles = _roles ?? throw new InvalidOperationException(
            "Nothing answers which roles there are for the members of " + _resource + ": no IMemberRoles<" + typeof(TResourceId).Name + ", " + typeof(TRoleId).Name
            + "> is registered. Register the resource with its storage package, which answers it for roles in code and roles a customer makes, and answer it yourself "
            + "for roles kept elsewhere.");

        if (!await roles.ExistsAsync(role, cancellationToken).ConfigureAwait(false)
            || (_policy is not null && !await _policy.MayHoldAsync(role, cancellationToken).ConfigureAwait(false)))
        {
            throw _codes.Refuse(MembershipRefusals.RoleNotForMembers, ("Role", role));
        }
    }
}
